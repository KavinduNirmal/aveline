using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

public class NotificationEndpointsIntegrationTests : IAsyncLifetime
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

    private static async Task<Guid> SeedUserAsync(string clerkId, string email)
    {
        await using var context = CreateSeedContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Username = clerkId,
            UserRole = "staff",
            OrganizationRole = "org:boutique_staff",
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            IsActive = true,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<(Guid recordId, Guid inboxId)> SeedNotificationAsync(Guid userId, string title, bool read = false)
    {
        await using var context = CreateSeedContext();
        var record = new NotificationRecord
        {
            OrganizationId = Guid.NewGuid(),
            Type = NotificationType.PaymentConfirmed,
            Title = title,
            Body = "Body of " + title,
            DataJson = "{\"orderId\":\"ord-1\"}",
        };
        context.NotificationRecords.Add(record);
        await context.SaveChangesAsync();

        var inbox = new UserNotification
        {
            UserId = userId,
            NotificationRecordId = record.Id,
            ReadAt = read ? DateTime.UtcNow : null,
        };
        context.UserNotifications.Add(inbox);
        await context.SaveChangesAsync();
        return (record.Id, inbox.Id);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    [Fact]
    public async Task ListNotifications_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListNotifications_ReturnsOnlyCallersVisibleItems()
    {
        const string clerkId = "user_notif_list";
        var userId = await SeedUserAsync(clerkId, "notif.list@aveline.lk");
        await SeedNotificationAsync(userId, "Unread one");
        await SeedNotificationAsync(userId, "Read one", read: true);
        var token = CreateToken(clerkId);

        var response = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/notifications", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, doc.GetProperty("total").GetInt32());
        Assert.Equal(2, doc.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ListNotifications_UnreadOnly_FiltersRead()
    {
        const string clerkId = "user_notif_unread";
        var userId = await SeedUserAsync(clerkId, "notif.unread@aveline.lk");
        await SeedNotificationAsync(userId, "Unread one");
        await SeedNotificationAsync(userId, "Read one", read: true);
        var token = CreateToken(clerkId);

        var response = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/notifications?unreadOnly=true", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, doc.GetProperty("total").GetInt32());
        var item = doc.GetProperty("items")[0];
        Assert.Equal("Unread one", item.GetProperty("title").GetString());
        Assert.False(item.GetProperty("isRead").GetBoolean());
        Assert.Equal("ord-1", item.GetProperty("data").GetProperty("orderId").GetString());
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsCount()
    {
        const string clerkId = "user_notif_count";
        var userId = await SeedUserAsync(clerkId, "notif.count@aveline.lk");
        await SeedNotificationAsync(userId, "Unread one");
        await SeedNotificationAsync(userId, "Read one", read: true);
        var token = CreateToken(clerkId);

        var response = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/notifications/unread-count", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, doc.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task MarkRead_ThenUnreadCountDrops()
    {
        const string clerkId = "user_notif_markread";
        var userId = await SeedUserAsync(clerkId, "notif.markread@aveline.lk");
        var (_, inboxId) = await SeedNotificationAsync(userId, "To read");
        var token = CreateToken(clerkId);

        var patch = await _client.SendAsync(Authorized(HttpMethod.Patch, $"/api/v1/notifications/{inboxId}/read", token));
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var count = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/notifications/unread-count", token));
        var doc = JsonDocument.Parse(await count.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, doc.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task MarkAllRead_MarksAllUnread()
    {
        const string clerkId = "user_notif_markall";
        var userId = await SeedUserAsync(clerkId, "notif.markall@aveline.lk");
        await SeedNotificationAsync(userId, "A");
        await SeedNotificationAsync(userId, "B");
        var token = CreateToken(clerkId);

        var response = await _client.SendAsync(Authorized(HttpMethod.Post, "/api/v1/notifications/read-all", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, doc.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task Dismiss_RemovesFromList()
    {
        const string clerkId = "user_notif_dismiss";
        var userId = await SeedUserAsync(clerkId, "notif.dismiss@aveline.lk");
        var (_, inboxId) = await SeedNotificationAsync(userId, "Dismiss me");
        var token = CreateToken(clerkId);

        var del = await _client.SendAsync(Authorized(HttpMethod.Delete, $"/api/v1/notifications/{inboxId}", token));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/notifications", token));
        var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, doc.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        const string clerkId = "user_notif_404";
        await SeedUserAsync(clerkId, "notif.404@aveline.lk");
        var token = CreateToken(clerkId);

        var response = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/notifications/{Guid.NewGuid()}", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
