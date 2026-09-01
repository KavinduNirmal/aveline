using System.Security.Claims;
using Aveline.Api.Configurations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAvelineAuthentication(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Protected endpoint used to verify Clerk JWT validation (Issue #14).
app.MapGet("/api/auth/claims", (ClaimsPrincipal user) =>
{
    var rawClaims = user.Claims
        .Where(c => !c.Type.StartsWith("http://schemas.microsoft.com")
                 && !c.Type.StartsWith("http://schemas.xmlsoap.org"))
        .GroupBy(c => c.Type)
        .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray());

    return Results.Ok(new
    {
        UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
        Email = user.FindFirstValue("email"),
        Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
        Claims = rawClaims,
    });
}).RequireAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
