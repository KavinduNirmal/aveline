import '../domain/conversation.dart';
import 'conversation_repository.dart';

/// A boutique's message inbox, held in memory.
///
/// Stands in as a *fixture*: it fills the fields the real endpoint carried before
/// the row contract landed - the client's name and the last message - so tests
/// can drive the inbox against a realistic column rather than against empty rows.
/// It is deliberately not reachable from a production path (D5): the screen and
/// the app fall back to [EmptyConversationRepository] instead.
///
/// Ages are measured from an injectable [clock] rather than from the wall clock,
/// so "40 minutes ago" means the same thing in a test as it does on screen. The
/// list is rebuilt per call rather than cached: nothing here mutates, so there is
/// no reason for two callers to share one.
class DemoConversationRepository implements ConversationRepository {
  DemoConversationRepository({
    DateTime Function()? clock,
    this.latency = const Duration(milliseconds: 300),
  }) : _clock = clock ?? DateTime.now;

  /// Where "now" comes from. Injectable so a test can pin the inbox's ages.
  final DateTime Function() _clock;

  /// How long the read takes.
  ///
  /// The seed is local, so without a delay the inbox would arrive instantly and
  /// the loading state would never be seen. Tests pass `Duration.zero`.
  final Duration latency;

  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async {
    if (latency > Duration.zero) {
      await Future<void>.delayed(latency);
    }
    // Unmodifiable: the inbox is a read, and a caller that could reorder this
    // list in place would be reordering the source of truth.
    final inbox = _buildInbox();
    return ConversationPage(
      items: List.unmodifiable(inbox),
      total: inbox.length,
      page: page,
      pageSize: inbox.length,
    );
  }

  @override
  Future<Conversation?> fetchConversation(String id) async {
    if (latency > Duration.zero) {
      await Future<void>.delayed(latency);
    }
    for (final conversation in _buildInbox()) {
      if (conversation.id == id) {
        return conversation;
      }
    }
    return null;
  }

  /// Where a thread stood [age] ago.
  DateTime _at(Duration age) => _clock().toUtc().subtract(age);

  List<Conversation> _buildInbox() => [
    // The Salon. Always present, because the concierge is the one thread the
    // boutique always has.
    Conversation(
      id: 'cnv_salon',
      kind: ConversationKind.aveline,
      threadId: 'thread_salon',
      lastMessageAt: _at(const Duration(minutes: 8)),
      lastMessagePreview:
          'Hasini de Silva has not been in for 96 days. Shall I draft a note '
          'for her?',
      lastMessageAuthor: ConversationAuthor.agent,
    ),
    Conversation(
      id: 'cnv_nadeesha',
      kind: ConversationKind.customer,
      customerId: 'cus_204',
      customerName: 'Nadeesha Perera',
      threadId: 'thread_204',
      lastMessageAt: _at(const Duration(minutes: 4)),
      lastMessagePreview:
          'Can the wine silk saree be taken in before Friday evening?',
      lastMessageAuthor: ConversationAuthor.customer,
    ),
    Conversation(
      id: 'cnv_chathurika',
      kind: ConversationKind.customer,
      customerId: 'cus_118',
      customerName: 'Chathurika Silva',
      threadId: 'thread_118',
      lastMessageAt: _at(const Duration(minutes: 40)),
      lastMessagePreview:
          'The blouse is pinned and ready for tomorrow\u2019s 10:30 fitting.',
      lastMessageAuthor: ConversationAuthor.staff,
    ),
    Conversation(
      id: 'cnv_menaka',
      kind: ConversationKind.customer,
      customerId: 'cus_311',
      customerName: 'Menaka Rathnayake',
      status: ConversationStatus.awaitingSignOff,
      threadId: 'thread_311',
      lastMessageAt: _at(const Duration(hours: 2)),
      lastMessagePreview:
          'The 12% goodwill discount on order #4821 needs a signature before I '
          'can release it.',
      lastMessageAuthor: ConversationAuthor.agent,
    ),
    Conversation(
      id: 'cnv_kasun',
      kind: ConversationKind.customer,
      customerId: 'cus_233',
      customerName: 'Kasun Bandara',
      threadId: 'thread_233',
      lastMessageAt: _at(const Duration(hours: 3)),
      lastMessagePreview: 'Does this one come in a second colour?',
      lastMessageAuthor: ConversationAuthor.customer,
    ),
    Conversation(
      id: 'cnv_hasini',
      kind: ConversationKind.customer,
      customerId: 'cus_091',
      customerName: 'Hasini de Silva',
      threadId: 'thread_091',
      lastMessageAt: _at(const Duration(days: 1, hours: 2)),
      lastMessagePreview:
          'I have put the evening wear aside for your next visit.',
      lastMessageAuthor: ConversationAuthor.staff,
    ),
    Conversation(
      id: 'cnv_amaya',
      kind: ConversationKind.customer,
      customerId: 'cus_045',
      customerName: 'Amaya Fernando',
      threadId: 'thread_045',
      lastMessageAt: _at(const Duration(days: 5)),
      lastMessagePreview: 'Thank you, the alterations are perfect.',
      lastMessageAuthor: ConversationAuthor.customer,
    ),
    Conversation(
      id: 'cnv_digest',
      kind: ConversationKind.system,
      threadId: 'thread_digest',
      lastMessageAt: _at(const Duration(days: 6)),
      lastMessagePreview:
          'Your month at the boutique: 42 clients served, 6 to win back.',
      lastMessageAuthor: ConversationAuthor.system,
    ),
  ];
}
