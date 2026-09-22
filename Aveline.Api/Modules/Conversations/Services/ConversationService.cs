using System.Net.Http.Json;
using System.Text.Json;
using Aveline.Api.Common.Media;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Conversations.Services;

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly ISignOffDecisionRepository _signOffDecisions;
    private readonly IConversationReadStateRepository _readStates;
    private readonly IMessageAttachmentRepository _attachments;
    private readonly IAttachmentStore _attachmentStore;
    private readonly IAgentServiceClient _agentClient;
    private readonly ILogger<ConversationService> _logger;
    private readonly MediaTokenMintService? _mediaTokens;

    /// <summary>
    /// <paramref name="mediaTokens"/> mints the bridge's absolute <c>image_url</c>. It is optional
    /// only so a fixture that constructs this service directly keeps its existing arity; the
    /// container always supplies it (<c>MediaModule</c> registers it, and <c>Program.cs</c>
    /// registers the media module before this one).
    /// </summary>
    public ConversationService(
        IConversationRepository conversations,
        IMessageRepository messages,
        ISignOffDecisionRepository signOffDecisions,
        IConversationReadStateRepository readStates,
        IMessageAttachmentRepository attachments,
        IAttachmentStore attachmentStore,
        IAgentServiceClient agentClient,
        ILogger<ConversationService> logger,
        MediaTokenMintService? mediaTokens = null)
    {
        _conversations = conversations;
        _messages = messages;
        _signOffDecisions = signOffDecisions;
        _readStates = readStates;
        _attachments = attachments;
        _attachmentStore = attachmentStore;
        _agentClient = agentClient;
        _logger = logger;
        _mediaTokens = mediaTokens;
    }

    public async Task<ConversationDto> GetOrCreateSalonAsync(
        Guid orgId,
        Guid userId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        var threadId = Guid.NewGuid().ToString("N");
        var (conversation, created) = await _conversations.GetOrCreateSalonAsync(orgId, userId, customerId, threadId, cancellationToken);

        // A brand-new Salon gets a predefined Aveline greeting so the user always has a
        // warm first message (no LLM required).
        if (created)
        {
            await SeedAvelineGreetingAsync(conversation, customerId, cancellationToken);
        }

        return ConversationDto.From(conversation);
    }

    /// <inheritdoc />
    public async Task<bool> EnsureCustomerSalonAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var threadId = Guid.NewGuid().ToString("N");
        // The repository matches a client-bound Salon on (org, customer, kind) without the user, so
        // the id it receives is irrelevant for a customer thread. `Guid.Empty` states the
        // organization-shared invariant (ADR-021) at the call site instead of implying an owner.
        var (conversation, created) = await _conversations.GetOrCreateSalonAsync(
            orgId, Guid.Empty, customerId, threadId, cancellationToken);

        if (created)
        {
            await SeedAvelineGreetingAsync(conversation, customerId, cancellationToken);
        }

        return created;
    }

    /// <summary>
    /// Inserts Aveline's predefined welcome message into a freshly created Salon.
    /// </summary>
    private async Task SeedAvelineGreetingAsync(
        Conversation conversation,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        var greeting = customerId is null
            ? "Welcome to your Salon. I'm Aveline, your boutique concierge. Ask me about a customer, a piece in your catalogue, or a price - and I'll bring in Ava, Elle, or Lina when they can help."
            : "Welcome. I'm Aveline, your boutique concierge. I'll help you look after this customer.";
        var message = new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = AgentKeys.Aveline,
            Kind = MessageKind.Note,
            ContentBlocksJson = JsonSerializer.Serialize(new[]
            {
                new { type = "text", text = greeting },
            }),
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);
    }

    public async Task<ConversationDto?> GetAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken);
        return conversation is null ? null : ConversationDto.From(conversation);
    }

    public async Task<ConversationDto?> SelectCustomerAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid customerId,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        // Bind the Salon to the customer the staff picked so later messages carry the context.
        // Binding also promotes the thread to organization-shared, so it stops being owned by
        // whichever user happened to resolve the customer (ADR-021).
        if (conversation.CustomerId != customerId || conversation.OwnerUserId is not null)
        {
            conversation.CustomerId = customerId;
            conversation.OwnerUserId = null;
            await _conversations.SaveAsync(conversation, cancellationToken);
        }

        // Re-run the original question (or a fallback) with the resolved customer in context so
        // Ava retrieves their profile/events. Best-effort like the other agent triggers.
        await TriggerAgentAsync(
            conversation,
            string.IsNullOrWhiteSpace(query) ? "Summarise recent activity for this customer." : query,
            customerId,
            cancellationToken: cancellationToken);

        return ConversationDto.From(conversation);
    }

    public async Task<(IReadOnlyList<ConversationDto> Items, int Total)> ListAsync(
        Guid orgId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (rows, total) = await _conversations.ListAsync(orgId, userId, page, pageSize, cancellationToken);
        return (rows.Select(ConversationTileMapper.ToDto).ToList(), total);
    }

    public async Task<ConversationTile?> GetTileAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var row = await _conversations.GetRowAsync(conversationId, cancellationToken);
        return row is null
            ? null
            : new ConversationTile(
                ConversationTileMapper.ToDto(row),
                row.Conversation.OrganizationId,
                row.Conversation.OwnerUserId);
    }

    public async Task<(IReadOnlyList<MessageDto> Items, int Total, int Page)> ListMessagesAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            throw new InvalidOperationException("Conversation not found in this organization.");
        }

        var (items, total, effectivePage) = await _messages.ListAsync(
            conversationId, page, pageSize, around, cancellationToken);
        return (items.Select(MessageDto.From).ToList(), total, effectivePage);
    }

    public async Task<ConversationHistoryDto?> GetHistoryAsync(
        Guid orgId,
        Guid conversationId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Organization-scoped, not user-scoped: the agent service is a trusted internal caller and
        // has no staff user id (see IConversationService.GetHistoryAsync).
        var conversation = await _conversations.GetAsync(orgId, conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        // Clamp rather than reject: the window is a context-budget decision made by the caller
        // (ADR-023), and a nonsensical value should degrade to something bounded, not 400.
        var take = Math.Clamp(limit, 1, MaxHistoryTurns);
        var messages = await _messages.ListLatestAsync(conversationId, take, cancellationToken);

        var items = messages
            .Select(message => new ConversationHistoryTurnDto(
                message.Id,
                message.AuthorKind.ToString(),
                message.AuthorAgentKey,
                message.Kind.ToString(),
                ConversationBlockText.Flatten(message.ContentBlocksJson),
                message.CreatedAt))
            .ToList();

        return new ConversationHistoryDto(conversationId, orgId, items);
    }

    /// <summary>The hard ceiling on a history window, so a caller cannot ask for an unbounded read.</summary>
    private const int MaxHistoryTurns = 200;

    public async Task<MessageDto> SendStaffNoteAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        string text,
        Guid? clientMessageId = null,
        IReadOnlyList<Guid>? attachmentIds = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken)
            ?? throw new InvalidOperationException("Conversation not found in this organization.");

        // A retry of one composed message must land on the row the first attempt stored. The
        // same key with the same words is a replay (200 with that row, no second agent brief);
        // the same key with different words is refused rather than silently written twice.
        // This lookup runs before the attachments are resolved: the stored row already carries
        // its blocks, so a retry naming the ids the first attempt bound must return that row
        // rather than re-resolve (and refuse) ids that are, correctly, already attached.
        if (clientMessageId is not null)
        {
            var existing = await _messages.GetByClientMessageIdAsync(
                conversationId, clientMessageId.Value, cancellationToken);
            if (existing is not null)
            {
                return ResolveReplay(existing, text, conversationId, clientMessageId.Value);
            }
        }

        // A fresh write resolves and validates its attachments **before** the message is
        // written, so a bad id leaves the uploads unbound and sweepable rather than
        // half-binding them to a stored message.
        var attachments = await ResolveAttachmentsAsync(orgId, conversationId, attachmentIds, cancellationToken);
        var contentBlocksJson = SerializeNoteBlocks(text, attachments);

        var message = new Message
        {
            ConversationId = conversationId,
            AuthorKind = AuthorKind.User,
            AuthorUserId = userId,
            Kind = MessageKind.Note,
            ContentBlocksJson = contentBlocksJson,
            ClientMessageId = clientMessageId,
            Status = MessageStatus.Published,
        };

        try
        {
            await _messages.SaveAsync(message, cancellationToken);
        }
        catch (DbUpdateException) when (clientMessageId is not null)
        {
            // A concurrent duplicate won the filtered unique index first. Adopt the row it
            // wrote rather than failing the retry the caller is making.
            var existing = await _messages.GetByClientMessageIdAsync(
                conversationId, clientMessageId.Value, cancellationToken);
            if (existing is not null)
            {
                return ResolveReplay(existing, text, conversationId, clientMessageId.Value);
            }
            throw;
        }

        // Bound after the row exists, inside the same request: an upload that is never sent
        // stays unbound and the sweep collects it.
        foreach (var attachment in attachments)
        {
            attachment.MessageId = message.Id;
            attachment.BoundAtUtc = DateTime.UtcNow;
            await _attachments.SaveAsync(attachment, cancellationToken);
        }

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        // The note's own attachments are described to the agent: the bridge carries their
        // identity and an absolute tokenised URL, never their bytes (salon plan §9.3 site 2).
        await TriggerAgentAsync(
            conversation, text, attachments: attachments, cancellationToken: cancellationToken);

        return MessageDto.From(message);
    }

    /// <summary>
    /// The attachments a send may bind, refusing the whole send when any id is unknown, foreign
    /// to this conversation, already bound, or over the per-message cap.
    /// </summary>
    private async Task<IReadOnlyList<MessageAttachment>> ResolveAttachmentsAsync(
        Guid orgId,
        Guid conversationId,
        IReadOnlyList<Guid>? attachmentIds,
        CancellationToken cancellationToken)
    {
        if (attachmentIds is null || attachmentIds.Count == 0)
        {
            return [];
        }

        var distinct = attachmentIds.Distinct().ToList();
        if (distinct.Count > MediaContentTypes.MaxPerMessage)
        {
            throw new AttachmentBindingException(
                $"A message may carry at most {MediaContentTypes.MaxPerMessage} attachments.");
        }

        var bindable = await _attachments.GetBindableAsync(orgId, conversationId, distinct, cancellationToken);
        if (bindable.Count != distinct.Count)
        {
            throw new AttachmentBindingException(
                "An attachment is unknown, belongs to another conversation, or is already attached to a message.");
        }

        return bindable;
    }

    /// <summary>
    /// The stored blocks: the note's own words, then one <c>attachment</c> block per file.
    /// </summary>
    private static string SerializeNoteBlocks(string text, IReadOnlyList<MessageAttachment> attachments)
    {
        var blocks = new List<Dictionary<string, object?>>
        {
            new() { ["type"] = "text", ["text"] = text },
        };

        foreach (var attachment in attachments)
        {
            blocks.Add(new Dictionary<string, object?>
            {
                ["type"] = "attachment",
                ["attachmentId"] = attachment.Id,
                ["url"] = attachment.Url,
                ["contentType"] = attachment.ContentType,
                ["fileName"] = attachment.FileName,
                ["sizeBytes"] = attachment.SizeBytes,
                ["width"] = attachment.Width,
                ["height"] = attachment.Height,
            });
        }

        return JsonSerializer.Serialize(blocks);
    }

    /// <summary>The note's own words, read back out of its stored blocks.</summary>
    private static string? ReadNoteText(string? contentBlocksJson)
    {
        if (string.IsNullOrWhiteSpace(contentBlocksJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(contentBlocksJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (var block in document.RootElement.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && block.TryGetProperty("text", out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public async Task<MessageAttachment?> CreateAttachmentAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        byte[] bytes,
        string contentType,
        string fileName,
        int? width,
        int? height,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        return await _attachmentStore.StoreAsync(
            new AttachmentStoreRequest(
                orgId, conversationId, userId, bytes, contentType, fileName, width, height,
                // A staff device uploaded this through the Salon, so the provenance is `web`; the
                // media concerns the conversation's customer when the thread is bound to one.
                MediaSource.Web,
                conversation.CustomerId,
                AttachmentContentHash.Compute(bytes)),
            cancellationToken);
    }

    public async Task<MessageAttachment?> GetAttachmentAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        // Visibility is checked on the conversation, not the row: an attachment is readable
        // exactly when the thread it belongs to is.
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        return await _attachments.GetAsync(orgId, conversationId, attachmentId, cancellationToken);
    }

    public Task<Stream?> OpenAttachmentAsync(
        MessageAttachment attachment,
        CancellationToken cancellationToken = default)
        => _attachmentStore.OpenReadAsync(attachment, cancellationToken);

    public async Task<MarkReadOutcome> MarkReadAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid lastReadMessageId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            return MarkReadOutcome.ConversationNotFound;
        }

        var message = await _messages.GetAsync(conversationId, lastReadMessageId, cancellationToken);
        if (message is null)
        {
            return MarkReadOutcome.MessageNotFound;
        }

        var state = await _readStates.GetAsync(orgId, userId, conversationId, cancellationToken);

        // Monotonic by (CreatedAt, Id): a second device re-opening an older position must not
        // un-read what the first device already read. The marker's own message is loaded to
        // compare; when it is gone there is nothing to prove a regression, so the write stands.
        if (state?.LastReadMessageId is Guid currentId && currentId != Guid.Empty)
        {
            var current = await _messages.GetAsync(conversationId, currentId, cancellationToken);
            if (current is not null && !IsAfter(message, current))
            {
                return MarkReadOutcome.Ignored;
            }
        }

        if (state is null)
        {
            state = new ConversationReadState
            {
                OrganizationId = orgId,
                UserId = userId,
                ConversationId = conversationId,
                LastReadMessageId = lastReadMessageId,
                LastReadAtUtc = DateTime.UtcNow,
            };
        }
        else
        {
            state.LastReadMessageId = lastReadMessageId;
            state.LastReadAtUtc = DateTime.UtcNow;
        }

        await _readStates.SaveAsync(state, cancellationToken);
        return MarkReadOutcome.Recorded;
    }

    /// <summary>Whether <paramref name="candidate"/> is a later position than <paramref name="current"/>.</summary>
    private static bool IsAfter(Message candidate, Message current)
        => candidate.CreatedAt > current.CreatedAt
           || (candidate.CreatedAt == current.CreatedAt && candidate.Id.CompareTo(current.Id) > 0);

    /// <summary>
    /// The stored row a retry named, or a conflict when the key was used for different words.
    /// </summary>
    private static MessageDto ResolveReplay(
        Message stored,
        string expectedText,
        Guid conversationId,
        Guid clientMessageId)
    {
        // Compared on the note's own words rather than the whole block array, so a retry that
        // carries the same text alongside its attachments replays instead of conflicting.
        if (!string.Equals(ReadNoteText(stored.ContentBlocksJson), expectedText, StringComparison.Ordinal))
        {
            throw new MessageIdempotencyConflictException(conversationId, clientMessageId);
        }

        return MessageDto.From(stored);
    }

    public async Task<MessageDto> ApplyAgentMessageAsync(
        AgentMessageEvent evt,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetByThreadIdAsync(evt.ThreadId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation for thread {evt.ThreadId} not found.");
        if (conversation is null)
        {
            // The event may arrive before the conversation is known to this instance; the
            // caller decides whether to retry. Surface a clear error rather than silently
            // dropping the message.
            throw new InvalidOperationException($"Conversation {evt.ConversationId} not found.");
        }

        var contentBlocksJson = evt.ContentBlocks.ValueKind == JsonValueKind.Undefined
            ? "[]"
            : evt.ContentBlocks.GetRawText();

        var isSignOff = evt.Kind == MessageKind.SignOff;

        var message = new Message
        {
            // The agent's message.created payload only carries a thread_id, so the
            // conversation is resolved above by thread id. Use its real id here rather
            // than evt.ConversationId (which is Guid.Empty for agent events).
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = evt.AgentKey,
            Kind = evt.Kind,
            ContentBlocksJson = contentBlocksJson,
            // Bind a SignOff to the exact payload the human will approve, so a later edit
            // to the message cannot change what was approved.
            ContentHash = isSignOff ? ContentHash.Compute(contentBlocksJson) : null,
            ReplyToMessageId = evt.ReplyToMessageId,
            WorkflowRunId = evt.WorkflowRunId,
            // A SignOff is staged until a human decides it; everything else is visible at once.
            Status = isSignOff ? MessageStatus.AwaitingSignOff : MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        if (isSignOff)
        {
            // Pause the thread on the associate. This is what lights the row's `approval`
            // marker, makes the thread's decision affordance appear, and lets
            // DecideSignOffAsync's guard pass - all three read this state.
            conversation.Status = ConversationStatus.AwaitingSignOff;
        }
        await _conversations.SaveAsync(conversation, cancellationToken);

        return MessageDto.From(message);
    }

    public async Task<MessageDto?> ApplyAgentMessageUpdateAsync(
        AgentMessageUpdateEvent evt,
        CancellationToken cancellationToken = default)
    {
        var message = await _messages.GetAsync(evt.ConversationId, evt.MessageId, cancellationToken);
        if (message is null)
        {
            // Unknown update target; the listener treats this as a benign skip.
            return null;
        }

        if (evt.Status is not null)
        {
            message.Status = evt.Status.Value;
        }

        if (evt.ContentBlocks.ValueKind != JsonValueKind.Undefined)
        {
            message.ContentBlocksJson = evt.ContentBlocks.GetRawText();
            // Re-bind a SignOff to the revised payload so a later decision reflects exactly
            // what the human will see.
            message.ContentHash = message.Kind == MessageKind.SignOff
                ? ContentHash.Compute(message.ContentBlocksJson)
                : null;
        }

        await _messages.UpdateAsync(message, cancellationToken);

        _logger.LogInformation(
            "Applied agent update to message {MessageId} in conversation {ConversationId}.",
            evt.MessageId, evt.ConversationId);

        return MessageDto.From(message);
    }

    public async Task<MessageDto> DecideSignOffAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        bool approved,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken)
            ?? throw new SignOffNotFoundException("Conversation not found in this organization.");

        var message = await _messages.GetAsync(conversationId, messageId, cancellationToken)
            ?? throw new SignOffNotFoundException("Message not found in this conversation.");

        if (message.Kind != MessageKind.SignOff)
        {
            throw new InvalidOperationException("Only a SignOff message can be decided.");
        }
        if (message.Status != MessageStatus.AwaitingSignOff)
        {
            throw new InvalidOperationException("This SignOff is not awaiting a decision.");
        }

        // Bind the decision to the exact payload the human saw. If the message content was
        // rewritten after display, the hash no longer matches and the approval is void.
        var currentHash = ContentHash.Compute(message.ContentBlocksJson);
        if (!string.Equals(currentHash, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This SignOff has changed since it was displayed. Please review the latest version before deciding.");
        }

        // Record the decision out of band (immutable, separate from the mutable message row)
        // so a later edit to the message cannot retroactively change what was approved.
        await _signOffDecisions.SaveAsync(new SignOffDecision
        {
            OrganizationId = orgId,
            ConversationId = conversationId,
            MessageId = messageId,
            ContentHash = currentHash,
            Kind = approved ? SignOffDecisionKind.Approved : SignOffDecisionKind.Rejected,
            DecidedBy = userId,
            DecidedAt = DateTime.UtcNow,
        }, cancellationToken);

        message.Status = approved ? MessageStatus.Published : MessageStatus.Cancelled;
        // `SaveAsync` inserts; this message was loaded, so re-adding it would violate the
        // primary key. `UpdateAsync` is the write for an existing row.
        await _messages.UpdateAsync(message, cancellationToken);

        conversation.Status = approved ? ConversationStatus.Active : ConversationStatus.Resolved;
        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        _logger.LogInformation(
            "SignOff {MessageId} {Decision} by user {UserId} in conversation {ConversationId} (hash {ContentHash}).",
            messageId, approved ? "approved" : "rejected", userId, conversationId, currentHash);

        // NOTE (ADR-018): resuming the paused LangGraph workflow via the conversation's
        // thread_id is intentionally deferred. The decision is recorded and the message/
        // conversation statuses are updated here; wiring the actual resume is future work and
        // must not be faked with a pretend interrupt.
        _logger.LogInformation(
            "SignOff resume for thread {ThreadId} is deferred (no LangGraph resume wired, ADR-018).",
            conversation.ThreadId);

        return MessageDto.From(message);
    }

    public async Task<MessageDto> RevokeSignOffAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetVisibleToUserAsync(orgId, conversationId, userId, cancellationToken)
            ?? throw new SignOffNotFoundException("Conversation not found in this organization.");

        var message = await _messages.GetAsync(conversationId, messageId, cancellationToken)
            ?? throw new SignOffNotFoundException("Message not found in this conversation.");

        if (message.Kind != MessageKind.SignOff)
        {
            throw new InvalidOperationException("Only a SignOff message can be decided.");
        }

        var newest = await _signOffDecisions.GetByMessageIdAsync(messageId, cancellationToken);
        if (newest is null || newest.Kind != SignOffDecisionKind.Approved)
        {
            throw new InvalidOperationException("This SignOff is not approved.");
        }

        // Appended, never mutated: the approval row stays as the record of what was decided,
        // and the revocation is a second row. The hash is carried over so the same payload is
        // decidable again.
        await _signOffDecisions.SaveAsync(new SignOffDecision
        {
            OrganizationId = orgId,
            ConversationId = conversationId,
            MessageId = messageId,
            ContentHash = newest.ContentHash,
            Kind = SignOffDecisionKind.Revoked,
            DecidedBy = userId,
            DecidedAt = DateTime.UtcNow,
        }, cancellationToken);

        message.Status = MessageStatus.AwaitingSignOff;
        await _messages.UpdateAsync(message, cancellationToken);

        // Back to the associate's queue: this is what lights the inbox's `approval` marker
        // again, since it is derived from this status.
        conversation.Status = ConversationStatus.AwaitingSignOff;
        await _conversations.SaveAsync(conversation, cancellationToken);

        _logger.LogInformation(
            "SignOff {MessageId} revoked by user {UserId} in conversation {ConversationId} (reason: {Reason}).",
            messageId, userId, conversationId, string.IsNullOrWhiteSpace(reason) ? "none" : reason);

        return MessageDto.From(message);
    }

    public async Task<MessageAttachment> StoreInboundAttachmentAsync(
        Guid orgId,
        string externalRef,
        Guid? customerId,
        byte[] bytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetOrCreateSalonByExternalRefAsync(
            orgId, externalRef, Guid.NewGuid().ToString("N"), customerId, cancellationToken);

        // No uploader: the bytes came from the customer's channel, not from a staff device.
        return await _attachmentStore.StoreAsync(
            new AttachmentStoreRequest(
                orgId, conversation.Id, null, bytes, contentType, fileName, null, null,
                // Fetched server-side from the provider's media id, so the provenance is the
                // channel, not the Salon; the customer is the caller's resolution of the phone.
                MediaSource.WhatsApp,
                customerId,
                AttachmentContentHash.Compute(bytes)),
            cancellationToken);
    }

    public async Task<MessageDto> RecordInboundClientMessageAsync(
        Guid orgId,
        string externalRef,
        string from,
        string text,
        Guid? customerId,
        Guid? attachmentId = null,
        CancellationToken cancellationToken = default)
    {
        var threadId = Guid.NewGuid().ToString("N");
        // The customer context is bound at creation (D1): the thread exists for the identified
        // customer, and a phone that is not on file is a rendered state rather than a silent one.
        var conversation = await _conversations.GetOrCreateSalonByExternalRefAsync(
            orgId, externalRef, threadId, customerId, cancellationToken);

        // Media the webhook already stored, so the same message the customer sent arrives as the
        // channel's words plus the file rather than as a dropped payload.
        var attachment = attachmentId is null
            ? null
            : await _attachments.GetAsync(orgId, conversation.Id, attachmentId.Value, cancellationToken);

        var blocks = new List<Dictionary<string, object?>>
        {
            new() { ["type"] = "client_message", ["from"] = from, ["text"] = text },
        };
        if (attachment is not null)
        {
            blocks.Add(new Dictionary<string, object?>
            {
                ["type"] = "attachment",
                ["attachmentId"] = attachment.Id,
                ["url"] = attachment.Url,
                ["contentType"] = attachment.ContentType,
                ["fileName"] = attachment.FileName,
                ["sizeBytes"] = attachment.SizeBytes,
                ["width"] = attachment.Width,
                ["height"] = attachment.Height,
            });
        }

        var message = new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.System,
            Kind = MessageKind.ClientMessage,
            ContentBlocksJson = JsonSerializer.Serialize(blocks),
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        if (attachment is not null)
        {
            attachment.MessageId = message.Id;
            attachment.BoundAtUtc = DateTime.UtcNow;
            await _attachments.SaveAsync(attachment, cancellationToken);
        }

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        // Best-effort: ask the agent to draft a response to this inbound client message into
        // the Salon (ADR-016). The client's phone is forwarded so the memory agent can attempt
        // to identify the customer and personalize the draft. Replies arrive later as
        // message.created events. The file the customer sent travels too: the agent is told
        // what it is and where to fetch it, never handed its bytes (salon plan §9.3 site 3).
        await TriggerInboundDraftAsync(conversation, from, text, attachment, cancellationToken);

        return MessageDto.From(message);
    }

    private async Task TriggerInboundDraftAsync(
        Conversation conversation,
        string from,
        string text,
        MessageAttachment? attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            var (attachments, imageUrl) = DescribeAttachments(
                conversation.OrganizationId,
                attachment is null ? Array.Empty<MessageAttachment>() : new[] { attachment });

            var payload = new
            {
                query = text,
                thread_id = conversation.ThreadId,
                org_context = new
                {
                    organization_id = conversation.OrganizationId,
                    // The agent reads its transcript window from this id (ADR-023, W1.3). Without
                    // it the workflow sees only the newest message and cannot resolve references.
                    conversation_id = conversation.Id,
                    phone_number = from,
                    channel = "whatsapp",
                    direction = "inbound",
                    attachments,
                    image_url = imageUrl,
                },
            };
            using var content = JsonContent.Create(payload);
            // Best-effort: a failure to reach the agent must not fail the webhook. The agent
            // replies arrive later as message.created events.
            var response = await _agentClient.PostAsync("/agents/query", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent inbound draft returned {StatusCode} for conversation {ConversationId}.",
                    (int)response.StatusCode,
                    conversation.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger inbound draft for conversation {ConversationId}.", conversation.Id);
        }
    }

    /// <summary>
    /// The agent brief. <paramref name="attachments"/> is what the caller must describe; the
    /// payload always carries an <c>attachments</c> array and, when one of them is an image the
    /// vision provider can read, an absolute <c>image_url</c> minted for that fetch
    /// (salon plan §9.1-§9.3, strategy §7 check 5).
    /// </summary>
    internal async Task TriggerAgentAsync(
        Conversation conversation,
        string query,
        Guid? customerId = null,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (described, imageUrl) = DescribeAttachments(
                conversation.OrganizationId, attachments ?? Array.Empty<MessageAttachment>());

            var payload = new
            {
                query,
                thread_id = conversation.ThreadId,
                org_context = new
                {
                    organization_id = conversation.OrganizationId,
                    // See TriggerInboundDraftAsync: the transcript window is keyed by this id.
                    conversation_id = conversation.Id,
                    customer_id = customerId,
                    attachments = described,
                    image_url = imageUrl,
                },
            };
            using var content = JsonContent.Create(payload);
            // Best-effort: a failure to reach the agent must not fail the staff note. The
            // agent replies arrive later as message.created events.
            var response = await _agentClient.PostAsync("/agents/query", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent query returned {StatusCode} for conversation {ConversationId}.",
                    (int)response.StatusCode,
                    conversation.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger agent for conversation {ConversationId}.", conversation.Id);
        }
    }

    /// <summary>
    /// The bridge's two halves: the authoritative identity list, and the compatibility
    /// <c>image_url</c> for the reader that already exists (<c>concierge_workflow.py:187</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>attachments</c> is the contract; <c>image_url</c> is a bridge, removed when the Python
    /// side sends references and no longer reads it (salon plan §9.1). The URL is absolute and
    /// Aveline-minted with the <c>vision.analyze</c> scope, so the external provider can fetch it
    /// and a leaked value is worth one image for ten minutes. It is never returned to an HTTP
    /// caller: it travels only in this service-to-service payload.
    /// </para>
    /// <para>
    /// A token is minted only for a type <see cref="VisionContentTypes"/> admits: a HEIC or a PDF
    /// is listed and not tokenised (strategy §3.6). Minting is best-effort, like the trigger
    /// itself, so a host with no signing key still sends the identity — the half of the contract
    /// that is not a credential.
    /// </para>
    /// </remarks>
    private (IReadOnlyList<object> Attachments, string? ImageUrl) DescribeAttachments(
        Guid organizationId,
        IReadOnlyList<MessageAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return (Array.Empty<object>(), null);
        }

        return (
            attachments.Select(DescribeAttachment).ToList(),
            MintImageUrl(organizationId, attachments));
    }

    private static object DescribeAttachment(MessageAttachment attachment) => new
    {
        attachmentId = attachment.Id,
        contentType = attachment.ContentType,
        fileName = attachment.FileName,
        // The provenance is the uploader: inbound channel media has none, and a staff device
        // always does. The row does not persist the source; the Cloudinary tags do.
        source = attachment.UploadedByUserId is null ? MediaSource.WhatsApp.Value : MediaSource.Web.Value,
        publicId = MediaStorageKey.PublicId(MediaStorageKey.AssetKey(attachment.StorageKey, attachment.Id)),
        reference = new { kind = MediaReferenceKinds.Attachment, id = attachment.Id },
    };

    private string? MintImageUrl(Guid organizationId, IReadOnlyList<MessageAttachment> attachments)
    {
        if (_mediaTokens is null)
        {
            return null;
        }

        var analysable = attachments.FirstOrDefault(a => VisionContentTypes.IsAnalysable(a.ContentType));
        if (analysable is null)
        {
            return null;
        }

        try
        {
            var minted = _mediaTokens.Mint(
                organizationId,
                MediaStorageKey.AssetKey(analysable.StorageKey, analysable.Id),
                MediaScope.VisionAnalyze);

            return minted.IsSuccess ? minted.Url : null;
        }
        catch (Exception ex)
        {
            // Best-effort, like every other step on this path: the identity still travels.
            _logger.LogWarning(
                ex, "Failed to mint an image URL for attachment {AttachmentId}.", analysable.Id);
            return null;
        }
    }
}
