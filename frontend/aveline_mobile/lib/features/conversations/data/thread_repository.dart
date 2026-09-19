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
}
