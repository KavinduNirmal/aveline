using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// U0.4 (S0): the salon upload seam carries the three fields the salon plan requires
/// (<c>Source</c>, <c>CustomerId</c>, <c>ContentHash</c>) from every call path to the store, and
/// the persisted row keeps the byte-level identity the Elle workstream consumes.
/// </summary>
/// <remarks>
/// The store is a capturing decorator over the real <see cref="DatabaseAttachmentStore"/>, so the
/// assertion is on what a call site actually handed the seam — not on a mock of the call site.
/// </remarks>
public class SalonAttachmentRequestTests
{
    private readonly AppDbContext _context;
    private readonly CapturingAttachmentStore _store;
    private readonly ConversationService _sut;

    public SalonAttachmentRequestTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"SalonAttachmentRequest_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _store = new CapturingAttachmentStore(new DatabaseAttachmentStore(_context));
        _sut = new ConversationService(
            new ConversationRepository(_context),
            new MessageRepository(_context),
            new SignOffDecisionRepository(_context),
            new ConversationReadStateRepository(_context),
            new MessageAttachmentRepository(_context),
            _store,
            new UnusedAgentServiceClient(),
            NullLogger<ConversationService>.Instance);
    }

    [Fact]
    public async Task AStaffUploadReachesTheStoreAsWeb_WithTheConversationCustomer_AndTheByteHash()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var salon = await new ConversationRepository(_context)
            .GetOrCreateSalonAsync(orgId, userId, customerId, "thread-staff-web");
        var bytes = new byte[] { 10, 20, 30, 40 };

        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Conversation.Id, bytes, "image/png", "photo.png", 4, 5, CancellationToken.None);

        Assert.NotNull(_store.LastRequest);
        Assert.Equal(MediaSource.Web, _store.LastRequest!.Source);
        Assert.Equal(customerId, _store.LastRequest.CustomerId);
        Assert.Equal(AttachmentContentHash.Compute(bytes), _store.LastRequest.ContentHash);

        // The same identity lands on the row the adapter writes.
        Assert.NotNull(attachment);
        Assert.Equal(AttachmentContentHash.Compute(bytes), attachment!.ContentHash);
    }

    [Fact]
    public async Task AStaffUploadToAGeneralSalon_LeavesCustomerIdNull()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await new ConversationRepository(_context)
            .GetOrCreateSalonAsync(orgId, userId, null, "thread-staff-general");

        await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Conversation.Id, [1], "image/png", "photo.png", null, null, CancellationToken.None);

        Assert.Equal(MediaSource.Web, _store.LastRequest!.Source);
        Assert.Null(_store.LastRequest.CustomerId);
    }

    [Fact]
    public async Task InboundWhatsAppMediaReachesTheStoreAsWhatsApp_WithTheResolvedCustomer_AndTheByteHash()
    {
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var bytes = new byte[] { 7, 8, 9 };

        await _sut.StoreInboundAttachmentAsync(
            orgId, "whatsapp:+94770000000", customerId, bytes, "image/jpeg", "whatsapp-media.jpg",
            CancellationToken.None);

        Assert.NotNull(_store.LastRequest);
        Assert.Equal(MediaSource.WhatsApp, _store.LastRequest!.Source);
        Assert.Equal(customerId, _store.LastRequest.CustomerId);
        Assert.Equal(AttachmentContentHash.Compute(bytes), _store.LastRequest.ContentHash);
    }

    [Fact]
    public async Task InboundWhatsAppMediaFromAPhoneNotOnFile_LeavesCustomerIdNull()
    {
        var orgId = Guid.NewGuid();

        await _sut.StoreInboundAttachmentAsync(
            orgId, "whatsapp:+94771111111", null, [1, 2], "image/jpeg", "whatsapp-unknown.jpg",
            CancellationToken.None);

        Assert.Equal(MediaSource.WhatsApp, _store.LastRequest!.Source);
        Assert.Null(_store.LastRequest.CustomerId);
        Assert.Equal(AttachmentContentHash.Compute([1, 2]), _store.LastRequest.ContentHash);
    }

    [Fact]
    public async Task ContentHashOnThePersistedRowIsTheSha256OfTheStoredBytes()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await new ConversationRepository(_context)
            .GetOrCreateSalonAsync(orgId, userId, null, "thread-persisted-hash");
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6 };

        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Conversation.Id, bytes, "image/jpeg", "photo.jpg", null, null,
            CancellationToken.None);

        var row = await _context.MessageAttachments.SingleAsync(a => a.Id == attachment!.Id);
        Assert.Equal(bytes, row.ImageData);
        Assert.Equal(AttachmentContentHash.Compute(row.ImageData!), row.ContentHash);
        Assert.Equal(64, row.ContentHash!.Length);
    }

    [Fact]
    public void TheConfigurationDeclaresTheNullableContentHashColumn()
    {
        var entity = _context.Model.FindEntityType(typeof(MessageAttachment))!;

        var property = entity.FindProperty(nameof(MessageAttachment.ContentHash))!;

        Assert.True(property.IsNullable);
        Assert.Equal(64, property.GetMaxLength());
    }

    [Fact]
    public void TheConfigurationDeclaresTheOrganizationAndContentHashIndex()
    {
        var entity = _context.Model.FindEntityType(typeof(MessageAttachment))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(MessageAttachment.OrganizationId),
                nameof(MessageAttachment.ContentHash),
            }));
    }

    /// <summary>Delegates to the real adapter and keeps what the seam was handed.</summary>
    private sealed class CapturingAttachmentStore(IAttachmentStore inner) : IAttachmentStore
    {
        public AttachmentStoreRequest? LastRequest { get; private set; }

        public string Provider => inner.Provider;

        public Task<MessageAttachment> StoreAsync(AttachmentStoreRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return inner.StoreAsync(request, ct);
        }

        public Task<Stream?> OpenReadAsync(MessageAttachment attachment, CancellationToken ct)
            => inner.OpenReadAsync(attachment, ct);

        public Task DeleteAsync(MessageAttachment attachment, CancellationToken ct)
            => inner.DeleteAsync(attachment, ct);
    }

    private sealed class UnusedAgentServiceClient : IAgentServiceClient
    {
        public Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The attachment seam must not call the agent.");

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The attachment seam must not call the agent.");
    }
}
