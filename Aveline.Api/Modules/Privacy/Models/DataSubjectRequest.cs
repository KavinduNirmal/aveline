namespace Aveline.Api.Modules.Privacy.Models;

/// <summary>
/// The two request kinds the durable data-subject-request log distinguishes (plan §3.3).
/// </summary>
public static class DataSubjectRequestKinds
{
    /// <summary>A data download.</summary>
    public const string Export = "export";

    /// <summary>An erasure.</summary>
    public const string Delete = "delete";
}

/// <summary>
/// The request lifecycle the durable log records (plan §3.3). The status is the <i>request's</i>
/// outcome, never a consent state: the consent tombstone is a separate row.
/// </summary>
public static class DataSubjectRequestStatuses
{
    /// <summary>The request arrived but no code has been proven yet.</summary>
    public const string Received = "received";

    /// <summary>The caller proved the phone with an OTP.</summary>
    public const string Verified = "verified";

    /// <summary>The export was returned or the erasure committed.</summary>
    public const string Completed = "completed";

    /// <summary>The work failed; <c>FailureReason</c> says why.</summary>
    public const string Failed = "failed";

    /// <summary>The verification window lapsed before the work was attempted.</summary>
    public const string Expired = "expired";
}

/// <summary>
/// Durable idempotency + audit for the anonymous export and delete routes (plan §3.3, §7.5).
/// <c>IdempotencyRecord</c> covers 24-hour HTTP replay; a deletion request record must outlive that
/// window, and the record <i>is</i> the proof that a previous request ran.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>ResultJson</c> stores counts, never content.</b> A deletion that returned names, addresses
/// or message text would itself be personal data inside the table the deletion audit is supposed to
/// survive, which would put the record back in scope for the next erasure.
/// </para>
/// <para>
/// <b><c>CustomerId</c> is <c>SET NULL</c> on purpose.</b> The record survives erasure, and after the
/// customer row is gone the only link left is <see cref="PhoneHash"/> — a fingerprint, not a number.
/// The unique index over (organisation, kind, idempotency key) is what makes a repeat request return
/// the stored result instead of deleting twice.
/// </para>
/// </remarks>
public class DataSubjectRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The customer the request named, or <c>null</c> after an erasure nulled it.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>One of <see cref="DataSubjectRequestKinds"/>.</summary>
    public string Kind { get; set; } = DataSubjectRequestKinds.Export;

    /// <summary>One of <see cref="DataSubjectRequestStatuses"/>.</summary>
    public string Status { get; set; } = DataSubjectRequestStatuses.Received;

    /// <summary>
    /// The deterministic fingerprint of the E.164 number (<c>PhoneFingerprint.Of</c>). Never the
    /// number: it lets a repeat request match after the customer row is gone without storing PII.
    /// </summary>
    public string PhoneHash { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public DateTime? VerifiedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Counts only — <c>{ "memories": 41, "messages": 12, ... }</c>. Never content.</summary>
    public string? ResultJson { get; set; }

    /// <summary>A bounded, non-personal failure reason.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Caller-supplied and unique per (organisation, kind).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
