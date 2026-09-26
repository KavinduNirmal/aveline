import 'dart:typed_data';

import '../domain/thread_attachment.dart';
import '../domain/thread_message.dart';

/// One page of a thread's messages.
///
/// Ordered oldest first within the page, which is how the API serves history: page
/// one holds the *oldest* messages, so a client that wants the end of a long
/// conversation has to ask for the last page rather than the first.
class ThreadPage {
  const ThreadPage({
    required this.items,
    required this.total,
    required this.page,
    required this.pageSize,
  });

  static const ThreadPage empty = ThreadPage(
    items: [],
    total: 0,
    page: 1,
    pageSize: 0,
  );

  final List<ThreadMessage> items;
  final int total;
  final int page;
  final int pageSize;

  /// How many pages the whole thread is served in, at least one.
  int get pageCount =>
      pageSize <= 0 ? 1 : ((total + pageSize - 1) ~/ pageSize).clamp(1, 1 << 30);

  factory ThreadPage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return ThreadPage(
      items: rawItems is List
          ? [
              for (final item in rawItems)
                if (item is Map)
                  ThreadMessage.fromJson(Map<String, dynamic>.from(item)),
            ]
          : const [],
      total: (json['total'] as num?)?.toInt() ?? 0,
      page: (json['page'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? 0,
    );
  }
}

/// What a delivery to a customer's channel reports back.
///
/// A delivery is not a send: the words leave the app and are handed to the channel the
/// client actually reads (today WhatsApp), so the server answers with where they went
/// rather than only with the row it stored. [message] is that row, when the server
/// returned one, so the thread can draw the real thing instead of guessing.
class ThreadDelivery {
  const ThreadDelivery({
    required this.delivered,
    this.channel,
    this.providerMessageId,
    this.message,
  });

  /// Whether the channel accepted the message.
  final bool delivered;

  /// The channel it went over, as the server names it (`WhatsApp`).
  final String? channel;

  /// The channel provider's own id for the message, when it returned one.
  final String? providerMessageId;

  /// The stored message the delivery produced, when the server returned it.
  final ThreadMessage? message;

  factory ThreadDelivery.fromJson(Map<String, dynamic> json) {
    final rawMessage = json['message'];
    return ThreadDelivery(
      delivered: json['delivered'] == true,
      channel: json['channel'] as String?,
      providerMessageId: json['providerMessageId'] as String?,
      message: rawMessage is Map
          ? ThreadMessage.fromJson(Map<String, dynamic>.from(rawMessage))
          : null,
    );
  }
}

/// The server's refusal to deliver, with the reason code it named.
///
/// The codes are the API's own (`no_customer`, `no_channel_handle`,
/// `channel_not_connected`, `channel_unsupported`, `provider_refused`), carried
/// un-translated so the layer that owns the copy can turn each into a sentence. A
/// refusal is a 409 or a 502 rather than a transport failure: the request was
/// understood and declined, and nothing was sent.
class DeliveryRefused implements Exception {
  const DeliveryRefused(this.refusal, {this.detail});

  /// The machine-readable reason the server named.
  final String refusal;

  /// The server's own sentence, when it sent one.
  final String? detail;

  @override
  String toString() => 'DeliveryRefused($refusal)';
}

/// One client's thread: its history, and what the associate can do in it.
abstract interface class ThreadRepository {
  /// One page of the thread's messages, oldest first.
  ///
  /// Pages are ascending, so page one is the *oldest*: the caller that wants the
  /// newest words has to work out the last page, which is why [ThreadPage.pageCount]
  /// exists.
  /// [around] asks for the page that **holds** that message instead of [page], which is how a
  /// notification deep-link opens on the words it was about. The response echoes the page it
  /// served, so `hasEarlier` and `hasMore` still follow from it.
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  });

  /// Sends a staff message and returns it as the server stored it.
  ///
  /// [clientMessageId] is the device's idempotency key for one composed message.
  /// A retry reuses the same key, so a send that timed out after the row was
  /// written comes back as the stored message instead of a duplicate.
  ///
  /// [attachmentIds] are the uploads this message binds. They must belong to this
  /// conversation and be unbound; a bad id fails the whole send.
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  });

  /// Uploads one file against a conversation, unbound until a send names it.
  Future<ThreadAttachment> uploadAttachment(
    String conversationId, {
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  });

  /// The stored bytes behind an attachment, fetched through the authenticated client.
  Future<Uint8List> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  );

  /// Approves or dismisses a staged draft, returning the message as the server
  /// stored it so the thread draws the real status rather than guessing.
  ///
  /// Takes the whole [message] rather than its id because the API binds the
  /// decision to the hash of the content the approver was shown.
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  });

  /// Resolves an unidentified thread by binding it to the customer a `choice`
  /// block's option named. The thread's name follows on the next read.
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  });

  /// Takes an approved SignOff back, returning it to the associate's queue.
  ///
  /// Gated by `approvals:approve` on both sides: the action is only drawn for a caller who
  /// holds it, and the API refuses anyone who does not.
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  });

  /// Advances the caller's own read marker for this thread.
  ///
  /// Best-effort by contract: reading is not an action the associate took, so a failure is
  /// not a toast. [lastReadMessageId] is always a server id, never a `local_*` one.
  Future<void> markRead(String conversationId, String lastReadMessageId);

  /// Hands [text] to the customer's channel for this conversation.
  ///
  /// Distinct from [sendMessage], which writes into the shop's own record: this is the
  /// action bar's "send to customer", and the words reach the client over the channel
  /// they actually read. The server refuses with a [DeliveryRefused] naming why — no
  /// client, no handle, a channel that is not connected, or a provider that said no —
  /// and a refusal means **nothing was sent**.
  ///
  /// [clientMessageId] is the device's idempotency key for one delivery, the same
  /// contract [sendMessage] uses.
  Future<ThreadDelivery> deliver(
    String conversationId,
    String text, {
    String? clientMessageId,
  });

  /// Asks the agent to produce a fresh reply for the turn behind [messageId].
  ///
  /// Accepted rather than answered: the API returns `202` with no body, and the new
  /// reply arrives later over the existing hub stream exactly like any other agent
  /// message. Nothing is replaced locally.
  Future<void> regenerate(String conversationId, String messageId);
}
