using Aveline.Api.Common.Middleware;
using Aveline.Api.Configurations;
using Aveline.Api.Endpoints;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Aveline.Api.Infrastructure.Notifications;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Admin.Repositories;
using Aveline.Api.Modules.Admin.Services;
using Aveline.Api.Modules.ApiAccess;
using Aveline.Api.Modules.Audit;
using Aveline.Api.Modules.Billing;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Conversations;
using Aveline.Api.Modules.Conversations.Hubs;
using Aveline.Api.Modules.CustomerConcierge;
using Aveline.Api.Modules.Integrations;
using Aveline.Api.Modules.Notifications;
using Aveline.Api.Modules.Notifications.Hubs;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Shared.Services;
using Aveline.Api.Modules.Statistics;
using Aveline.Api.Modules.SystemHealth;
using Aveline.Api.Modules.SystemHealth.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddAvelineLogging(builder.Configuration);
builder.Services.AddAvelineObservability(builder.Configuration);
builder.Services.AddAvelineDatabase(builder.Configuration);
builder.Services.AddAvelineCache(builder.Configuration);
builder.Services.AddAvelineJobs(builder.Configuration);
builder.Services.AddAvelineEventing(builder.Configuration);
builder.Services.AddAvelineAuthentication(builder.Configuration);
builder.Services.AddAvelineAuthorization();
builder.Services.AddAvelineCors(builder.Configuration);
builder.Services.AddAgentServiceClient(builder.Configuration);
builder.Services.AddClerkAdminClient();
builder.Services.AddWhatsAppProvider(builder.Configuration);
builder.Services.AddBillingModule();
builder.Services.AddApiAccessModule();
builder.Services.AddAvelineIdempotency();
builder.Services.AddAuditModule();
builder.Services.AddIntegrationsModule();
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Serialize NotificationType as its name (e.g. "PaymentConfirmed") so the
        // ReceiveNotification payload matches the documented client contract.
        options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddConversationsModule(builder.Configuration);
builder.Services.AddCustomerConciergeModule();
builder.Services.AddSystemHealthModule(builder.Configuration);
builder.Services.AddStatisticsModule(builder.Configuration);

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserCacheService, UserCacheService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IInvitationCodeStore, DistributedInvitationCodeStore>();
builder.Services.AddScoped<IEmailService, LoggingEmailService>();
builder.Services.AddScoped<IRateLimiter, DistributedRateLimiter>();

builder.Services.AddScoped<IAdminApprovalRepository, AdminApprovalRepository>();
builder.Services.AddScoped<IAdminApprovalService, AdminApprovalService>();
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddScoped<Aveline.Api.Modules.Organizations.Webhooks.IClerkWebhookSyncService,
    Aveline.Api.Modules.Organizations.Webhooks.ClerkWebhookSyncService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAvelineSecurityHeaders();
app.UseCors(CorsConfiguration.DefaultPolicy);
// Correlation ids must be established before authentication so 401/403 audit logs
// and every downstream log line carry the same request id (FR-6.10).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
// A cross-organization API key gets 404 before authorization can answer 403 (plan §8.2).
app.UseMiddleware<Aveline.Api.Modules.ApiAccess.Middleware.ApiKeyTenantScopeMiddleware>();
app.UseAuthorization();
app.UseAvelineAuthAudit();
app.UseAvelineOnboarding();
// Telemetry is stamped after authentication/authorization so attribution is available, and
// before endpoints so every measured request is captured (FR-6.1). It never fails a request.
app.UseMiddleware<Aveline.Api.Modules.Statistics.Telemetry.ApiTelemetryMiddleware>();

app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ConversationHub>("/hubs/conversations");
// Health checks are public (the fallback authorization policy requires auth by default).
app.MapSystemHealthEndpoints();
// Prometheus scraping requires an internal service token or the configured scrape token.
app.MapPrometheusScrapingEndpoint("/metrics")
    .RequireAuthorization(AuthorizationConfiguration.MetricsPolicy);

var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapAuthPolicyDemoEndpoints();
v1.MapAgentEndpoints();
v1.MapUserEndpoints();
v1.MapAdminUserEndpoints();
v1.MapDeviceTokenEndpoints();
v1.MapNotificationEndpoints();
v1.MapAdminEndpoints();
v1.MapOrganizationEndpoints();
v1.MapOnboardingEndpoints();
v1.MapIntegrationEndpoints();
v1.MapOrgUsageEndpoints();
v1.MapWebhookEndpoints();
v1.MapClerkWebhookEndpoints();
v1.MapConversationEndpoints();
v1.MapPricingEndpoints();
v1.MapBlossomEndpoints();
v1.MapSubscriptionEndpoints();
v1.MapApiAccessEndpoints();
v1.MapStatisticsEndpoints();

app.MapBillingEndpoints();
app.MapCustomerConciergeEndpoints();
app.MapStatisticsInternalEndpoints();

// Apply EF Core migrations on startup for a fresh/local database. Guarded to the
// relational (PostgreSQL) provider so the in-memory contexts used by the test suite are
// skipped, and to the Development environment to avoid unexpected migrations in prod.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();
    }
}

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;

