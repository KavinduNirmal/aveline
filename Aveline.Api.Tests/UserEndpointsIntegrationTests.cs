using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

public class UserEndpointsIntegrationTests : IAsyncLifetime
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

    private string CreateToken(string userId, string? email = null, string? firstName = null, string? lastName = null, string? userRole = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null) claims.Add(new Claim("email", email));
        if (firstName != null) claims.Add(new Claim("first_name", firstName));
        if (lastName != null) claims.Add(new Claim("last_name", lastName));
        if (userRole != null) claims.Add(new Claim("user_role", userRole));

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

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticated_ReturnsStubAndFalseHeader()
    {
        var token = CreateToken("user_clerk_me_test", "me@aveline.lk", "Jane", "Doe", "associate");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Completed-Onboarding"));
        Assert.Equal("false", response.Headers.GetValues("X-Completed-Onboarding").Single());

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal("user_clerk_me_test", doc.GetProperty("clerkId").GetString());
        Assert.Equal("me@aveline.lk", doc.GetProperty("email").GetString());
        Assert.False(doc.GetProperty("hasCompletedOnboarding").GetBoolean());
    }

    [Fact]
    public async Task CompleteOnboarding_ValidPayload_ReturnsUpdatedUserAndTrueHeader()
    {
        var token = CreateToken("user_clerk_onboard_flow", "flow@aveline.lk", "Kasun", "Fernando", "manager");

        // 1. Initial hit creates stub
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // 2. Submit Onboarding Form
        var onboardingPayload = new
        {
            displayName = "Kasun Fernando",
            phoneNumber = "+94771234567",
            address = "75 Union Place, Colombo 02",
            contactPreference = "WhatsApp",
            pushNotificationsEnabled = true
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/onboarding")
        {
            Content = new StringContent(JsonSerializer.Serialize(onboardingPayload), Encoding.UTF8, "application/json")
        };
        postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var postResponse = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var body = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("hasCompletedOnboarding").GetBoolean());
        Assert.Equal("Kasun Fernando", body.GetProperty("displayName").GetString());
        Assert.Equal("+94771234567", body.GetProperty("phoneNumber").GetString());
        Assert.Equal("75 Union Place, Colombo 02", body.GetProperty("address").GetString());

        // 3. Subsequent /users/me returns hasCompletedOnboarding = true and X-Completed-Onboarding: true
        var meCheckRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        meCheckRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meCheckResponse = await _client.SendAsync(meCheckRequest);

        Assert.Equal(HttpStatusCode.OK, meCheckResponse.StatusCode);
        Assert.Equal("true", meCheckResponse.Headers.GetValues("X-Completed-Onboarding").Single());
        var meBody = JsonDocument.Parse(await meCheckResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(meBody.GetProperty("hasCompletedOnboarding").GetBoolean());
    }

    [Fact]
    public async Task CompleteOnboarding_WithoutAddress_ReturnsUpdatedUser()
    {
        var token = CreateToken("user_clerk_no_address", "staff@aveline.lk", "Nimali", "Perera", "staff");

        // 1. Initial hit creates stub
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // 2. Submit Onboarding Form with no address (staff do not provide one)
        var onboardingPayload = new
        {
            displayName = "Nimali Perera",
            phoneNumber = "+94771234567",
            contactPreference = "WhatsApp",
            pushNotificationsEnabled = true
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/onboarding")
        {
            Content = new StringContent(JsonSerializer.Serialize(onboardingPayload), Encoding.UTF8, "application/json")
        };
        postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var postResponse = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var body = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("hasCompletedOnboarding").GetBoolean());
        Assert.Equal("Nimali Perera", body.GetProperty("displayName").GetString());
        Assert.Equal("+94771234567", body.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task CompleteOnboarding_InvalidPayload_Returns400BadRequest()
    {
        var token = CreateToken("user_clerk_invalid_test", "invalid@aveline.lk");

        // Missing required DisplayName / PhoneNumber (Address is now optional).
        var invalidPayload = new
        {
            displayName = ""
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/onboarding")
        {
            Content = new StringContent(JsonSerializer.Serialize(invalidPayload), Encoding.UTF8, "application/json")
        };
        postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
