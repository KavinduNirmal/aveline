using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Repositories;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class IntegrationServiceTests
{
    private readonly AppDbContext _context;
    private readonly IntegrationService _sut;
    private readonly IntegrationCredentialRepository _repository;
    private readonly CredentialEncryptionService _encryption;
    private readonly FakeWhatsAppService _whatsApp;

    public IntegrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"IntegrationServiceTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Convert.ToBase64String(new byte[32]),
            })
            .Build();

        _repository = new IntegrationCredentialRepository(_context);
        _encryption = new CredentialEncryptionService(config);
        _whatsApp = new FakeWhatsAppService();
        _sut = new IntegrationService(_repository, _encryption, _whatsApp, NullLogger<IntegrationService>.Instance);
    }

    private sealed class FakeWhatsAppService : IWhatsAppService
    {
        public bool Valid { get; set; } = true;

        public Task<WhatsAppTestResult> TestConnectionAsync(
            string accessToken, string phoneNumberId, CancellationToken cancellationToken = default)
            => Task.FromResult(Valid
                ? new WhatsAppTestResult(IsValid: true)
                : new WhatsAppTestResult(IsValid: false, Error: "Invalid token"));

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.test"));
    }

    private static SaveIntegrationRequest WhatsApp(string token = "wa-access-token") =>
        new(new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["phoneNumberId"] = "111",
            ["appSecret"] = "app-secret",
            ["webhookVerifyToken"] = "verify-token",
        }, Metadata: "{\"phoneNumberId\":\"111\"}");

    private static SaveIntegrationRequest Instagram() =>
        new(new Dictionary<string, string>
        {
            ["clientId"] = "client-1",
            ["clientSecret"] = "client-secret-1",
            ["accessToken"] = "ig-access-token",
        });

    private static SaveIntegrationRequest Payment() =>
        new(new Dictionary<string, string> { ["secretKey"] = "sk_live_123", ["publishableKey"] = "pk_live_456" });

    [Fact]
    public async Task SaveAsync_StoresEncryptedNotPlaintext()
    {
        var orgId = Guid.NewGuid();

        var status = await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());

        // A plain save leaves the integration Pending until it is validated/connected.
        Assert.Equal(IntegrationStatus.Pending, status.Status);
        Assert.False(status.Connected);

        var row = await _repository.GetAsync(orgId, IntegrationType.WhatsApp);
        Assert.NotNull(row);
        Assert.DoesNotContain("wa-access-token", row.EncryptedValue);
        // Encrypted blob round-trips to the original secret using the org+type AAD.
        var decrypted = _encryption.Decrypt(row.EncryptedValue, $"{orgId}:{IntegrationType.WhatsApp}");
        Assert.Contains("wa-access-token", decrypted);
    }

    [Fact]
    public async Task SaveAsync_ReturnsMaskedPreview_NeverPlaintext()
    {
        var orgId = Guid.NewGuid();
        var status = await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp("abcdefghijkl"));

        Assert.NotNull(status.MaskedPreview);
        Assert.DoesNotContain("abcdefghijkl", status.MaskedPreview);
        Assert.Contains("ijkl", status.MaskedPreview); // last 4 chars shown
    }

    [Fact]
    public async Task SaveAsync_MissingRequiredKey_Throws()
    {
        var orgId = Guid.NewGuid();
        var bad = new SaveIntegrationRequest(new Dictionary<string, string> { ["foo"] = "bar" });

        await Assert.ThrowsAsync<InvalidIntegrationCredentialsException>(
            () => _sut.SaveAsync(orgId, IntegrationType.Instagram, bad));
    }

    [Fact]
    public async Task SaveAsync_UpsertsPerOrganization_AndDoesNotBleedBetweenOrgs()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        await _sut.SaveAsync(orgA, IntegrationType.WhatsApp, WhatsApp("token-for-a"));
        await _sut.SaveAsync(orgB, IntegrationType.WhatsApp, WhatsApp("token-for-b"));

        var a = await _sut.GetCredentialsAsync(orgA, IntegrationType.WhatsApp);
        var b = await _sut.GetCredentialsAsync(orgB, IntegrationType.WhatsApp);

        Assert.Equal("token-for-a", a["accessToken"]);
        Assert.Equal("token-for-b", b["accessToken"]);

        // Updating org A must not affect org B.
        await _sut.SaveAsync(orgA, IntegrationType.WhatsApp, WhatsApp("token-for-a-v2"));
        var bAfter = await _sut.GetCredentialsAsync(orgB, IntegrationType.WhatsApp);
        Assert.Equal("token-for-b", bAfter["accessToken"]);
    }

    [Fact]
    public async Task ListStatusAsync_OnlyReturnsConfiguredIntegrations_AndIsMasked()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.Instagram, Instagram());

        var list = await _sut.ListStatusAsync(orgId);

        var status = Assert.Single(list);
        Assert.Equal(IntegrationType.Instagram, status.Type);
        Assert.Equal(IntegrationStatus.Pending, status.Status);
        Assert.NotNull(status.MaskedPreview);
        Assert.DoesNotContain("ig-access-token", status.MaskedPreview);
    }

    [Fact]
    public async Task GetCredentialsAsync_WhenNotConfigured_Throws()
    {
        await Assert.ThrowsAsync<IntegrationNotConfiguredException>(
            () => _sut.GetCredentialsAsync(Guid.NewGuid(), IntegrationType.PaymentGateway));
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsAllStoredSecrets()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.PaymentGateway, Payment());

        var creds = await _sut.GetCredentialsAsync(orgId, IntegrationType.PaymentGateway);

        Assert.Equal("sk_live_123", creds["secretKey"]);
        Assert.Equal("pk_live_456", creds["publishableKey"]);
    }

    [Fact]
    public async Task DeleteAsync_RemovesCredential()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());

        await _sut.DeleteAsync(orgId, IntegrationType.WhatsApp);

        await Assert.ThrowsAsync<IntegrationNotConfiguredException>(
            () => _sut.GetCredentialsAsync(orgId, IntegrationType.WhatsApp));
    }

    [Fact]
    public async Task SaveAsync_InstagramRequiresAllThreeSecrets()
    {
        var orgId = Guid.NewGuid();
        var partial = new SaveIntegrationRequest(new Dictionary<string, string>
        {
            ["clientId"] = "client-1",
            ["accessToken"] = "ig-access-token", // missing clientSecret
        });

        await Assert.ThrowsAsync<InvalidIntegrationCredentialsException>(
            () => _sut.SaveAsync(orgId, IntegrationType.Instagram, partial));
    }

    [Fact]
    public async Task MarkConnectedAsync_SetsConnectedAndRecordsLastConnectedAt()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());

        var status = await _sut.MarkConnectedAsync(orgId, IntegrationType.WhatsApp);

        Assert.Equal(IntegrationStatus.Connected, status.Status);
        Assert.True(status.Connected);
        Assert.NotNull(status.LastConnectedAt);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_SetsErrorWithMessage()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());

        var status = await _sut.MarkFailedAsync(orgId, IntegrationType.WhatsApp, "Invalid token");

        Assert.Equal(IntegrationStatus.Error, status.Status);
        Assert.False(status.Connected);
        Assert.Equal("Invalid token", status.LastError);
    }

    [Fact]
    public async Task MarkExpiredAsync_SetsExpiredWithMessage()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());

        var status = await _sut.MarkExpiredAsync(orgId, IntegrationType.WhatsApp, "Token expired");

        Assert.Equal(IntegrationStatus.Expired, status.Status);
        Assert.False(status.Connected);
        Assert.Equal("Token expired", status.LastError);
    }

    [Fact]
    public async Task MarkConnectedAsync_WhenNotConfigured_Throws()
    {
        await Assert.ThrowsAsync<IntegrationNotConfiguredException>(
            () => _sut.MarkConnectedAsync(Guid.NewGuid(), IntegrationType.WhatsApp));
    }

    [Fact]
    public async Task TestConnectionAsync_WhatsAppValid_MarksConnected()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());
        _whatsApp.Valid = true;

        var result = await _sut.TestConnectionAsync(orgId, IntegrationType.WhatsApp);

        Assert.True(result.IsValid);
        Assert.Equal(IntegrationStatus.Connected, result.Status.Status);
        Assert.True(result.Status.Connected);
    }

    [Fact]
    public async Task TestConnectionAsync_WhatsAppInvalid_MarksError()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.WhatsApp, WhatsApp());
        _whatsApp.Valid = false;

        var result = await _sut.TestConnectionAsync(orgId, IntegrationType.WhatsApp);

        Assert.False(result.IsValid);
        Assert.Equal(IntegrationStatus.Error, result.Status.Status);
        Assert.NotNull(result.Status.LastError);
    }

    [Fact]
    public async Task TestConnectionAsync_NonWhatsApp_MarksConnectedWithoutProviderCheck()
    {
        var orgId = Guid.NewGuid();
        await _sut.SaveAsync(orgId, IntegrationType.PaymentGateway, Payment());

        var result = await _sut.TestConnectionAsync(orgId, IntegrationType.PaymentGateway);

        Assert.True(result.IsValid);
        Assert.Equal(IntegrationStatus.Connected, result.Status.Status);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenNotConfigured_Throws()
    {
        await Assert.ThrowsAsync<IntegrationNotConfiguredException>(
            () => _sut.TestConnectionAsync(Guid.NewGuid(), IntegrationType.WhatsApp));
    }
}
