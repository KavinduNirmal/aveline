using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// Background service that periodically validates connected integrations against their
/// providers. When a WhatsApp token is no longer valid the integration is marked
/// <see cref="IntegrationStatus.Expired"/> and the boutique owner is notified so they can
/// reconnect. Runs on a configurable interval (<c>IntegrationHealth:IntervalHours</c>,
/// default 6 hours).
/// </summary>
public sealed class IntegrationHealthService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IntegrationHealthService> _logger;

    public IntegrationHealthService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<IntegrationHealthService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _configuration.GetValue("IntegrationHealth:IntervalHours", 6);
        var interval = TimeSpan.FromHours(Math.Max(0.1, intervalHours));

        _logger.LogInformation("Integration health service started (interval={IntervalHours}h).", intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllIntegrationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Integration health check pass failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one health-check pass. Internal so the test project can drive it directly
    /// without waiting on the background loop.
    /// </summary>
    internal async Task CheckAllIntegrationsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var integrationService = scope.ServiceProvider.GetRequiredService<IIntegrationService>();
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var connected = await db.IntegrationCredentials
            .Where(c => c.IntegrationType == IntegrationType.WhatsApp && c.Status == IntegrationStatus.Connected)
            .ToListAsync(stoppingToken);

        foreach (var integration in connected)
        {
            try
            {
                var credentials = await integrationService.GetCredentialsAsync(
                    integration.OrganizationId, IntegrationType.WhatsApp, stoppingToken);
                credentials.TryGetValue("accessToken", out var accessToken);
                credentials.TryGetValue("phoneNumberId", out var phoneNumberId);

                if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(phoneNumberId))
                {
                    await MarkExpiredAndNotifyAsync(
                        integrationService, dispatcher, integration, "Missing accessToken or phoneNumberId.", stoppingToken);
                    continue;
                }

                var result = await whatsApp.TestConnectionAsync(accessToken, phoneNumberId, stoppingToken);
                if (!result.IsValid)
                {
                    var error = result.Error ?? "Token validation failed.";
                    await MarkExpiredAndNotifyAsync(integrationService, dispatcher, integration, error, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Integration health check failed. organizationId={OrganizationId} type={Type}",
                    integration.OrganizationId, integration.IntegrationType);
            }
        }
    }

    private async Task MarkExpiredAndNotifyAsync(
        IIntegrationService integrationService,
        INotificationDispatcher dispatcher,
        IntegrationCredential integration,
        string error,
        CancellationToken stoppingToken)
    {
        await integrationService.MarkExpiredAsync(
            integration.OrganizationId, integration.IntegrationType, error, stoppingToken);

        _logger.LogWarning(
            "Integration marked expired. organizationId={OrganizationId} type={Type} error={Error}",
            integration.OrganizationId, integration.IntegrationType, error);

        var notification = new Notification(
            Type: NotificationType.IntegrationExpired,
            Title: "WhatsApp connection expired",
            Body: "Your WhatsApp connection has expired. Please reconnect it from Settings → Integrations.",
            Target: new NotificationTarget(
                integration.OrganizationId,
                Roles: [Roles.BoutiqueOwner, Roles.Owner]),
            Data: new Dictionary<string, string?>
            {
                ["integrationType"] = integration.IntegrationType.ToString(),
                ["error"] = error,
            },
            Channels: NotificationChannel.Realtime | NotificationChannel.Push);

        await dispatcher.DispatchAsync(notification, stoppingToken);
    }
}
