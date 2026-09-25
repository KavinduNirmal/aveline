using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Resolves consent for the inbound path. The repository is the only required dependency, so the gate
/// can be used by services that must not know how consent is stored.
/// </summary>
public sealed class ConsentGateService : IConsentGateService
{
    private readonly ICustomerConsentRepository _consent;
    private readonly ILogger<ConsentGateService> _logger;
    private readonly IConsentTombstoneStore? _tombstones;

    public ConsentGateService(
        ICustomerConsentRepository consent,
        ILogger<ConsentGateService> logger,
        IConsentTombstoneStore? tombstones = null)
    {
        _consent = consent;
        _logger = logger;
        _tombstones = tombstones;
    }

    /// <inheritdoc />
    public Task<ConsentDecision> CheckAsync(
        Guid organizationId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
        => CheckAsync(organizationId, customerId, phoneE164: null, cancellationToken);

    /// <inheritdoc />
    public async Task<ConsentDecision> CheckAsync(
        Guid organizationId,
        Guid? customerId,
        string? phoneE164,
        CancellationToken cancellationToken = default)
    {
        // No customer and no number means there is nobody whose consent could have been recorded.
        // The message still has to be recorded and answered (plan §8.4), so it is processed - with
        // the reason surfaced so the skip counter can distinguish it from a cleared run.
        if (customerId is null && string.IsNullOrWhiteSpace(phoneE164))
        {
            return ConsentDecision.Process(
                ConsentStatuses.AbsentRow, ConsentGateReasons.NoCustomerContext);
        }

        try
        {
            var status = ConsentStatuses.AbsentRow;
            if (customerId is { } id)
            {
                var row = await _consent.GetForCustomerAsync(organizationId, id, cancellationToken);
                // An absent row is `pending` (Pr0's single definition), and pending is not a revocation.
                status = ConsentStatuses.TryNormalize(row?.ConsentStatus) ?? ConsentStatuses.AbsentRow;
            }

            if (status == ConsentStatuses.Revoked)
            {
                return ConsentDecision.Skip(status, ConsentGateReasons.ConsentRevoked);
            }

            // Q-4: an erasure tombstone is a terminal objection, and it matters most exactly when
            // the customer row is already gone (customerId is null) - that is the R-3 case. It is
            // consulted only when the live status is not an explicit grant, so a customer who
            // re-consents after being erased is not blocked forever.
            if (status != ConsentStatuses.Granted
                && _tombstones is not null
                && !string.IsNullOrWhiteSpace(phoneE164)
                && await _tombstones.WasErasedAsync(organizationId, phoneE164, cancellationToken))
            {
                return ConsentDecision.Skip(ConsentStatuses.Revoked, ConsentGateReasons.ConsentRevoked);
            }

            var reason = customerId is null ? ConsentGateReasons.NoCustomerContext : null;
            return ConsentDecision.Process(status, reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed (plan §8.3): a privacy control that fails open processes a customer who
            // may have revoked. It must not throw either - a consent-store outage cannot be allowed
            // to fail inbound message handling, because the webhook would then be retried forever.
            _logger.LogError(
                ex,
                "Consent check failed for organization {OrganizationId}; refusing to process (fail closed).",
                organizationId);
            return ConsentDecision.FailClosed();
        }
    }
}
