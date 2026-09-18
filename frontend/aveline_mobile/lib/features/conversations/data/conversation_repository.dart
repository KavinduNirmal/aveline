import '../../../../core/network/org_context.dart';
import '../domain/conversation.dart';

// Re-exported so the inbox's callers see the one "not yet" exception without
// importing core's network layer directly.
export '../../../../core/network/org_context.dart' show OrgContextUnavailable;

/// One page of the inbox.
///
/// Shaped exactly like [ThreadPage], with one semantic difference worth stating:
/// a **thread** page one holds the *oldest* messages, while an **inbox** page one
/// holds the *newest* threads, because the column is read from the top.
class ConversationPage {
  const ConversationPage({
    required this.items,
    required this.total,
    required this.page,
    required this.pageSize,
  });

  static const ConversationPage empty = ConversationPage(
    items: [],
    total: 0,
    page: 1,
    pageSize: 0,
  );

  /// The threads on this page, newest first.
  final List<Conversation> items;

  /// How many threads the whole inbox holds, not only this page.
  final int total;

  final int page;
  final int pageSize;

  /// How many pages the whole inbox is served in, at least one.
  int get pageCount =>
      pageSize <= 0 ? 1 : ((total + pageSize - 1) ~/ pageSize).clamp(1, 1 << 30);

  factory ConversationPage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return ConversationPage(
      items: rawItems is List
          ? [
              for (final item in rawItems)
                if (item is Map)
                  Conversation.fromJson(Map<String, dynamic>.from(item)),
            ]
          : const [],
      total: (json['total'] as num?)?.toInt() ?? 0,
      page: (json['page'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? 0,
    );
  }
}

/// The boutique's message inbox: the Salon, and every client thread.
///
/// Deliberately unordered. Which thread belongs at the top is a decision the
/// inbox makes once, in its controller, rather than a rule every source has to
/// remember to apply.
abstract interface class ConversationRepository {
  /// One page of conversations, newest first.
  ///
  /// Page one holds the newest threads; the envelope's [ConversationPage.total]
  /// is what tells the caller whether another page exists.
  ///
  /// Throws [OrgContextUnavailable] when the organization id is not known yet -
  /// a "not yet" the screen keeps as a loading state, never the error state.
  Future<ConversationPage> fetchConversations({int page = 1});
}
