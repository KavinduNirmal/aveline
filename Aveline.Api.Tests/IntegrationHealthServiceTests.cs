using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class IntegrationHealthServiceTests
{
    private readonly AppDbContext _context;
    private readonly FakeWhatsAppService _whatsApp;
    private readonly FakeNotificationDispatcher _dispatcher;
    private readonly IntegrationHealthService _sut;

    public IntegrationHealthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"HealthServiceTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Convert.ToBase64String(new byte[32]),
            })
            .Build();

        var encryption = new CredentialEncryptionService(config);
        var repository = new Aveline.Api.Modules.Integrations.Repositories.IntegrationCredentialRepository(_context);
        _whatsApp = new FakeWhatsAppService();
        _dispatcher = new FakeNotificationDispatcher();
        var integrationService = new IntegrationService(repository, encryption, _whatsApp, NullLogger<IntegrationService>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton<IIntegrationService>(integrationService);
        services.AddSingleton<IWhatsAppService>(_whatsApp);
        services.AddSingleton<INotificationDispatcher>(_dispatcher);
        var provider = services.BuildServiceProvider();

        _sut = new IntegrationHealthService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<IntegrationHealthService>.Instance);
    }

    private async Task<Guid> SeedConnectedWhatsAppAsync(string token = "valid-token")
    {
        var orgId = Guid.NewGuid();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Convert.ToBase64String(new byte[32]),
            })
            .Build();
        var encryption = new CredentialEncryptionService(config);
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["accessToken"] = token,
            ["phoneNumberId"] = "111",
            ["appSecret"] = "secret",
            ["webhookVerifyToken"] = "verify",
        });
        _context.IntegrationCredentials.Add(new IntegrationCredential
        {
            OrganizationId = orgId,
            IntegrationType = IntegrationType.WhatsApp,
            EncryptedValue = encryption.Encrypt(json, $"{orgId}:{IntegrationType.WhatsApp}"),
            Status = IntegrationStatus.Connected,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
        return orgId;
    }

    [Fact]
    public async Task Check_ValidToken_LeavesConnected_NoNotification()
    {
        var orgId = await SeedConnectedWhatsAppAsync("valid-token");
        _whatsApp.Valid = true;

        await _sut.CheckAllIntegrationsAsync(CancellationToken.None);

        var row = await _context.IntegrationCredentials.SingleAsync(c => c.OrganizationId == orgId);
        Assert.Equal(IntegrationStatus.Connected, row.Status);
        Assert.Empty(_dispatcher.Dispatched);
    }

    [Fact]
    public async Task Check_InvalidToken_MarksExpired_AndNotifies()
    {
        var orgId = await SeedConnectedWhatsAppAsync("expired-token");
        _whatsApp.Valid = false;

        await _sut.CheckAllIntegrationsAsync(CancellationToken.None);

        var row = await _context.IntegrationCredentials.SingleAsync(c => c.OrganizationId == orgId);
        Assert.Equal(IntegrationStatus.Expired, row.Status);
        Assert.NotNull(row.LastError);

        var notification = Assert.Single(_dispatcher.Dispatched);
        Assert.Equal(NotificationType.IntegrationExpired, notification.Type);
        Assert.Equal(orgId, notification.Target.OrganizationId);
    }

    [Fact]
    public async Task Check_IgnoresNonConnectedIntegrations()
    {
        var orgId = Guid.NewGuid();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Convert.ToBase64String(new byte[32]),
            })
            .Build();
        var encryption = new CredentialEncryptionService(config);
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["accessToken"] = "token",
            ["phoneNumberId"] = "111",
            ["appSecret"] = "secret",
            ["webhookVerifyToken"] = "verify",
        });
        _context.IntegrationCredentials.Add(new IntegrationCredential
        {
            OrganizationId = orgId,
            IntegrationType = IntegrationType.WhatsApp,
            EncryptedValue = encryption.Encrypt(json, $"{orgId}:{IntegrationType.WhatsApp}"),
            Status = IntegrationStatus.Pending, // not connected -> skipped
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();
        _whatsApp.Valid = false;

        await _sut.CheckAllIntegrationsAsync(CancellationToken.None);

        var row = await _context.IntegrationCredentials.SingleAsync(c => c.OrganizationId == orgId);
        Assert.Equal(IntegrationStatus.Pending, row.Status);
        Assert.Empty(_dispatcher.Dispatched);
    }

    private sealed class FakeWhatsAppService : IWhatsAppService
    {
        public bool Valid { get; set; } = true;

        public Task<WhatsAppTestResult> TestConnectionAsync(
            string accessToken, string phoneNumberId, CancellationToken cancellationToken = default)
            => Task.FromResult(Valid
                ? new WhatsAppTestResult(IsValid: true)
                : new WhatsAppTestResult(IsValid: false, Error: "Token expired"));

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.test"));
    }

    private sealed class FakeNotificationDispatcher : INotificationDispatcher
    {
        public List<Notification> Dispatched { get; } = [];

        public Task DispatchAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            Dispatched.Add(notification);
            return Task.CompletedTask;
        }
    }
}
