using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Hubs;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for the SignalR <see cref="NotificationHub"/> (#90): verifies
/// JWT-authenticated connections are accepted, unauthenticated connections are rejected,
/// and an authenticated user is placed in <c>user:&#123;id&#125;</c> / <c>org:&#123;orgId&#125;</c>
/// groups so the realtime channel can deliver <c>ReceiveNotification</c> messages.
/// </summary>
public class NotificationHubIntegrationTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;

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
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(new[] { new Claim("sub", clerkId) }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(User user, Organization org)> SeedActiveMemberAsync(string clerkId)
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "SignalR",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = "SignalR Boutique",
            Slug = $"signalr-{Guid.NewGuid():N}"[..20],
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return (user, org);
    }

    private HubConnection BuildConnection(string? token)
    {
        var handler = _factory.Server.CreateHandler();
        return new HubConnectionBuilder()
            .WithUrl($"{_factory.Server.BaseAddress}hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => handler;
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            })
            .Build();
    }

    /// <summary>
    /// Sends <paramref name="notification"/> to <paramref name="groupName"/> repeatedly
    /// until the client receives it. <c>StartAsync</c> returns once the handshake completes,
    /// but the server-side <c>OnConnectedAsync</c> (which joins the group) may still be
    /// running, so a single send can race ahead of group membership. Retrying makes the
    /// assertion deterministic.
    /// </summary>
    private static async Task<NotificationDto> SendUntilReceivedAsync(
        IHubContext<NotificationHub> hubContext,
        string groupName,
        NotificationDto notification,
        Task<NotificationDto> received,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (received.IsCompleted)
            {
                return await received;
            }

            await hubContext.Clients.Group(groupName).SendAsync("ReceiveNotification", notification);
            await Task.Delay(100);
        }

        return await received.WaitAsync(timeout);
    }

    [Fact]
    public async Task AuthenticatedUser_Connects_AndReceivesNotificationOnUserGroup()
    {
        var (user, _) = await SeedActiveMemberAsync("hub_auth_user");
        var token = CreateToken(user.ClerkId);

        await using var connection = BuildConnection(token);
        var received = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<NotificationDto>("ReceiveNotification", dto => received.TrySetResult(dto));

        await connection.StartAsync();

        // Send a message to the user:{id} group from the server side.
        var hubContext = _factory.Services.GetRequiredService<IHubContext<NotificationHub>>();
        var notification = new NotificationDto(
            NotificationType.PaymentConfirmed,
            "Payment confirmed",
            "Order #1234 paid",
            new Dictionary<string, string?> { ["orderId"] = "ord-1" });

        var delivered = await SendUntilReceivedAsync(
            hubContext, $"user:{user.Id}", notification, received.Task, TimeSpan.FromSeconds(10));
        Assert.Equal(NotificationType.PaymentConfirmed, delivered.Type);
        Assert.Equal("Payment confirmed", delivered.Title);
        Assert.Equal("ord-1", delivered.Data["orderId"]);
    }

    [Fact]
    public async Task AuthenticatedUser_IsAddedToOrgGroup_AndReceivesOrgBroadcast()
    {
        var (user, org) = await SeedActiveMemberAsync("hub_org_user");
        var token = CreateToken(user.ClerkId);

        await using var connection = BuildConnection(token);
        var received = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<NotificationDto>("ReceiveNotification", dto => received.TrySetResult(dto));

        await connection.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<NotificationHub>>();
        var notification = new NotificationDto(
            NotificationType.NewMessage,
            "New message",
            "A customer sent a message",
            new Dictionary<string, string?>());

        var delivered = await SendUntilReceivedAsync(
            hubContext, $"org:{org.Id}", notification, received.Task, TimeSpan.FromSeconds(10));
        Assert.Equal(NotificationType.NewMessage, delivered.Type);
    }

    [Fact]
    public async Task UnauthenticatedConnection_IsRejected()
    {
        await using var connection = BuildConnection(token: null);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.NotNull(exception);
    }
}
