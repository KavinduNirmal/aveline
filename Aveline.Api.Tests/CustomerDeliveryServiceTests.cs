using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

/// <summary>
/// The outbound delivery path: what leaves the boutique, over which channel, and what the thread
/// records when it does.
/// </summary>
public class CustomerDeliveryServiceTests
{
    private const string CustomerPhone = "94771234567";
    private const string ThreadHandle = "94779999999";

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        public List<Conversation> All { get; } = [];

        public Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.OrganizationId == orgId && c.Id == id));

        public Task<Conversation?> GetVisibleToUserAsync(Guid orgId, Guid id, Guid userId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.OrganizationId == orgId && c.Id == id));

        public Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.ThreadId == threadId));

        public Task<(IReadOnlyList<ConversationListRow> Items, int Total)> ListAsync(
            Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ConversationListRow?> GetRowAsync(Guid conversationId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(
            Guid orgId, Guid userId, Guid? customerId, string threadId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<Conversation> GetOrCreateSalonByExternalRefAsync(
            Guid orgId, string externalRef, string threadId, Guid? customerId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task SaveAsync(Conversation conversation, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeMessageRepository : IMessageRepository
    {
        public List<Message> All { get; } = [];

        public Task<Message?> GetAsync(Guid conversationId, Guid messageId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(m => m.ConversationId == conversationId && m.Id == messageId));

        public Task<Message?> GetByClientMessageIdAsync(Guid conversationId, Guid clientMessageId, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(m =>
                m.ConversationId == conversationId && m.ClientMessageId == clientMessageId));

        public Task<(IReadOnlyList<Message> Items, int Total, int Page)> ListAsync(
            Guid conversationId, int page, int pageSize, Guid? around, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Message>> ListLatestAsync(Guid conversationId, int take, CancellationToken ct)
            => throw new NotImplementedException();

        public Task SaveAsync(Message message, CancellationToken ct)
        {
            All.Add(message);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Message message, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeCustomerRepository : ICustomerRepository
    {
        public List<Customer> All { get; } = [];

        public Task<Customer?> GetAsync(Guid orgId, Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.OrganizationId == orgId && c.Id == id));

        public Task<Customer?> GetByPhoneAsync(Guid orgId, string phoneNumber, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(c => c.OrganizationId == orgId && c.PhoneNumber == phoneNumber));

        public Task<IReadOnlyList<Customer>> ListMatchesAsync(
            Guid orgId, string? name, string? phoneNumber, int limit, CancellationToken ct, string? email = null)
            => throw new NotImplementedException();

        public Task<Customer> AddAsync(Customer customer, CancellationToken ct) => throw new NotImplementedException();

        public Task SaveAsync(Customer customer, CancellationToken ct) => throw new NotImplementedException();
    }

    /// <summary>Credentials the tenant has connected, and nothing else.</summary>
    private sealed class FakeIntegrationService : IIntegrationService
    {
        public Dictionary<IntegrationType, Dictionary<string, string>> Configured { get; } = [];

        public Task<IDictionary<string, string>> GetCredentialsAsync(
            Guid organizationId, IntegrationType type, CancellationToken ct)
            => Configured.TryGetValue(type, out var credentials)
                ? Task.FromResult<IDictionary<string, string>>(credentials)
                : throw new IntegrationNotConfiguredException(type);

        public Task<IntegrationStatusDto> SaveAsync(Guid organizationId, IntegrationType type, SaveIntegrationRequest request, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<IntegrationStatusDto>> ListStatusAsync(Guid organizationId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IntegrationStatusDto> MarkConnectedAsync(Guid organizationId, IntegrationType type, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IntegrationStatusDto> MarkFailedAsync(Guid organizationId, IntegrationType type, string error, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IntegrationStatusDto> MarkExpiredAsync(Guid organizationId, IntegrationType type, string error, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<IntegrationTestResultDto> TestConnectionAsync(Guid organizationId, IntegrationType type, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteAsync(Guid organizationId, IntegrationType type, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class FakeWhatsAppService : IWhatsAppService
    {
        public List<(string PhoneNumberId, string To, string Text)> Sent { get; } = [];
        public WhatsAppSendResult Next { get; set; } = new(true, "wamid.1");

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text, CancellationToken ct)
        {
            Sent.Add((phoneNumberId, to, text));
            return Task.FromResult(Next);
        }

        public Task<WhatsAppSendResult> SendTemplateAsync(
            string accessToken, string phoneNumberId, string to, string templateName,
            string languageCode, IReadOnlyList<object>? components, CancellationToken ct)
            => throw new NotSupportedException("This test double does not send templates.");

        public Task<WhatsAppTestResult> TestConnectionAsync(string accessToken, string phoneNumberId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<WhatsAppMediaResult> GetMediaAsync(string accessToken, string mediaId, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class FakeBroadcaster : IMessageBroadcaster
    {
        public List<MessageDto> Broadcast { get; } = [];

        public Task BroadcastMessageAsync(MessageDto message, CancellationToken ct)
        {
            Broadcast.Add(message);
            return Task.CompletedTask;
        }

        public Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken ct) => Task.CompletedTask;

        public Task BroadcastConversationChangedAsync(ConversationTile tile, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly FakeConversationRepository _conversations = new();
    private readonly FakeMessageRepository _messages = new();
    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeIntegrationService _integrations = new();
    private readonly FakeWhatsAppService _whatsApp = new();
    private readonly FakeBroadcaster _broadcaster = new();
    private readonly CustomerDeliveryService _sut;

    public CustomerDeliveryServiceTests()
    {
        _sut = new CustomerDeliveryService(
            _conversations, _messages, _customers, _integrations, _whatsApp, _broadcaster,
            new NullLogger<CustomerDeliveryService>());
    }

    /// <summary>A customer-bound thread, with WhatsApp connected unless a test says otherwise.</summary>
    private (Guid OrgId, Guid UserId, Guid CustomerId, Conversation Conversation) GivenAThread(
        string? externalRef = null, string? phoneNumber = CustomerPhone, bool withCustomer = true)
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phoneNumber ?? string.Empty,
            FullName = "Nadia Perera",
        };
        if (withCustomer)
        {
            _customers.All.Add(customer);
        }

        var conversation = new Conversation
        {
            OrganizationId = orgId,
            CustomerId = withCustomer ? customer.Id : null,
            ExternalRef = externalRef,
            ThreadId = $"thread-{Guid.NewGuid():N}",
        };
        _conversations.All.Add(conversation);

        _integrations.Configured[IntegrationType.WhatsApp] = new Dictionary<string, string>
        {
            ["accessToken"] = "token",
            ["phoneNumberId"] = "phone-1",
        };

        return (orgId, userId, customer.Id, conversation);
    }

    [Fact]
    public async Task DeliverAsync_SendsOverWhatsApp_AndRecordsWhatWentOut()
    {
        var (orgId, userId, _, conversation) = GivenAThread();

        var outcome = await _sut.DeliverAsync(
            orgId, userId, conversation.Id, "Silk Slip Dress · Size M · LKR 24,000");

        Assert.True(outcome.Delivered);
        Assert.Equal("WhatsApp", outcome.Channel);
        Assert.Equal("wamid.1", outcome.ProviderMessageId);

        var sent = Assert.Single(_whatsApp.Sent);
        Assert.Equal("phone-1", sent.PhoneNumberId);
        Assert.Equal(CustomerPhone, sent.To);
        Assert.Equal("Silk Slip Dress · Size M · LKR 24,000", sent.Text);

        // The thread records the delivery as `Sent`, which is the one status that says "a customer
        // has this" — a `Published` note would be a record that never left the shop.
        var recorded = Assert.Single(_messages.All);
        Assert.Equal(MessageStatus.Sent, recorded.Status);
        Assert.Equal(AuthorKind.User, recorded.AuthorKind);
        Assert.Equal(userId, recorded.AuthorUserId);
        Assert.Contains("Silk Slip Dress", recorded.ContentBlocksJson);

        Assert.Equal(recorded.Id, Assert.Single(_broadcaster.Broadcast).Id);
    }

    [Fact]
    public async Task DeliverAsync_PrefersTheThreadsOwnChannelHandle()
    {
        // The thread's ref is the address the customer wrote from, so a number edited on the
        // customer record must not silently redirect the reply to a different handset.
        var (orgId, userId, _, conversation) = GivenAThread(externalRef: ThreadHandle);

        await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.Equal(ThreadHandle, Assert.Single(_whatsApp.Sent).To);
    }

    [Fact]
    public async Task DeliverAsync_FallsBackToTheCustomerNumber_WhenTheThreadHasNoHandle()
    {
        var (orgId, userId, _, conversation) = GivenAThread();

        await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.Equal(CustomerPhone, Assert.Single(_whatsApp.Sent).To);
    }

    [Fact]
    public async Task DeliverAsync_RefusesAThreadWithNoCustomer_WithoutCallingTheProvider()
    {
        var (orgId, userId, _, conversation) = GivenAThread(withCustomer: false);

        var outcome = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.False(outcome.Delivered);
        Assert.Equal(DeliveryRefusal.NoCustomer, outcome.Refusal);
        Assert.Empty(_whatsApp.Sent);
        Assert.Empty(_messages.All);
    }

    [Fact]
    public async Task DeliverAsync_RefusesACustomerWithNoReachableHandle()
    {
        var (orgId, userId, _, conversation) = GivenAThread(phoneNumber: null);

        var outcome = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.False(outcome.Delivered);
        Assert.Equal(DeliveryRefusal.NoChannelHandle, outcome.Refusal);
        Assert.Empty(_whatsApp.Sent);
        Assert.Empty(_messages.All);
    }

    [Fact]
    public async Task DeliverAsync_RefusesWhenNoChannelIsConnected()
    {
        var (orgId, userId, _, conversation) = GivenAThread();
        _integrations.Configured.Clear();

        var outcome = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.False(outcome.Delivered);
        Assert.Equal(DeliveryRefusal.ChannelNotConnected, outcome.Refusal);
        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task DeliverAsync_SaysInstagramIsUnsupported_RatherThanSendingNothingQuietly()
    {
        // Instagram has credentials in this platform but no provider, so the honest answer is a
        // sentence an associate can act on, not a send that silently does nothing.
        var (orgId, userId, _, conversation) = GivenAThread();
        _integrations.Configured.Clear();
        _integrations.Configured[IntegrationType.Instagram] = new Dictionary<string, string>
        {
            ["clientId"] = "id",
            ["clientSecret"] = "secret",
            ["accessToken"] = "token",
        };

        var outcome = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.False(outcome.Delivered);
        Assert.Equal(DeliveryRefusal.ChannelUnsupported, outcome.Refusal);
        Assert.Contains("Instagram", outcome.Detail);
        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task DeliverAsync_ReportsAProviderRefusal_AndRecordsNothing()
    {
        var (orgId, userId, _, conversation) = GivenAThread();
        _whatsApp.Next = new WhatsAppSendResult(false, Error: "rate limited");

        var outcome = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello");

        Assert.False(outcome.Delivered);
        Assert.Equal(DeliveryRefusal.ProviderRefused, outcome.Refusal);
        // A row saying `Sent` is a claim that a customer was messaged; a refused send must not
        // leave one behind.
        Assert.Empty(_messages.All);
        Assert.Empty(_broadcaster.Broadcast);
    }

    [Fact]
    public async Task DeliverAsync_ReportsAnInvisibleConversation()
    {
        var (_, userId, _, conversation) = GivenAThread();

        var outcome = await _sut.DeliverAsync(
            Guid.NewGuid(), userId, conversation.Id, "hello");

        Assert.False(outcome.Delivered);
        Assert.Equal(DeliveryRefusal.ConversationNotFound, outcome.Refusal);
        Assert.Empty(_whatsApp.Sent);
    }

    [Fact]
    public async Task DeliverAsync_AnswersARetryFromTheRowItAlreadySent()
    {
        // A retry after a timeout must not put the same words in front of the customer twice.
        var (orgId, userId, _, conversation) = GivenAThread();
        var key = Guid.NewGuid();

        var first = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello", key);
        var replay = await _sut.DeliverAsync(orgId, userId, conversation.Id, "hello", key);

        Assert.True(first.Delivered);
        Assert.True(replay.Delivered);
        Assert.Single(_whatsApp.Sent);
        Assert.Single(_messages.All);
    }

    [Fact]
    public async Task DeliverAsync_RefusesBlankWords()
    {
        // A blank body is a caller error rather than a delivery state; the endpoint refuses it with
        // a `400` first, so the service guards the invariant instead of inventing a refusal code no
        // HTTP surface could produce.
        var (orgId, userId, _, conversation) = GivenAThread();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.DeliverAsync(orgId, userId, conversation.Id, "   "));

        Assert.Empty(_whatsApp.Sent);
    }
}
