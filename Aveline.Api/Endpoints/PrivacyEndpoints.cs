using System.Security.Cryptography;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.Common;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Models;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Endpoints;

/// <summary>
/// The anonymous opt-out flow (plan §5.2, §11 Phase 4). Both routes are <b>anonymous by design</b>:
/// the OTP is the authentication, and the caller is a customer who has no Aveline account. There is
/// no JWT and there must be no boutique-scoped route that reaches the same code (plan §9.2,
/// "Access control on deletion").
/// </summary>
/// <remarks>
/// <para>
/// <b>Anti-enumeration is a wire contract, not a nicety.</b> <c>start</c> answers the same
/// <c>202 {"status":"accepted","handle":"…","expiresInSeconds":300}</c> field set whether or not the
/// number belongs to a customer, whether or not the boutique has WhatsApp configured, and whether
/// or not the send succeeded: the status and the TTL are constant and the handle is a fresh opaque
/// value on every path, so nothing on the wire encodes existence. The handle is what the client
/// returns on <c>verify</c> (plan §5.3: "returned to the client"), and for a number we do not know
/// it simply has no stored code behind it. The only difference is server-side. <c>verify</c> answers
/// one <c>400 {"code":"otp-invalid"}</c> for an unknown handle, an expired code, a replay, a wrong
/// code and a spent attempt budget.
/// </para>
/// <para>
/// <b>Fail closed.</b> The OTP counters and the store are the only components that can answer
/// <c>503</c>, and they do so whenever the cache is unreachable (DR-6). Nothing in the flow fails
/// open.
/// </para>
/// <para>
/// <b>Never logs a code or an unmasked number.</b> The identifiers are the organization, the handle
/// and a hashed address.
/// </para>
/// </remarks>
public static class PrivacyEndpoints
{
    /// <summary>The response body <c>start</c> returns for every input that passed validation.</summary>
    public const string AcceptedStatus = "accepted";

    /// <summary>The single error code <c>verify</c> returns for every failed verification.</summary>
    public const string OtpInvalidCode = "otp-invalid";

    /// <summary>Verification attempts a single address may make per hour, before any OTP work.</summary>
    public const int MaxVerifiesPerIpPerHour = 30;

    internal static readonly TimeSpan VerifyIpWindow = TimeSpan.FromHours(1);

    /// <summary>The two scope literals on the wire.</summary>
    public const string ScopeOrg = "org";
    public const string ScopeAll = "all";

    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/privacy")
            .WithTags("Privacy")
            .AllowAnonymous();

        group.MapPost("/opt-out/start", StartAsync)
            .WithName("StartPrivacyOptOut")
            .WithSummary("Request an opt-out code for a phone number. Always answers the same 202.")
            .Produces<OptOutStartResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/opt-out/verify", VerifyAsync)
            .WithName("VerifyPrivacyOptOut")
            .WithSummary("Verify the opt-out code and revoke consent (org-scoped or global).")
            .Produces<OptOutVerifyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // Phase 5 (plan §7.2): the OTP-gated data-subject-rights routes. Both verify the code
        // *inside the same request* - there is no session to carry a verified state between calls,
        // which is why the request shape carries the handle (the reference the code was minted
        // under) together with the code.
        group.MapPost("/data/export", ExportDataAsync)
            .WithName("ExportDataSubjectData")
            .WithSummary("Return the caller's full record inline, after verifying an OTP.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/data/delete", DeleteDataAsync)
            .WithName("DeleteDataSubjectData")
            .WithSummary("Erase the caller's data after verifying an OTP and an explicit confirmation word.")
            .Produces<DataDeleteResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>
    /// §7.2 export. The code is verified here, in the same request as the export, because there is no
    /// session to bind a prior verification to (plan §7.1). The document is returned <b>inline</b>
    /// (DR-4): the only email channel logs its payload, and there is no object store for a short-lived
    /// link. <c>Cache-Control: no-store</c> keeps a shared cache from retaining PII.
    /// </summary>
    private static async Task<IResult> ExportDataAsync(
        DataExportRequest request,
        HttpContext http,
        IOtpService otp,
        IDataSubjectExportService export,
        IOrganizationRepository organizations,
        IDataSubjectRequestLog requestLog,
        IRateLimiter rateLimiter,
        IAuditService audit,
        OtpMetrics otpMetrics,
        RightsMetrics rightsMetrics,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Aveline.Privacy.DataExport");

        var phone = PhoneNormalizer.ToE164(request.PhoneNumber);
        if (phone is null)
        {
            return InvalidPhone();
        }

        if (!TryResolveFormat(request.Format, out var format))
        {
            return Results.BadRequest(new
            {
                code = "invalid-format",
                message = "format must be 'json' or 'csv'.",
            });
        }

        if (!await TryAllowAsync(rateLimiter, http, "export", phone, ct))
        {
            otpMetrics.RecordRateLimited(PrivacyRateLimitReasons.RightsIpBudget);
            rightsMetrics.RecordExportRequest(DataSubjectOutcomes.Unavailable);
            return TooManyRequests("Too many data downloads were requested. Try again later.");
        }

        var verification = await otp.VerifyAsync(request.Handle, request.Otp, ct);
        if (!verification.Success)
        {
            if (verification.Failure == OtpVerifyFailure.StoreUnavailable)
            {
                rightsMetrics.RecordExportRequest(DataSubjectOutcomes.Unavailable);
                return OtpUnavailable();
            }

            rightsMetrics.RecordExportRequest(DataSubjectOutcomes.VerificationFailed);
            return OtpInvalid();
        }

        // The number the code proved - never the one in the body.
        var proven = verification.PhoneE164!;
        if (!string.Equals(proven, phone, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Data export refused: the code proved a different number than the body named. organizationId={OrganizationId}",
                request.OrganizationId);
            rightsMetrics.RecordExportRequest(DataSubjectOutcomes.VerificationFailed);
            return OtpInvalid();
        }

        var document = await export.BuildAsync(request.OrganizationId, proven, ct);
        if (document is null)
        {
            // The caller proved the number, so "no record" is not an enumeration probe.
            rightsMetrics.RecordExportRequest(DataSubjectOutcomes.NotFound);
            return Results.NotFound(new
            {
                code = "no-data",
                message = "No record was found for that number at this boutique.",
            });
        }

        var generatedAt = document.Document.GetProperty("generatedAtUtc").GetDateTime();
        await requestLog.RecordAsync(
            new DataSubjectRequestRecord(
                request.OrganizationId,
                document.CustomerId,
                DataSubjectRequestKinds.Export,
                PhoneFingerprint.Of(proven),
                IdempotencyKey: null,
                document.Counts),
            ct);

        await audit.RecordAsync(
            new AuditEntryRequest(
                Action: AuditAction.DataExportCompleted,
                EntityType: "DataSubjectRequest",
                EntityId: document.CustomerId.ToString("D"),
                OrganizationId: request.OrganizationId,
                ActorKind: ConsentActorKinds.Customer,
                ActorRef: PhoneFingerprint.Of(proven),
                After: document.Counts,
                IpHash: MetricDimensionHasher.HashIp(
                    http.Connection.RemoteIpAddress?.ToString(), ResolveIpSalt(http)),
                UserAgent: http.Request.Headers.UserAgent.ToString()),
            ct);

        var organization = await organizations.GetByIdAsync(request.OrganizationId, ct);
        var slug = organization?.Slug ?? request.OrganizationId.ToString("D");
        var fileName = DataSubjectExportCsv.FileName(slug, generatedAt, format);

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";

        rightsMetrics.RecordExportRequest(DataSubjectOutcomes.Completed);

        if (format == "csv")
        {
            // A ZIP of CSVs plus MANIFEST.json, never one flattened file (plan §7.4).
            return Results.File(DataSubjectExportCsv.BuildZip(document), "application/zip", fileName);
        }

        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        return Results.Text(document.Document.GetRawText(), "application/json", System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// §7.2 delete. <c>confirm</c> is a second factor of intent checked before any OTP work, so a
    /// misdirected request cannot even reach the code path. The erasure itself is idempotent by key
    /// (§7.5).
    /// </summary>
    private static async Task<IResult> DeleteDataAsync(
        DataDeleteRequest request,
        HttpContext http,
        IOtpService otp,
        IErasureService erasure,
        OtpMetrics otpMetrics,
        RightsMetrics rightsMetrics,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Aveline.Privacy.DataDelete");

        if (!string.Equals(request.Confirm?.Trim(), ConfirmWord, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                code = "confirm-required",
                message = $"Send \"confirm\": \"{ConfirmWord}\" to erase. This is deliberate.",
            });
        }

        var key = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest(new
            {
                code = "idempotency-key-required",
                message = "idempotencyKey is required so a retry cannot delete twice.",
            });
        }

        if (key.Length > MaxIdempotencyKeyLength)
        {
            return Results.BadRequest(new
            {
                code = "invalid-idempotency-key",
                message = $"idempotencyKey must be at most {MaxIdempotencyKeyLength} characters.",
            });
        }

        var phone = PhoneNormalizer.ToE164(request.PhoneNumber);
        if (phone is null)
        {
            return InvalidPhone();
        }

        if (!TryResolveScope(request.Scope, out var scope))
        {
            return Results.BadRequest(new
            {
                code = "invalid-scope",
                message = "scope must be 'org' or 'all'.",
            });
        }

        var erasureScope = scope == ConsentRevocationScope.All ? ErasureScope.All : ErasureScope.Org;

        // §7.5 idempotency, checked before the code: a network retry carries the same key but a
        // *spent* OTP (the code is single-use), so a retry that had to re-verify would be refused
        // instead of returning its stored result. This lookup changes nothing and returns counts.
        if (await erasure.FindCompletedAsync(request.OrganizationId, key, erasureScope, ct) is { } replay)
        {
            // A replay returns the stored result; it is a completed request, and the erasure itself
            // is not re-run, so only the counter (not the duration histogram) is recorded.
            rightsMetrics.RecordDeleteRequest(DataSubjectOutcomes.Completed);
            return Results.Ok(ToResponse(replay));
        }

        var verification = await otp.VerifyAsync(request.Handle, request.Otp, ct);
        if (!verification.Success)
        {
            if (verification.Failure == OtpVerifyFailure.StoreUnavailable)
            {
                rightsMetrics.RecordDeleteRequest(DataSubjectOutcomes.Unavailable);
                return OtpUnavailable();
            }

            rightsMetrics.RecordDeleteRequest(DataSubjectOutcomes.VerificationFailed);
            return OtpInvalid();
        }

        var proven = verification.PhoneE164!;
        if (!string.Equals(proven, phone, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Data deletion refused: the code proved a different number than the body named. organizationId={OrganizationId}",
                request.OrganizationId);
            rightsMetrics.RecordDeleteRequest(DataSubjectOutcomes.VerificationFailed);
            return OtpInvalid();
        }

        ErasureResult result;
        try
        {
            result = await erasure.EraseAsync(
                new ErasureRequest(
                    request.OrganizationId,
                    proven,
                    erasureScope,
                    key,
                    new ConsentActor(
                        Kind: ConsentActorKinds.Customer,
                        Source: ConsentSources.OtpLink,
                        ActorRef: PhoneFingerprint.Of(proven),
                        IpHash: MetricDimensionHasher.HashIp(
                            http.Connection.RemoteIpAddress?.ToString(), ResolveIpSalt(http)),
                        UserAgent: http.Request.Headers.UserAgent.ToString()),
                    Reason: "customer_otp_erasure"),
                ct);
        }
        catch (ErasureInProgressException)
        {
            rightsMetrics.RecordDeleteRequest(DataSubjectOutcomes.Conflict);
            return Results.Json(
                new
                {
                    code = "erasure-in-progress",
                    message = "An erasure for this number is already running. Retry shortly.",
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        rightsMetrics.RecordDeleteRequest(DataSubjectOutcomes.Completed);
        return Results.Ok(ToResponse(result));
    }

    private static DataDeleteResponse ToResponse(ErasureResult result)
        => new(
            "completed",
            result.RequestId,
            result.CompletedAtUtc,
            result.Counts,
            result.OrganizationsAffected,
            result.Replayed);

    /// <summary>The explicit second factor of intent (§7.2).</summary>
    public const string ConfirmWord = "DELETE";

    /// <summary>Matches the <c>IdempotencyKey</c> column width.</summary>
    public const int MaxIdempotencyKeyLength = 64;

    /// <summary>
    /// §5.2 step 2. Validates the signed link, normalizes the phone, charges the per-IP and per-phone
    /// budgets, and - only when a customer exists and the boutique has a WhatsApp channel - mints and
    /// sends a code. The response field set is identical on every path; the handle is minted up front
    /// and returned on all of them (plan §5.3), while the code itself is only ever issued, stored and
    /// sent on the path that found a customer and a channel.
    /// </summary>
    private static async Task<IResult> StartAsync(
        OptOutStartRequest request,
        HttpContext http,
        IPrivacyLinkSigner linkSigner,
        IOtpService otp,
        IOtpDeliveryService delivery,
        ICustomerRepository customers,
        IOrganizationRepository organizations,
        IAuditService audit,
        OtpMetrics otpMetrics,
        PrivacyDeliveryMetrics deliveryMetrics,
        IPrivacyNotificationService notifications,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Aveline.Privacy.OptOut");

        // Mint the handle before any lookup so every 202 path - including the unknown-phone and the
        // unconfigured-boutique paths - can return one. It carries no information on its own: it is
        // 32 random bytes, so a handle with a stored code behind it is indistinguishable from one
        // without, and verify answers the single otp-invalid when there is no digest (the same
        // answer a wrong code gets). The path that actually stores a code returns the handle
        // IOtpService minted instead, because the stored digest is keyed by that handle.
        var handle = CreateOpaqueHandle();

        // Step 0: the link must be one we signed. A caller cannot point the flow at an arbitrary
        // boutique. The body is still answered with the constant 202 so a guessed organization does
        // not stand out from a real one.
        if (!linkSigner.Verify(request.OrganizationId, request.Version, request.Signature))
        {
            logger.LogWarning("Opt-out start with an invalid link signature; answering the constant 202.");
            return Accepted(handle);
        }

        var phone = PhoneNormalizer.ToE164(request.PhoneNumber);
        if (phone is null)
        {
            // A malformed number is a client mistake, not an existence probe, and it is answered
            // before any lookup so no customer state is touched.
            return Results.BadRequest(new
            {
                code = "invalid-phone-number",
                message = "Enter a Sri Lankan mobile number, for example 0771234567.",
            });
        }

        if (!TryResolveScope(request.Scope, out var scope))
        {
            return Results.BadRequest(new
            {
                code = "invalid-scope",
                message = "scope must be 'org' or 'all'.",
            });
        }

        var allowed = await otp.TryStartAsync(
            http.Connection.RemoteIpAddress?.ToString() ?? string.Empty, phone, ct);
        if (allowed is null)
        {
            // The counter store is down. Fail closed with a distinct code so an outage is never
            // reported as "you asked too often" (DR-6).
            return Results.Json(
                new
                {
                    code = "otp-unavailable",
                    message = "The opt-out service is unavailable. Try again shortly.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (allowed is false)
        {
            return Results.Json(
                new
                {
                    code = "otp-rate-limited",
                    message = "Too many codes were requested. Try again later.",
                },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var customer = await customers.GetByPhoneAsync(request.OrganizationId, phone, ct);
        if (customer is null)
        {
            // Anti-enumeration: the same body and status as the success path. Nothing is sent, and
            // the log line names only the organization.
            logger.LogInformation(
                "Opt-out start for an unknown number in organization {OrganizationId}.",
                request.OrganizationId);
            return Accepted(handle);
        }

        if (!await delivery.IsConfiguredAsync(request.OrganizationId, ct))
        {
            // The customer exists but there is no channel to reach them. Identical body: the caller
            // must not learn that the boutique exists but has not connected WhatsApp.
            logger.LogInformation(
                "Opt-out start could not be delivered: organization {OrganizationId} has no WhatsApp channel.",
                request.OrganizationId);
            deliveryMetrics.RecordFailed(PrivacyDeliveryKinds.Otp, "not_configured");
            return Accepted(handle);
        }

        OtpIssueResult issued;
        try
        {
            issued = await otp.IssueAsync(request.OrganizationId, phone, ct);
        }
        catch (OtpStoreUnavailableException)
        {
            // The code could not be stored, so it could never verify. 503, never "accepted".
            return Results.Json(
                new
                {
                    code = "otp-unavailable",
                    message = "The opt-out service is unavailable. Try again shortly.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        otpMetrics.RecordIssued();

        var delivery1 = await delivery.SendAsync(request.OrganizationId, phone, issued.Handle, issued.Code, ct);
        if (delivery1.IsSuccess)
        {
            deliveryMetrics.RecordDelivered(PrivacyDeliveryKinds.Otp);
        }
        else
        {
            var reason = delivery1.Skipped
                ? PrivacyDeliveryFailureReasons.NotConfigured
                : PrivacyDeliveryFailureReasons.Provider;
            deliveryMetrics.RecordFailed(PrivacyDeliveryKinds.Otp, reason);

            // Phase 6 item 6.2: the customer cannot complete an opt-out. That is a rights-accessibility
            // failure, so the owner is told. Identifiers only: never the number or the code. The
            // response stays byte-identical (anti-enumeration), so only the server-side record differs.
            if (!delivery1.Skipped)
            {
                await notifications.PrivacyDeliveryFailedAsync(
                    request.OrganizationId,
                    customer.Id,
                    PrivacyDeliveryKinds.Otp,
                    reason,
                    new Dictionary<string, string?> { ["channel"] = OtpDeliveryService.ChannelKey },
                    ct);
            }
        }

        await audit.RecordAsync(
            new AuditEntryRequest(
                Action: AuditAction.OtpIssued,
                EntityType: "CustomerConsent",
                EntityId: customer.Id.ToString("D"),
                OrganizationId: request.OrganizationId,
                ActorKind: ConsentActorKinds.Customer,
                ActorRef: PhoneFingerprint.Of(phone),
                After: new { scope = ScopeName(scope) },
                IpHash: MetricDimensionHasher.HashIp(
                    http.Connection.RemoteIpAddress?.ToString(), ResolveIpSalt(http)),
                UserAgent: http.Request.Headers.UserAgent.ToString()),
            ct);

        // The status and the field set are the same whether or not the code reached the customer;
        // only the server-side record differs.
        return Accepted(issued.Handle);
    }

    /// <summary>
    /// §5.2 step 3. Verifies the code, then revokes with the requested scope and audits both the
    /// verification and the revocation. Every failed verification answers the same 400.
    /// </summary>
    private static async Task<IResult> VerifyAsync(
        OptOutVerifyRequest request,
        HttpContext http,
        IPrivacyLinkSigner linkSigner,
        IOtpService otp,
        IConsentRevoker revoker,
        IOrganizationRepository organizations,
        IDisclosureDispatchQueue acknowledgementQueue,
        IAuditService audit,
        IDistributedCache cache,
        OtpMetrics otpMetrics,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Aveline.Privacy.OptOut");

        // The same signed link is required as at start: global revocation is "every organisation
        // whose records match this proven number", and the signature is what makes the issuing
        // boutique part of the request rather than an arbitrary scalar.
        if (!linkSigner.Verify(request.OrganizationId, request.Version, request.Signature))
        {
            return OtpInvalid();
        }

        if (!TryResolveScope(request.Scope, out var scope))
        {
            return OtpInvalid();
        }

        var allowed = await TryChargeVerifyBudgetAsync(cache, http, ct);
        if (allowed is null)
        {
            // The counter store is unreachable. Refuse rather than leave verification unmetered.
            otpMetrics.RecordRateLimited(PrivacyRateLimitReasons.StoreUnavailable);
            return Results.Json(
                new
                {
                    code = "otp-unavailable",
                    message = "The opt-out service is unavailable. Try again shortly.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (allowed is false)
        {
            otpMetrics.RecordRateLimited(PrivacyRateLimitReasons.VerifyIpBudget);
            return Results.Json(
                new
                {
                    code = "otp-rate-limited",
                    message = "Too many verification attempts. Try again later.",
                },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var verification = await otp.VerifyAsync(request.Handle, request.Otp, ct);
        if (!verification.Success)
        {
            if (verification.Failure == OtpVerifyFailure.StoreUnavailable)
            {
                return Results.Json(
                    new
                    {
                        code = "otp-unavailable",
                        message = "The opt-out service is unavailable. Try again shortly.",
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            otpMetrics.RecordFailed(verification.Failure.ToString());
            logger.LogInformation(
                "Opt-out verification refused. organizationId={OrganizationId} reason={Reason}",
                request.OrganizationId, verification.Failure);
            return OtpInvalid();
        }

        // The number the code proved - never the number in the body. The digest binds them, and the
        // service reports the proven one.
        var phone = verification.PhoneE164!;

        var actor = new ConsentActor(
            Kind: ConsentActorKinds.Customer,
            Source: ConsentSources.OtpLink,
            ActorRef: PhoneFingerprint.Of(phone),
            IpHash: MetricDimensionHasher.HashIp(
                http.Connection.RemoteIpAddress?.ToString(), ResolveIpSalt(http)),
            UserAgent: http.Request.Headers.UserAgent.ToString());

        var organization = await organizations.GetByIdAsync(request.OrganizationId, ct);

        var result = await revoker.RevokeAsync(
            new ConsentRevocationRequest(
                request.OrganizationId,
                CustomerId: null,
                PhoneE164: phone,
                Scope: scope,
                Actor: actor,
                Reason: "customer_otp_opt_out"),
            ct);

        otpMetrics.RecordVerified();

        await audit.RecordAsync(
            new AuditEntryRequest(
                Action: AuditAction.OtpVerified,
                EntityType: "CustomerConsent",
                EntityId: PhoneFingerprint.Of(phone),
                OrganizationId: request.OrganizationId,
                ActorKind: ConsentActorKinds.Customer,
                ActorRef: PhoneFingerprint.Of(phone),
                After: new
                {
                    scope = ScopeName(scope),
                    rowsRevoked = result.RowsRevoked,
                },
                IpHash: actor.IpHash,
                UserAgent: actor.UserAgent),
            ct);

        // One non-personalised acknowledgement per 24 hours, drained off the request path by the
        // privacy worker (plan §11 item 4.5). A refused enqueue loses the confirmation, not the
        // revocation, so it is logged and never fails the customer's opt-out.
        if (organization is not null
            && result.AcknowledgementCustomerId(request.OrganizationId) is { } acknowledgementCustomerId)
        {
            var queued = await acknowledgementQueue.EnqueueAcknowledgementAsync(
                new OptOutAcknowledgementIntent(
                    request.OrganizationId, acknowledgementCustomerId, phone, organization.Name),
                ct);
            if (!queued)
            {
                logger.LogWarning(
                    "The opt-out acknowledgement was not queued (queue full). organizationId={OrganizationId}",
                    request.OrganizationId);
            }
        }

        return Results.Ok(new OptOutVerifyResponse(
            "revoked", ScopeName(scope), result.EffectiveAtUtc));
    }

    /// <summary>
    /// The per-address verification budget. Returns <c>null</c> when the store cannot be read, so the
    /// caller can answer <c>503</c> rather than leaving verification unmetered (DR-6).
    /// </summary>
    private static async Task<bool?> TryChargeVerifyBudgetAsync(
        IDistributedCache cache, HttpContext http, CancellationToken ct)
    {
        var key = $"otp:verify:ip:{OtpService.IpFingerprint(http.Connection.RemoteIpAddress?.ToString())}";

        try
        {
            var raw = await cache.GetStringAsync(key, ct);
            var count = int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 0;
            count++;

            await cache.SetStringAsync(
                key,
                count.ToString(),
                new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = VerifyIpWindow,
                },
                ct);

            return count <= MaxVerifiesPerIpPerHour;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool TryResolveScope(string? scope, out ConsentRevocationScope resolved)
    {
        switch (scope?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case ScopeOrg:
                resolved = ConsentRevocationScope.Org;
                return true;
            case ScopeAll:
                resolved = ConsentRevocationScope.All;
                return true;
            default:
                resolved = ConsentRevocationScope.Org;
                return false;
        }
    }

    private static string ScopeName(ConsentRevocationScope scope)
        => ConsentRevocationResult.ScopeName(scope);

    /// <summary>
    /// The <c>202</c> answer every validated <c>start</c> path returns. The handle and the TTL are
    /// the plan's documented shape (plan §5.3); the status keeps its shipped <c>accepted</c> value.
    /// </summary>
    private static IResult Accepted(string handle)
        => Results.Accepted(value: new OptOutStartResponse(
            AcceptedStatus, handle, (int)OtpService.CodeTtl.TotalSeconds));

    /// <summary>
    /// A fresh 32-byte base64url handle, the same shape <c>OtpService</c> mints, so a handle with a
    /// stored code behind it cannot be told from one without. Also used for the paths on which no
    /// code is stored, so every <c>202</c> carries one.
    /// </summary>
    private static string CreateOpaqueHandle()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The single failed-verification answer (anti-enumeration).</summary>
    private static IResult OtpInvalid()
        => Results.BadRequest(new
        {
            code = OtpInvalidCode,
            message = "The code is invalid, expired or was already used.",
        });

    private static IResult OtpUnavailable()
        => Results.Json(
            new
            {
                code = "otp-unavailable",
                message = "The privacy service is unavailable. Try again shortly.",
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult InvalidPhone()
        => Results.BadRequest(new
        {
            code = "invalid-phone-number",
            message = "Enter a Sri Lankan mobile number, for example 0771234567.",
        });

    private static IResult TooManyRequests(string message)
        => Results.Json(
            new { code = "rate-limited", message },
            statusCode: StatusCodes.Status429TooManyRequests);

    private static bool TryResolveFormat(string? format, out string resolved)
    {
        switch (format?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "json":
                resolved = "json";
                return true;
            case "csv":
                resolved = "csv";
                return true;
            default:
                resolved = "json";
                return false;
        }
    }

    /// <summary>
    /// The §7.5 per-phone and per-address budget. Unlike the OTP counters (which fail closed, DR-6),
    /// this is availability protection around an already-authenticated request, so a limiter failure
    /// does not block it: the OTP is the security control, not this.
    /// </summary>
    private static async Task<bool> TryAllowAsync(
        IRateLimiter rateLimiter, HttpContext http, string scope, string phoneE164, CancellationToken ct)
    {
        try
        {
            var phoneKey = $"privacy:{scope}:phone:{PhoneFingerprint.Of(phoneE164)}";
            var ipKey = $"privacy:{scope}:ip:{OtpService.IpFingerprint(http.Connection.RemoteIpAddress?.ToString())}";

            var phoneAllowed = await rateLimiter.TryAllowAsync(phoneKey, limit: 10, TimeSpan.FromHours(1), ct);
            var ipAllowed = await rateLimiter.TryAllowAsync(ipKey, limit: 30, TimeSpan.FromHours(1), ct);
            return phoneAllowed && ipAllowed;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return true;
        }
    }

    private static string? ResolveIpSalt(HttpContext http)
        => http.RequestServices.GetService(typeof(IConfiguration)) is IConfiguration configuration
            ? configuration["Telemetry:IpHashSalt"]
            : null;
}

/// <summary>The <c>start</c> body. <c>v</c> and <c>s</c> come from the signed link.</summary>
public sealed record OptOutStartRequest(
    Guid OrganizationId,
    string PhoneNumber,
    string? Scope,
    string? Version,
    string? Signature);

/// <summary>
/// The <c>start</c> answer. The field set is constant and carries nothing that encodes whether the
/// phone exists (anti-enumeration): <see cref="Status"/> is the constant <c>accepted</c>,
/// <see cref="Handle"/> is a fresh opaque 32-byte base64url reference the client returns on
/// <c>verify</c> - with no stored code behind it when no customer matched - and
/// <see cref="ExpiresInSeconds"/> is the code lifetime the client should show.
/// </summary>
public sealed record OptOutStartResponse(string Status, string Handle, int ExpiresInSeconds);

/// <summary>The <c>verify</c> body.</summary>
public sealed record OptOutVerifyRequest(
    Guid OrganizationId,
    string? Otp,
    string? Handle,
    string? Scope,
    string? Version,
    string? Signature);

/// <summary>What <c>verify</c> returns on success.</summary>
public sealed record OptOutVerifyResponse(string Status, string Scope, DateTime EffectiveAtUtc);

/// <summary>
/// The §7.2 export body. The plan's literal shape is <c>{ phone_number, org_id, otp }</c>; the
/// <c>handle</c> is the additional, load-bearing member the shipped OTP contract requires — the code
/// is only verifiable against the handle it was minted under (see <c>IOtpService</c>).
/// </summary>
public sealed record DataExportRequest(
    Guid OrganizationId,
    string? PhoneNumber,
    string? Handle,
    string? Otp,
    string? Format);

/// <summary>The §7.2 delete body. <c>confirm</c> must be the literal <c>DELETE</c>.</summary>
public sealed record DataDeleteRequest(
    Guid OrganizationId,
    string? PhoneNumber,
    string? Handle,
    string? Otp,
    string? Confirm,
    string? Scope,
    string? IdempotencyKey);

/// <summary>The §7.2 delete answer: the counts, and whether this was a replay of an earlier key.</summary>
public sealed record DataDeleteResponse(
    string Status,
    Guid RequestId,
    DateTime DeletedAtUtc,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<Guid> OrganizationsAffected,
    bool Replayed);
