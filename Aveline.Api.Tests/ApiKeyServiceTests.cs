using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.ApiAccess.Domain;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.ApiAccess.Repositories;
using Aveline.Api.Modules.ApiAccess.Services;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #202 — API key generation, hashing, scope validation and lifecycle
/// (FR-3.10–FR-3.15, BR-3.4).
/// </summary>
public class ApiKeyServiceTests
{
    private readonly AppDbContext _context;

    public ApiKeyServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ApiKey_{Guid.NewGuid()}")
            .Options);
    }

    private ApiKeyService CreateService() =>
        new(
            new ApiKeyRepository(_context),
            new EntitlementResolver(new EntitlementRepository(_context)),
            new InMemoryEventBus(),
            new AuditService(
                new AuditRepository(_context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            NullLogger<ApiKeyService>.Instance);

    private async Task<(Guid OrgId, Guid UserId)> SeedOrganizationAsync(PlanTier tier = PlanTier.Rose)
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"o_{ownerId:N}", Email = "o@aveline.lk",
            FirstName = "O", LastName = "W", Username = $"o_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Key Boutique", Slug = $"kb-{ownerId:N}", OwnerUserId = ownerId, PlanTier = tier,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return (org.Id, ownerId);
    }

    [Theory]
    [InlineData(ApiKeyEnvironment.Live)]
    [InlineData(ApiKeyEnvironment.Test)]
    public void Generate_ProducesDocumentedFormat(ApiKeyEnvironment environment)
    {
        var generated = ApiKeyCredentials.Generate(environment);

        var expectedPrefix = environment == ApiKeyEnvironment.Live ? "avl_live_" : "avl_test_";
        Assert.StartsWith(expectedPrefix, generated.Plaintext);
        Assert.Matches(
            new Regex($"^avl_{(environment == ApiKeyEnvironment.Live ? "live" : "test")}_[0-9A-Za-z]{{32}}$"),
            generated.Plaintext);
    }

    [Fact]
    public void Generate_PrefixIsFirstSixteenCharacters()
    {
        var generated = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live);

        Assert.Equal(16, generated.Prefix.Length);
        Assert.Equal(generated.Plaintext[..16], generated.Prefix);
    }

    [Fact]
    public void Generate_ProducesDistinctSecrets()
    {
        var secrets = Enumerable.Range(0, 200)
            .Select(_ => ApiKeyCredentials.Generate(ApiKeyEnvironment.Live).Plaintext)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(200, secrets.Count);
    }

    [Fact]
    public void Hash_IsLowercaseSha256HexAndOneWay()
    {
        var secret = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live).Plaintext;

        var hash = ApiKeyCredentials.Hash(secret);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

        Assert.Equal(expected, hash);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(secret, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedTimeEquals_MatchesOnlyTheExactHash()
    {
        var secret = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live).Plaintext;
        var hash = ApiKeyCredentials.Hash(secret);

        Assert.True(ApiKeyCredentials.FixedTimeEquals(hash, hash));
        Assert.False(ApiKeyCredentials.FixedTimeEquals(
            hash, ApiKeyCredentials.Hash(ApiKeyCredentials.Generate(ApiKeyEnvironment.Live).Plaintext)));
        Assert.False(ApiKeyCredentials.FixedTimeEquals(hash, hash[..32]));
    }

    [Fact]
    public void Scopes_AcceptsCatalogSubset()
    {
        var validated = ApiKeyScopes.Validate(["catalog:view", "customers:view", "billing:view"]);

        Assert.Equal(["catalog:view", "customers:view", "billing:view"], validated);
    }

    [Theory]
    [InlineData("pricing:view")]
    [InlineData("pricing:manage")]
    [InlineData("pricing:backdate")]
    [InlineData("billing:adjust")]
    [InlineData("admin:users:read")]
    [InlineData("admin:users:manage")]
    [InlineData("admin:orgs:read")]
    public void Scopes_RejectsMoneyAndAdminScopes(string scope)
    {
        Assert.Throws<ApiKeyScopeNotAllowedException>(() => ApiKeyScopes.Validate([scope]));
    }

    [Fact]
    public void Scopes_RejectsUnknownScope()
    {
        Assert.Throws<ApiKeyValidationException>(() => ApiKeyScopes.Validate(["not:a:permission"]));
    }

    [Fact]
    public void Scopes_RejectsEmptySet()
    {
        Assert.Throws<ApiKeyValidationException>(() => ApiKeyScopes.Validate([]));
    }

    [Fact]
    public async Task CreateAsync_PersistsOnlyTheHashAndPrefix()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();

        var created = await service.CreateAsync(
            orgId, userId, new CreateApiKeyCommand("Production", ["catalog:view"]));

        var stored = await _context.ApiKeys.SingleAsync();
        Assert.Equal(created.Plaintext[..16], stored.Prefix);
        Assert.Equal(ApiKeyCredentials.Hash(created.Plaintext), stored.KeyHash);
        Assert.Equal("sha256", stored.HashAlgorithm);
        Assert.Equal(ApiKeyStatus.Active, stored.Status);
        Assert.Equal(ApiKeyEnvironment.Live, stored.Environment);
        Assert.Equal(orgId, stored.OrganizationId);
        Assert.Equal(userId, stored.CreatedByUserId);
        Assert.DoesNotContain(created.Plaintext, stored.KeyHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_RejectsWhenApiAccessEntitlementIsFalse()
    {
        var (orgId, userId) = await SeedOrganizationAsync(PlanTier.Seed);
        var service = CreateService();

        await Assert.ThrowsAsync<ApiKeyEntitlementRequiredException>(() => service.CreateAsync(
            orgId, userId, new CreateApiKeyCommand("Production", ["catalog:view"])));
    }

    [Fact]
    public async Task CreateAsync_RejectsForbiddenScope()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<ApiKeyScopeNotAllowedException>(() => service.CreateAsync(
            orgId, userId, new CreateApiKeyCommand("Production", ["pricing:manage"])));
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsKeyForAValidPresentedSecret()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();
        var created = await service.CreateAsync(orgId, userId, new CreateApiKeyCommand("Prod", ["catalog:view"]));

        var resolved = await service.AuthenticateAsync(created.Plaintext);

        Assert.NotNull(resolved);
        Assert.Equal(created.Key.Id, resolved!.Id);
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsWrongSecretWithTheRightPrefix()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();
        var created = await service.CreateAsync(orgId, userId, new CreateApiKeyCommand("Prod", ["catalog:view"]));

        var tampered = created.Plaintext[..^1] + (created.Plaintext[^1] == 'a' ? 'b' : 'a');
        Assert.Null(await service.AuthenticateAsync(tampered));
    }

    [Fact]
    public async Task AuthenticateAsync_RejectsRevokedAndExpiredKeys()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();

        var revoked = await service.CreateAsync(orgId, userId, new CreateApiKeyCommand("Revoked", ["catalog:view"]));
        await service.RevokeAsync(orgId, revoked.Key.Id, userId, "compromised");
        Assert.Null(await service.AuthenticateAsync(revoked.Plaintext));

        var expiredSecret = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live);
        _context.ApiKeys.Add(new ApiKey
        {
            OrganizationId = orgId,
            Name = "Expired",
            Prefix = expiredSecret.Prefix,
            KeyHash = expiredSecret.Hash,
            Scopes = ["catalog:view"],
            CreatedByUserId = userId,
            Status = ApiKeyStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await _context.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(expiredSecret.Plaintext));
    }

    [Fact]
    public async Task RevokeAsync_SetsStatusAndRevocationAuditFields()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();
        var created = await service.CreateAsync(orgId, userId, new CreateApiKeyCommand("Prod", ["catalog:view"]));

        var revoked = await service.RevokeAsync(orgId, created.Key.Id, userId, "rotated");

        Assert.Equal(ApiKeyStatus.Revoked, revoked.Status);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal(userId, revoked.RevokedByUserId);
        Assert.Equal("rotated", revoked.RevokedReason);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnUnusedKeyButRejectsAUsedOne()
    {
        var (orgId, userId) = await SeedOrganizationAsync();
        var service = CreateService();

        var unused = await service.CreateAsync(orgId, userId, new CreateApiKeyCommand("Unused", ["catalog:view"]));
        await service.DeleteAsync(orgId, unused.Key.Id);
        Assert.Empty(_context.ApiKeys.Where(k => k.Id == unused.Key.Id));

        var used = await service.CreateAsync(orgId, userId, new CreateApiKeyCommand("Used", ["catalog:view"]));
        var row = await _context.ApiKeys.SingleAsync(k => k.Id == used.Key.Id);
        row.RequestCount = 5;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApiKeyAlreadyUsedException>(() => service.DeleteAsync(orgId, used.Key.Id));
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyTheOwningOrganizationsKeys()
    {
        var (orgA, userA) = await SeedOrganizationAsync();
        var (orgB, userB) = await SeedOrganizationAsync();
        var service = CreateService();

        await service.CreateAsync(orgA, userA, new CreateApiKeyCommand("A", ["catalog:view"]));
        await service.CreateAsync(orgB, userB, new CreateApiKeyCommand("B", ["catalog:view"]));

        var listed = await service.ListAsync(orgA);

        Assert.Single(listed);
        Assert.Equal(orgA, listed[0].OrganizationId);
    }
}
