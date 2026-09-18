using System.Text.Json;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Repositories;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Integrations.Services;

public sealed class IntegrationService : IIntegrationService
{
    private static readonly IReadOnlyDictionary<IntegrationType, string[]> RequiredKeys =
        new Dictionary<IntegrationType, string[]>
        {
            [IntegrationType.WhatsApp] = ["accessToken", "phoneNumberId", "appSecret", "webhookVerifyToken"],
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
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<IntegrationService> _logger;

    public IntegrationService(
        IIntegrationCredentialRepository repository,
        ICredentialEncryptionService encryption,
        IWhatsAppService whatsApp,
        ILogger<IntegrationService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _whatsApp = whatsApp;
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

        var encrypted = _encryption.Encrypt(
            JsonSerializer.Serialize(normalized), Aad(organizationId, type));
        await _repository.UpsertAsync(
            organizationId, type, encrypted, request.Metadata, IntegrationStatus.Pending, cancellationToken);

        _logger.LogInformation(
            "Integration credentials saved. organizationId={OrganizationId} type={Type}",
            organizationId, type);

        return ToStatus(type, IntegrationStatus.Pending, normalized, request.Metadata, null, null, DateTime.UtcNow);
    }

    public async Task<IntegrationStatusDto> MarkConnectedAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default)
    {
        await _repository.UpdateStatusAsync(
            organizationId, type, IntegrationStatus.Connected, lastError: null, cancellationToken);
        return await GetStatusAsync(organizationId, type, cancellationToken);
    }

    public async Task<IntegrationStatusDto> MarkFailedAsync(
        Guid organizationId,
        IntegrationType type,
        string error,
        CancellationToken cancellationToken = default)
    {
        await _repository.UpdateStatusAsync(
            organizationId, type, IntegrationStatus.Error, error, cancellationToken);
        return await GetStatusAsync(organizationId, type, cancellationToken);
    }

    public async Task<IntegrationStatusDto> MarkExpiredAsync(
        Guid organizationId,
        IntegrationType type,
        string error,
        CancellationToken cancellationToken = default)
    {
        await _repository.UpdateStatusAsync(
            organizationId, type, IntegrationStatus.Expired, error, cancellationToken);
        return await GetStatusAsync(organizationId, type, cancellationToken);
    }

    private async Task<IntegrationStatusDto> GetStatusAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken)
    {
        var row = await _repository.GetAsync(organizationId, type, cancellationToken);
        if (row is null)
        {
            throw new IntegrationNotConfiguredException(type);
        }

        return ToStatus(row.IntegrationType, row.Status, TryDecrypt(row), row.Metadata,
            row.LastConnectedAt, row.LastError, row.UpdatedAt);
    }

    public async Task<IntegrationTestResultDto> TestConnectionAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default)
    {
        var credentials = await GetCredentialsAsync(organizationId, type, cancellationToken);

        // Only WhatsApp has a live provider check today; other types are treated as connected
        // once credentials are stored (their providers are wired in later slices).
        if (type != IntegrationType.WhatsApp)
        {
            var connected = await MarkConnectedAsync(organizationId, type, cancellationToken);
            return new IntegrationTestResultDto(IsValid: true, Error: null, Status: connected);
        }

        credentials.TryGetValue("accessToken", out var accessToken);
        credentials.TryGetValue("phoneNumberId", out var phoneNumberId);
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            var failed = await MarkFailedAsync(
                organizationId, type, "Missing accessToken or phoneNumberId.", cancellationToken);
            return new IntegrationTestResultDto(IsValid: false, Error: "Missing accessToken or phoneNumberId.", Status: failed);
        }

        var result = await _whatsApp.TestConnectionAsync(accessToken, phoneNumberId, cancellationToken);
        if (result.IsValid)
        {
            var connected = await MarkConnectedAsync(organizationId, type, cancellationToken);
            return new IntegrationTestResultDto(IsValid: true, Error: null, Status: connected);
        }

        var error = result.Error ?? "Connection test failed.";
        var errored = await MarkFailedAsync(organizationId, type, error, cancellationToken);
        return new IntegrationTestResultDto(IsValid: false, Error: error, Status: errored);
    }

    public async Task<IReadOnlyList<IntegrationStatusDto>> ListStatusAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListByOrganizationAsync(organizationId, cancellationToken);
        var result = new List<IntegrationStatusDto>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(ToStatus(row.IntegrationType, row.Status, TryDecrypt(row), row.Metadata,
                row.LastConnectedAt, row.LastError, row.UpdatedAt));
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
            var json = _encryption.Decrypt(
                row.EncryptedValue, Aad(row.OrganizationId, row.IntegrationType));
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

        var json = _encryption.Decrypt(row.EncryptedValue, Aad(organizationId, type));
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
        IntegrationStatus status,
        IDictionary<string, string>? credentials,
        string? metadata,
        DateTime? lastConnectedAt,
        string? lastError,
        DateTime updatedAt)
    {
        var primary = PrimarySecretKey.TryGetValue(type, out var primaryKey) ? primaryKey : string.Empty;
        var value = credentials is not null && !string.IsNullOrEmpty(primary)
            && credentials.TryGetValue(primary, out var v) ? v : null;
        var connected = status == IntegrationStatus.Connected && value is not null;
        return new IntegrationStatusDto(
            Type: type,
            Status: status,
            Connected: connected,
            MaskedPreview: value is null ? null : Mask(value),
            Metadata: metadata,
            LastConnectedAt: lastConnectedAt,
            LastError: lastError,
            UpdatedAt: updatedAt);
    }

    /// <summary>Normalized associated data binding a ciphertext to its owning org + type.</summary>
    private static string Aad(Guid organizationId, IntegrationType type) =>
        $"{organizationId}:{type}";

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
