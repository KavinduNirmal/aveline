using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

public class DeviceTokenEndpointsIntegrationTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId) => CreateToken(userId, "user@aveline.lk");

    private string CreateToken(string userId, string email)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("email", email),
            new("first_name", "Test"),
            new("last_name", "User"),
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private static AppDbContext CreateSeedContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task RegisterDeviceToken_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/me/devices", new { token = "tok", platform = "Android" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDeviceToken_Authenticated_PersistsToken()
    {
        const string clerkId = "user_dev_register";
        const string deviceToken = "fcm-token-register-1";
        var token = CreateToken(clerkId);

        var response = await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/users/me/devices", token,
            new { token = deviceToken, platform = "Android" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateSeedContext();
        var stored = await context.UserDeviceTokens.SingleAsync(t => t.Token == deviceToken);
        Assert.Equal(deviceToken, stored.Token);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task RegisterDeviceToken_SameToken_UpsertsNotDuplicates()
    {
        const string clerkId = "user_dev_upsert";
        const string deviceToken = "fcm-token-upsert-2";
        var token = CreateToken(clerkId);

        var payload = new { token = deviceToken, platform = "IOS" };
        var first = await _client.SendAsync(AuthorizedJson(HttpMethod.Post, "/api/v1/users/me/devices", token, payload));
        var second = await _client.SendAsync(AuthorizedJson(HttpMethod.Post, "/api/v1/users/me/devices", token, payload));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var context = CreateSeedContext();
        Assert.Equal(1, await context.UserDeviceTokens.CountAsync(t => t.Token == deviceToken));
    }

    [Fact]
    public async Task DeleteDeviceToken_Authenticated_DeactivatesToken()
    {
        const string clerkId = "user_dev_delete";
        const string deviceToken = "fcm-token-delete-3";
        var token = CreateToken(clerkId);

        // Register first.
        await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/users/me/devices", token,
            new { token = deviceToken, platform = "Android" }));

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/me/devices/{deviceToken}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var deleteResponse = await _client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        await using var context = CreateSeedContext();
        var stored = await context.UserDeviceTokens.SingleAsync(t => t.Token == deviceToken);
        Assert.False(stored.IsActive);
    }
}
