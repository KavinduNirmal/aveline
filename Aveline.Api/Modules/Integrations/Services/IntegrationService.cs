using System.Text.Json;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Integrations.Services;

public sealed class IntegrationService : IIntegrationService
{
    private static readonly IReadOnlyDictionary<IntegrationType, string[]> RequiredKeys =
        new Dictionary<IntegrationType, string[]>
        {
            [IntegrationType.WhatsApp] = ["accessToken"],
            [IntegrationType.Instagram] = ["clientId", "clientSecret", "accessToken"],
            [IntegrationType.PaymentGateway] = ["secretKey"],
        };

    private static readonly IReadOnlyDictionary<IntegrationType, string> PrimarySecretKey =
        new Dictionary<IntegrationType, string>
        {
            [IntegrationType.WhatsApp] = "accessToken",
            [IntegrationType.Instagram] = "accessToken",
            [IntegrationType.PaymentGateway] = "secretKey",
        };

    private readonly IIntegrationCredentialRepository _repository;
    private readonly ICredentialEncryptionService _encryption;
    private readonly ILogger<IntegrationService> _logger;

    public IntegrationService(
        IIntegrationCredentialRepository repository,
        ICredentialEncryptionService encryption,
        ILogger<IntegrationService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<IntegrationStatusDto> SaveAsync(
        Guid organizationId,
        IntegrationType type,
        SaveIntegrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var required = RequiredKeys.GetValueOrDefault(type);
        if (required is null)
        {
            throw new InvalidIntegrationCredentialsException(type, "unsupported integration type.");
        }

        var normalized = Normalize(request.Credentials);
        var missing = required.Where(k => !normalized.ContainsKey(k)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidIntegrationCredentialsException(type, string.Join(", ", missing));
        }

        var encrypted = _encryption.Encrypt(JsonSerializer.Serialize(normalized));
        await _repository.UpsertAsync(organizationId, type, encrypted, request.Metadata, cancellationToken);

        _logger.LogInformation(
            "Integration credentials saved. organizationId={OrganizationId} type={Type}",
            organizationId, type);

        return ToStatus(type, normalized, request.Metadata, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<IntegrationStatusDto>> ListStatusAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListByOrganizationAsync(organizationId, cancellationToken);
        var result = new List<IntegrationStatusDto>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(ToStatus(row.IntegrationType, TryDecrypt(row), row.Metadata, row.UpdatedAt));
        }

        return result;
    }

    /// <summary>
    /// Decrypts a stored row solely to derive a safe masked preview for status display.
    /// On any failure (e.g. a tampered blob) the row is still reported as connected but
    /// with no preview, and the credential is never returned as plaintext.
    /// </summary>
    private IDictionary<string, string>? TryDecrypt(IntegrationCredential row)
    {
        try
        {
            var json = _encryption.Decrypt(row.EncryptedValue);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "Could not decrypt integration status preview. organizationId={OrganizationId} type={Type}",
                row.OrganizationId, row.IntegrationType);
            return null;
        }
    }

    public async Task<IDictionary<string, string>> GetCredentialsAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default)
    {
        var row = await _repository.GetAsync(organizationId, type, cancellationToken);
        if (row is null)
        {
            throw new IntegrationNotConfiguredException(type);
        }

        var json = _encryption.Decrypt(row.EncryptedValue);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public async Task DeleteAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default)
    {
        await _repository.DeleteAsync(organizationId, type, cancellationToken);
        _logger.LogInformation("Integration credentials deleted. organizationId={OrganizationId} type={Type}", organizationId, type);
    }

    private static IntegrationStatusDto ToStatus(
        IntegrationType type,
        IDictionary<string, string>? credentials,
        string? metadata,
        DateTime updatedAt)
    {
        var primary = PrimarySecretKey.TryGetValue(type, out var primaryKey) ? primaryKey : string.Empty;
        var value = credentials is not null && !string.IsNullOrEmpty(primary)
            && credentials.TryGetValue(primary, out var v) ? v : null;
        return new IntegrationStatusDto(
            Type: type,
            Connected: value is not null,
            MaskedPreview: value is null ? null : Mask(value),
            Metadata: metadata,
            UpdatedAt: updatedAt);
    }

    private static Dictionary<string, string> Normalize(IDictionary<string, string> credentials)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in credentials)
        {
            if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
            {
                result[k.Trim()] = v.Trim();
            }
        }

        return result;
    }

    /// <summary>Masks a secret for safe display, e.g. <c>****AbCd</c>.</summary>
    private static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "****";
        }

        const string placeholder = "****";
        return value.Length <= placeholder.Length
            ? placeholder
            : placeholder + value[^4..];
    }
}
