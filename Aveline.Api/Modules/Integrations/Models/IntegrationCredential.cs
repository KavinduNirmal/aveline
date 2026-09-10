using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Integrations.Models;

/// <summary>
/// Tenant-scoped encrypted credentials for a single third-party integration.
/// Only <see cref="EncryptedValue"/> (an AES-256-GCM blob of the secret JSON) is
/// stored — never plaintext. The plaintext secret is only ever decrypted transiently
/// inside <see cref="Services.IntegrationService"/> for outbound calls and is never
/// returned to callers or written to logs. One row per <c>(OrganizationId, IntegrationType)</c>.
/// </summary>
public class IntegrationCredential
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning boutique. All repository operations filter by this column to enforce tenant isolation.</summary>
    public Guid OrganizationId { get; set; }

    public IntegrationType IntegrationType { get; set; }

    /// <summary>Encrypted secret blob (<c>iv:tag:ciphertext</c> hex) of the credential JSON.</summary>
    public string EncryptedValue { get; set; } = string.Empty;

    /// <summary>Optional non-sensitive metadata (JSON), e.g. WhatsApp phone number or token expiry.</summary>
    public string? Metadata { get; set; }

    /// <summary>Lifecycle state of the integration (see <see cref="IntegrationStatus"/>).</summary>
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;

    /// <summary>UTC instant the integration was last successfully validated/connected.</summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>Last error message (non-secret) when the integration is in an error/expired state.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Owning boutique.</summary>
    public Organization? Organization { get; set; }
}
