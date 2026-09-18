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

/// One client's thread: its history, and the two things the associate can do in
/// it - say something, and decide a draft someone else wrote.
abstract interface class ThreadRepository {
  /// One page of the thread's messages, oldest first.
  ///
  /// Pages are ascending, so page one is the *oldest*: the caller that wants the
  /// newest words has to work out the last page, which is why [ThreadPage.pageCount]
  /// exists.
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
  });

  /// Sends a staff message and returns it as the server stored it.
  Future<ThreadMessage> sendMessage(String conversationId, String text);

  /// Approves or dismisses a staged draft.
  ///
  /// Takes the whole [message] rather than its id because the API binds the
  /// decision to the hash of the content the approver was shown.
  Future<void> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  });
}
