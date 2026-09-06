using Aveline.Api.Common.Middleware;
using Aveline.Api.Configurations;
using Aveline.Api.Endpoints;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Modules.Admin.Repositories;
using Aveline.Api.Modules.Admin.Services;
using Aveline.Api.Modules.Billing;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddAvelineLogging(builder.Configuration);
builder.Services.AddAvelineDatabase(builder.Configuration);
builder.Services.AddAvelineCache(builder.Configuration);
builder.Services.AddAvelineAuthentication(builder.Configuration);
builder.Services.AddAvelineAuthorization();
builder.Services.AddAvelineCors(builder.Configuration);
builder.Services.AddAgentServiceClient(builder.Configuration);
builder.Services.AddClerkAdminClient();
builder.Services.AddBillingModule();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserCacheService, UserCacheService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();

builder.Services.AddScoped<IAdminApprovalRepository, AdminApprovalRepository>();
builder.Services.AddScoped<IAdminApprovalService, AdminApprovalService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAvelineSecurityHeaders();
app.UseCors(CorsConfiguration.DefaultPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseAvelineAuthAudit();
app.UseAvelineOnboarding();

var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapAuthPolicyDemoEndpoints();
v1.MapAgentEndpoints();
v1.MapUserEndpoints();
v1.MapAdminEndpoints();
v1.MapOrganizationEndpoints();

app.MapBillingEndpoints();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;

