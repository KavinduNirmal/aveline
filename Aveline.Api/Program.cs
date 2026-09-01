using Aveline.Api.Configurations;
using Aveline.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAvelineLogging(builder.Configuration);
builder.Services.AddAvelineAuthentication(builder.Configuration);
builder.Services.AddAvelineAuthorization();
builder.Services.AddAvelineCors(builder.Configuration);
builder.Services.AddAgentServiceClient(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAvelineSecurityHeaders();
app.UseCors(CorsConfiguration.DefaultPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseAvelineAuthAudit();

var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapAuthPolicyDemoEndpoints();
v1.MapAgentEndpoints();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;

