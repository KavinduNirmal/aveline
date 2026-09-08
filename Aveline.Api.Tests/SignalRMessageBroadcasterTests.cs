using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Hubs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.AspNetCore.SignalR;

namespace Aveline.Api.Tests;

public class SignalRMessageBroadcasterTests
{
    private sealed class RecordingClients : IHubClients
    {
        public RecordingClientProxy SalonGroup { get; } = new();

        public IClientProxy All => throw new NotImplementedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public IClientProxy Client(string connectionId) => throw new NotImplementedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
        public IClientProxy Group(string groupName) => SalonGroup;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
        public IClientProxy User(string userId) => throw new NotImplementedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[]? Arguments { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Method = method;
            Arguments = args;
            return Task.CompletedTask;
        }
    }

    private sealed class TestableHubContext : IHubContext<ConversationHub>
    {
        public RecordingClients Clients { get; } = new();
        public IGroupManager Groups => throw new NotImplementedException();
        IHubClients IHubContext<ConversationHub>.Clients => Clients;
    }

    [Fact]
    public async Task BroadcastMessageAsync_SendsReceiveMessage_ToSalonGroup()
    {
        var hubContext = new TestableHubContext();
        var broadcaster = new SignalRMessageBroadcaster(hubContext);
        var dto = new MessageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Agent",
            AgentKeys.Aveline,
            null,
            MessageKind.Note,
            default,
            null,
            MessageStatus.Published,
            DateTime.UtcNow);

        await broadcaster.BroadcastMessageAsync(dto);

        Assert.Equal("ReceiveMessage", hubContext.Clients.SalonGroup.Method);
        var sent = Assert.Single(hubContext.Clients.SalonGroup.Arguments!);
        Assert.Same(dto, sent);
    }
}
