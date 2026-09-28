using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Hubs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Tests;

public class SignalRMessageBroadcasterTests
{
    private sealed record Send(string Group, string Method, object?[] Arguments);

    private sealed class RecordingClients : IHubClients
    {
        public List<Send> Sends { get; } = [];

        public IClientProxy All => throw new NotImplementedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public IClientProxy Client(string connectionId) => throw new NotImplementedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
        public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, Sends);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
        public IClientProxy User(string userId) => throw new NotImplementedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
    }

    private sealed class RecordingClientProxy(string groupName, List<Send> sends) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            sends.Add(new Send(groupName, method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class TestableHubContext : IHubContext<ConversationHub>
    {
        public RecordingClients Clients { get; } = new();
        public IGroupManager Groups => throw new NotImplementedException();
        IHubClients IHubContext<ConversationHub>.Clients => Clients;
    }

    private static ConversationDto Tile() => new(
        Guid.NewGuid(),
        "Salon",
        Guid.NewGuid(),
        "Nadeesha Perera",
        null,
        "thread-1",
        "Active",
        DateTime.UtcNow,
        "A preview",
        "Note",
        "text",
        "Agent",
        AgentKeys.Ava,
        ["draft"]);

    [Fact]
    public async Task BroadcastMessageAsync_SendsReceiveMessage_ToSalonGroup()
    {
        var hubContext = new TestableHubContext();
        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        var conversationId = Guid.NewGuid();
        var dto = new MessageDto(
            Guid.NewGuid(),
            conversationId,
            "Agent",
            AgentKeys.Aveline,
            null,
            MessageKind.Note,
            default,
            null,
            null,
            MessageStatus.Published,
            DateTime.UtcNow);

        await broadcaster.BroadcastMessageAsync(dto);

        var send = Assert.Single(hubContext.Clients.Sends);
        Assert.Equal(GroupName.ForSalon(conversationId), send.Group);
        Assert.Equal("ReceiveMessage", send.Method);
        Assert.Same(dto, Assert.Single(send.Arguments));
    }

    [Fact]
    public async Task BroadcastAgentStateAsync_SendsReceiveAgentState_ToSalonGroup()
    {
        var hubContext = new TestableHubContext();
        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        var conversationId = Guid.NewGuid();
        var dto = new AgentStateDto(conversationId, "searching", AgentKeys.Aveline, null);

        await broadcaster.BroadcastAgentStateAsync(dto);

        var send = Assert.Single(hubContext.Clients.Sends);
        Assert.Equal(GroupName.ForSalon(conversationId), send.Group);
        Assert.Equal("ReceiveAgentState", send.Method);
        Assert.Same(dto, Assert.Single(send.Arguments));
    }

    [Fact]
    public async Task BroadcastConversationChangedAsync_SharedThread_GoesToTheOrgGroup()
    {
        var hubContext = new TestableHubContext();
        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        var organizationId = Guid.NewGuid();
        var tile = Tile();

        await broadcaster.BroadcastConversationChangedAsync(
            new ConversationTile(tile, organizationId, OwnerUserId: null));

        var send = Assert.Single(hubContext.Clients.Sends);
        Assert.Equal(GroupName.ForOrganization(organizationId), send.Group);
        Assert.Equal("ReceiveConversationChanged", send.Method);
        Assert.Same(tile, Assert.Single(send.Arguments));
    }

    [Fact]
    public async Task BroadcastConversationChangedAsync_PrivateSalon_NeverReachesTheOrgGroup()
    {
        // The general Salon is owned by one user (ADR-021). The org group is joined by every
        // active member on connect, so routing this tile there would leak its existence.
        var hubContext = new TestableHubContext();
        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        var organizationId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        await broadcaster.BroadcastConversationChangedAsync(
            new ConversationTile(Tile(), organizationId, ownerUserId));

        var send = Assert.Single(hubContext.Clients.Sends);
        Assert.Equal(GroupName.ForUser(ownerUserId), send.Group);
        Assert.DoesNotContain(
            hubContext.Clients.Sends,
            s => s.Group == GroupName.ForOrganization(organizationId));
    }
}
