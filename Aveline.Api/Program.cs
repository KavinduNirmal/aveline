using Aveline.Api.Common.Exceptions;
using Aveline.Api.Common.Middleware;
using Aveline.Api.Configurations;
using Aveline.Api.Endpoints;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Aveline.Api.Infrastructure.Notifications;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Admin.Endpoints;
using Aveline.Api.Modules.Admin.Repositories;
using Aveline.Api.Modules.Admin.Services;
using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.ApiAccess;
using Aveline.Api.Modules.Audit;
using Aveline.Api.Modules.Billing;
using Aveline.Api.Modules.Revenue;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Commerce;
using Aveline.Api.Modules.Commerce.Endpoints;
using Aveline.Api.Modules.Conversations;
using Aveline.Api.Modules.Conversations.Hubs;
using Aveline.Api.Modules.Conversations.Media;
using Aveline.Api.Modules.CustomerConcierge;
using Aveline.Api.Modules.Handbook;
using Aveline.Api.Modules.Home;
using Aveline.Api.Modules.Home.Endpoints;
using Aveline.Api.Modules.Integrations;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Notifications;
using Aveline.Api.Modules.Notifications.Hubs;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Endpoints;
using Aveline.Api.Modules.Privacy;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Shared.Services;
using Aveline.Api.Modules.Statistics;
using Aveline.Api.Modules.Statistics.Telemetry;
using Aveline.Api.Modules.SystemHealth;
using Aveline.Api.Modules.SystemHealth.Endpoints;
using Aveline.Api.Modules.VisualIntelligence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// A single handler turns every unhandled exception into a stable 500 envelope (M-7).
// AddHsts registers the policy options the handler reads so a handled 500 carries the
// same HSTS header UseHsts() writes on success paths.
builder.Services.AddHsts(_ => { });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddAvelineLogging(builder.Configuration);
builder.Services.AddAvelineObservability(builder.Configuration);
// The bridged business-metric gauges; the collector publishes its snapshot here before the
// database write so a database outage does not blind the operator dashboard.
builder.Services.AddAvelineMetrics();
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
// The media provider seam (S0): the options and the one explicit credential resolution, then
// the single place `IMediaStorage` is registered, selected from `Media:Provider`.
builder.Services.AddMediaOptions(builder.Configuration);
builder.Services.AddMediaModule(builder.Configuration);
// The pasted-image-URL fetcher (S6): its own client, its own pinned transport, and deliberately
// no auth delegating handler — this client must never authenticate to the host it fetches
// (salon plan §7.5 item 12). The kill switch is `Media:ImageUrlUploadEnabled`, default false.
builder.Services.AddImageUrlFetcher();
builder.Services.AddBillingModule();
builder.Services.AddRevenueModule(builder.Configuration);
// The payment provider abstraction and its adapters. Additive: it maps no endpoints and changes no
// existing flow, and `Payments:Provider` defaults to the honest `manual` adapter.
builder.Services.AddPaymentsModule(builder.Configuration, builder.Environment);
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
builder.Services.AddPrivacyModule(builder.Configuration);
builder.Services.AddHandbookModule();
builder.Services.AddHomeModule();
builder.Services.AddSystemHealthModule(builder.Configuration);
builder.Services.AddStatisticsModule(builder.Configuration);
builder.Services.AddAnalyticsModule(builder.Configuration);

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

builder.Services.AddControllers();

// Visual Intelligence & Inventory Module (Slice 2)
builder.Services.AddVisualIntelligenceModule();

// Commerce Module (Slice 3). Carries the Phase 9 rollback switch
// (`Payments:Commerce:UseProviderIntents`), so it binds the `Payments` section itself.
builder.Services.AddCommerceModule(builder.Configuration);

var app = builder.Build();

// Fail fast when Production would hash client IPs with an empty salt (M-1).
TelemetrySecurityGuard.EnsureIpHashSaltForProduction(app.Environment, app.Configuration);
// Fail fast when Production would expose /metrics under the committed internal token (S-1).
MetricsSecurityGuard.EnsureScrapeTokenForProduction(app.Environment, app.Configuration);
// Fail fast when the media provider is half-configured, and warn (never silently accept) when a
// Production host keeps image bytes in the database via the approved escape hatch (strategy §3.4).
MediaOptionsValidator.ValidateOrThrow(app.Configuration, app.Environment, app.Logger);
// Fail fast when Privacy:LinkSigningKey is set but unusable: such a host cannot sign the permanent
// opt-out link and would message customers without one. An absent key only warns - see
// PrivacyOptionsValidator for why absence is not a boot failure.
PrivacyOptionsValidator.ValidateOrThrow(app.Configuration, app.Logger);

// Outermost middleware: it catches every downstream failure, including the security
// header middleware, and writes the stable error envelope (M-7).
app.UseExceptionHandler();
// HSTS is emitted for HTTPS responses (the default excludes localhost) (M-13).
app.UseHsts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAvelineSecurityHeaders();
app.UseCors(CorsConfiguration.DefaultPolicy);
// Requests that carry an Idempotency-Key get a rewindable body buffer before minimal-API binding
// reads it; the idempotency endpoint filter runs after binding and hashes what this preserved.
app.UseAvelineIdempotencyBodyBuffering();
// Correlation ids must be established before authentication so 401/403 audit logs
// and every downstream log line carry the same request id (FR-6.10).
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
// A cross-organization API key gets 404 before authorization can answer 403 (plan §8.2).
app.UseMiddleware<Aveline.Api.Modules.ApiAccess.Middleware.ApiKeyTenantScopeMiddleware>();
app.UseAuthorization();
app.UseAvelineAuthAudit();
// The media token route carries a bearer credential in its path. The audit middleware above and
// the exception handler outside both log the request path on the way out, so the path is replaced
// with its route template before either reads it (migration plan §7.7: the token is never logged).
app.UseMiddleware<MediaTokenPathRedactionMiddleware>();
app.UseAvelineOnboarding();
// Telemetry is stamped after authentication/authorization so attribution is available, and
// before endpoints so every measured request is captured (FR-6.1). It never fails a request.
app.UseMiddleware<Aveline.Api.Modules.Statistics.Telemetry.ApiTelemetryMiddleware>();
// Quota enforcement is a no-op unless Quotas:EnforcementEnabled=true; when it is on, an
// exhausted meter is rejected with 429 before the endpoint runs (BR-6.5).
app.UseMiddleware<Aveline.Api.Modules.Statistics.Telemetry.QuotaEnforcementMiddleware>();

app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ConversationHub>("/hubs/conversations");
// Health checks are public (the fallback authorization policy requires auth by default).
app.MapSystemHealthEndpoints();
// Prometheus scraping requires an internal service token or the configured scrape token.
app.MapPrometheusScrapingEndpoint("/metrics")
    .RequireAuthorization(AuthorizationConfiguration.MetricsPolicy);

var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
if (app.Environment.IsDevelopment())
{
    // Demonstration endpoints only; never mapped in Production (M-14).
    v1.MapAuthPolicyDemoEndpoints();
    // The mock provider's hosted checkout page and settle endpoint (plan §7.4 guardrail 5): mapped
    // only here, so a Production build does not expose them even if the provider is misconfigured.
    v1.MapMockCheckoutEndpoints();
}
v1.MapAgentEndpoints();
v1.MapUserEndpoints();
v1.MapAdminUserEndpoints();
v1.MapDeviceTokenEndpoints();
v1.MapNotificationEndpoints();
v1.MapAdminEndpoints();
v1.MapAdminOrganizationEndpoints();
v1.MapAuditEndpoints();
v1.MapOrganizationEndpoints();
v1.MapOnboardingEndpoints();
v1.MapIntegrationEndpoints();
v1.MapOrgUsageEndpoints();
v1.MapWebhookEndpoints();
// The anonymous OTP-verified opt-out flow (privacy plan, Phase 4). It is a public,
// unauthenticated customer surface: the OTP is the authentication, and there is deliberately no
// boutique-scoped route that reaches the same code.
v1.MapPrivacyEndpoints();
v1.MapClerkWebhookEndpoints();
v1.MapConversationEndpoints();
v1.MapPricingEndpoints();
v1.MapBlossomEndpoints();
// The payment-intent checkout/poll/cancel routes and the anonymous provider webhook (P2-B2). The
// existing operator top-up route above keeps its shape (decision D7).
v1.MapPaymentEndpoints();
v1.MapPaymentWebhookEndpoints();
v1.MapSubscriptionEndpoints();
v1.MapBillingStatisticsEndpoints();
v1.MapApiAccessEndpoints();
v1.MapStatisticsEndpoints();
v1.MapAnalyticsEndpoints();
v1.MapRevenueModuleEndpoints();
v1.MapCatalogEndpoints();
v1.MapHomeEndpoints();
v1.MapCustomerTenantEndpoints();
v1.MapSearchEndpoints();
v1.MapIncomeEndpoints();
v1.MapDashboardEndpoints();

app.MapBillingEndpoints();
// The P7 admin payment reconciliation read, mapped at the root for the same reason as the Blossom
// drift read above (`app.MapBillingEndpoints`): the admin statistics paths carry the full prefix.
app.MapPaymentModuleEndpoints();
app.MapCustomerConciergeEndpoints();
// The handbook knowledge base (ADR-025): chunk ingest, hybrid search and source listing.
app.MapHandbookEndpoints();
// Internal (service-to-service) conversation transcript read for the agent service (ADR-023, W1.1).
app.MapInternalConversationEndpoints();
app.MapVisualEndpoints();
app.MapStatisticsInternalEndpoints();
// The protected media tier (unit U2.1): the token proxy at /api/v1/media/{token} and the two
// mint endpoints, mapped once here from the lane that owns them.
app.MapMediaEndpoints();
app.MapControllers();

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

