using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.ApiAccess.Domain;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.ApiAccess.Repositories;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;

namespace Aveline.Api.Modules.ApiAccess.Services;

/// <summary>
/// The API-key lifecycle: generation, scope validation, resolution and revocation
/// (FR-3.10–FR-3.18).
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    /// <summary>Entitlement that gates API-key creation to the Rose and Enterprise plans.</summary>
    public const string ApiAccessEntitlementKey = "api.access";

    private const int MaxNameLength = 100;
    private const int MaxReasonLength = 300;

    private readonly IApiKeyRepository _repository;
    private readonly IEntitlementResolver _entitlements;
    private readonly IEventBus _eventBus;
    private readonly IAuditService _audit;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(
        IApiKeyRepository repository,
        IEntitlementResolver entitlements,
        IEventBus eventBus,
        IAuditService audit,
        ILogger<ApiKeyService> logger)
    {
        _repository = repository;
        _entitlements = entitlements;
        _eventBus = eventBus;
        _audit = audit;
        _logger = logger;
    }

    public async Task<CreatedApiKey> CreateAsync(
        Guid organizationId,
        Guid createdByUserId,
        CreateApiKeyCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ApiKeyValidationException("A key name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new ApiKeyValidationException($"A key name cannot exceed {MaxNameLength} characters.");
        }

        var scopes = ApiKeyScopes.Validate(command.Scopes);

        if (command.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
        {
            throw new ApiKeyValidationException("The expiry must be in the future.");
        }

        // FR-3.16: API access is a Rose/Enterprise entitlement. Fail closed when the
        // catalog cannot resolve it rather than granting access by default.
        var entitlement = await _entitlements.GetAsync(
            organizationId, ApiAccessEntitlementKey, null, cancellationToken);
        if (entitlement?.Flag != true)
        {
            throw new ApiKeyEntitlementRequiredException();
        }

        var generated = ApiKeyCredentials.Generate(command.Environment);
        var key = new ApiKey
        {
            OrganizationId = organizationId,
            Name = name,
            Prefix = generated.Prefix,
            KeyHash = generated.Hash,
            HashAlgorithm = ApiKeyHashing.AlgorithmSha256,
            Scopes = [.. scopes],
            Environment = command.Environment,
            Status = ApiKeyStatus.Active,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = command.ExpiresAt,
        };

        await _repository.AddAsync(key, cancellationToken);
        _logger.LogInformation(
            "API key created. organizationId={OrganizationId} keyId={KeyId} prefix={Prefix}",
            organizationId, key.Id, key.Prefix);

        await _audit.RecordAsync(new AuditEntryRequest(
            AuditAction.ApiKeyCreated,
            "ApiKey",
            key.Id.ToString(),
            OrganizationId: organizationId,
            ActorKind: AuditActorKind.User,
            ActorUserId: createdByUserId,
            After: new { key.Name, key.Prefix, Scopes = scopes, Environment = key.Environment.ToString() }),
            cancellationToken);

        await _eventBus.PublishAsync(
            "apikey.created",
            organizationId,
            new { keyId = key.Id, prefix = key.Prefix, name = key.Name, scopes },
            cancellationToken: cancellationToken);

        return new CreatedApiKey(key, generated.Plaintext);
    }

    public Task<IReadOnlyList<ApiKey>> ListAsync(
        Guid organizationId, CancellationToken cancellationToken = default) =>
        _repository.ListAsync(organizationId, cancellationToken);

    public async Task<ApiKey> RevokeAsync(
        Guid organizationId,
        Guid keyId,
        Guid revokedByUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (reason is { Length: > MaxReasonLength })
        {
            throw new ApiKeyValidationException($"A revocation reason cannot exceed {MaxReasonLength} characters.");
        }

        var key = await _repository.GetByIdAsync(organizationId, keyId, cancellationToken)
            ?? throw new ApiKeyNotFoundException(keyId);

        // Revocation is idempotent: a second call does not move the revocation timestamp.
        if (key.Status == ApiKeyStatus.Revoked)
        {
            return key;
        }

        key.Status = ApiKeyStatus.Revoked;
        key.RevokedAt = DateTime.UtcNow;
        key.RevokedByUserId = revokedByUserId;
        key.RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await _repository.UpdateAsync(key, cancellationToken);
        _logger.LogInformation(
            "API key revoked. organizationId={OrganizationId} keyId={KeyId}", organizationId, keyId);

        await _audit.RecordAsync(new AuditEntryRequest(
            AuditAction.ApiKeyRevoked,
            "ApiKey",
            key.Id.ToString(),
            OrganizationId: organizationId,
            ActorKind: AuditActorKind.User,
            ActorUserId: revokedByUserId,
            Before: new { Status = ApiKeyStatus.Active.ToString() },
            After: new { Status = ApiKeyStatus.Revoked.ToString(), key.RevokedReason },
            Reason: key.RevokedReason),
            cancellationToken);

        await _eventBus.PublishAsync(
            "apikey.revoked",
            organizationId,
            new { keyId = key.Id, prefix = key.Prefix },
            cancellationToken: cancellationToken);

        return key;
    }

    public async Task DeleteAsync(
        Guid organizationId, Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await _repository.GetByIdAsync(organizationId, keyId, cancellationToken)
            ?? throw new ApiKeyNotFoundException(keyId);

        // FR-3.13: only a key that has never served a request may be hard-deleted, so the
        // historical request counter can never be silently discarded.
        if (key.RequestCount > 0 || key.LastUsedAt is not null)
        {
            throw new ApiKeyAlreadyUsedException(keyId);
        }

        await _repository.DeleteAsync(key, cancellationToken);
        _logger.LogInformation(
            "API key deleted. organizationId={OrganizationId} keyId={KeyId}", organizationId, keyId);

        await _audit.RecordAsync(new AuditEntryRequest(
            AuditAction.ApiKeyDeleted,
            "ApiKey",
            key.Id.ToString(),
            OrganizationId: organizationId,
            ActorKind: AuditActorKind.User,
            Before: new { key.Name, key.Prefix }),
            cancellationToken);

        await _eventBus.PublishAsync(
            "apikey.revoked",
            organizationId,
            new { keyId = key.Id, prefix = key.Prefix, deleted = true },
            cancellationToken: cancellationToken);
    }

    public async Task<ApiKey?> AuthenticateAsync(
        string presentedSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedSecret))
        {
            return null;
        }

        var secret = presentedSecret.Trim();
        if (secret.Length <= ApiKeyCredentials.PrefixLength)
        {
            return null;
        }

        var key = await _repository.GetByPrefixAsync(
            ApiKeyCredentials.PrefixOf(secret), cancellationToken);
        if (key is null || key.Status != ApiKeyStatus.Active)
        {
            return null;
        }

        if (key.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return ApiKeyCredentials.FixedTimeEquals(key.KeyHash, ApiKeyCredentials.Hash(secret))
            ? key
            : null;
    }
}
