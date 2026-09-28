using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.Privacy.Models;

/// <summary>
/// The anonymised consent tombstone that survives erasure (plan §7.3 <c>Customer_Consent</c> row,
/// §15 Q-4, risk R-3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this table exists.</b> The erasure hard-deletes the <c>Customers</c> row, and
/// <c>CustomerConsent</c> cascades with it. The inbound path treats "no customer yet" as
/// <c>pending</c> and re-creates both rows on the next message (<c>CustomerService.cs</c>), so
/// deleting the consent row alone would silently lapse the opt-out. This row keeps the decision —
/// "this number objected" — while keeping no name, email, message or number: only the deterministic
/// phone fingerprint and the terminal status.
/// </para>
/// <para>
/// <b>Scoped to the organisation that erased.</b> A global (<c>scope = all</c>) erasure writes one
/// row per organisation it touched; an org-scoped erasure writes one. A tombstone never crosses a
/// tenant boundary on its own.
/// </para>
/// </remarks>
public class PrivacyErasureTombstone
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The deterministic fingerprint of the E.164 number. Never the number.</summary>
    public string PhoneHash { get; set; } = string.Empty;

    /// <summary>The terminal consent status the tombstone preserves (<c>revoked</c>).</summary>
    public string Status { get; set; } = ConsentStatuses.Revoked;

    public DateTime ErasedAt { get; set; } = DateTime.UtcNow;
}
