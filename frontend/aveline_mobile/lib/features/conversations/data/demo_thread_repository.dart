import '../domain/thread_message.dart';
import 'thread_repository.dart';

/// A boutique's client threads, held in memory.
///
/// Stands in for the messages endpoints while the messaging slice is being built.
/// The exchanges are the ones the demo inbox's previews promise, so opening a
/// thread from the inbox lands on the conversation that was advertised rather than
/// on an empty room: a client asking about a saree, an associate answering, a note
/// the client never saw, and a reply an agent drafted that is waiting on a
/// signature.
///
/// Ages are measured from an injectable [clock], so "40 minutes ago" means the same
/// thing in a test as it does on screen. Each instance keeps its own copy, so one
/// test cannot send into another test's thread.
class DemoThreadRepository implements ThreadRepository {
  DemoThreadRepository({
    DateTime Function()? clock,
    this.latency = const Duration(milliseconds: 300),
  }) : _clock = clock ?? DateTime.now;

  /// Where "now" comes from. Injectable so a test can pin the thread's ages.
  final DateTime Function() _clock;

  /// How long each call takes.
  ///
  /// The seed is local, so without a delay the thread would arrive instantly and
  /// the loading state would never be seen. Tests pass `Duration.zero`.
  final Duration latency;

  /// The threads, built once per instance and then mutated by sending.
  final Map<String, List<ThreadMessage>> _threads = {};

  /// How many messages have been sent through this instance, so the stored ids
  /// are stable within a test.
  int _sentCount = 0;

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
  }) async {
    await _settle();
    final thread = _threadOf(conversationId);
    return ThreadPage(
      items: List.unmodifiable(_slice(thread, page: page, pageSize: pageSize)),
      total: thread.length,
      page: page,
      pageSize: pageSize,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(String conversationId, String text) async {
    await _settle();
    final stored = ThreadMessage(
      id: 'msg_sent_${++_sentCount}',
      author: MessageAuthor.staff,
      kind: MessageKind.note,
      status: MessageStatus.sent,
      text: text,
      createdAt: _clock().toUtc(),
    );
    _threadOf(conversationId).add(stored);
    return stored;
  }

  @override
  Future<void> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async {
    await _settle();
    final thread = _threadOf(conversationId);
    final index = thread.indexWhere((item) => item.id == message.id);
    if (index < 0) {
      return;
    }
    thread[index] = thread[index].copyWith(
      // Approving releases the reply to the client; dismissing keeps it in the
      // thread as a decision that was made, rather than pretending it never was.
      status: approved ? MessageStatus.sent : MessageStatus.cancelled,
    );
  }

  Future<void> _settle() async {
    if (latency > Duration.zero) {
      await Future<void>.delayed(latency);
    }
  }

  List<ThreadMessage> _threadOf(String conversationId) =>
      _threads.putIfAbsent(conversationId, () => _seedFor(conversationId));

  List<ThreadMessage> _slice(
    List<ThreadMessage> thread, {
    required int page,
    required int pageSize,
  }) {
    if (pageSize <= 0 || page < 1) {
      return const [];
    }
    final start = (page - 1) * pageSize;
    if (start >= thread.length) {
      return const [];
    }
    return thread.sublist(start, (start + pageSize).clamp(0, thread.length));
  }

  /// One message, aged from the clock.
  ThreadMessage _seed({
    required String id,
    required MessageAuthor author,
    required String text,
    required Duration age,
    MessageKind? kind,
    MessageStatus? status,
    String? contentHash,
  }) => ThreadMessage(
    id: id,
    author: author,
    kind:
        kind ??
        (author == MessageAuthor.client
            ? MessageKind.clientMessage
            : MessageKind.note),
    text: text,
    createdAt: _clock().toUtc().subtract(age),
    status: status ?? MessageStatus.published,
    contentHash: contentHash,
  );

  /// The thread one conversation opens onto.
  List<ThreadMessage> _seedFor(String conversationId) => switch (conversationId) {
    'cnv_nadeesha' => [
      _seed(
        id: 'msg_nd_1',
        author: MessageAuthor.client,
        text: 'Hi! Is the wine silk saree still there in a medium?',
        age: const Duration(minutes: 55),
      ),
      _seed(
        id: 'msg_nd_2',
        author: MessageAuthor.staff,
        text: 'It is. I have put one aside for you.',
        status: MessageStatus.read,
        age: const Duration(minutes: 50),
      ),
      _seed(
        id: 'msg_nd_3',
        author: MessageAuthor.client,
        text: 'Wonderful. Could it be taken in before Friday evening?',
        age: const Duration(minutes: 30),
      ),
      _seed(
        id: 'msg_nd_4',
        author: MessageAuthor.staff,
        text:
            'She needs it by Friday. The tailor is in tomorrow, so it can be '
            'done in one visit.',
        status: MessageStatus.published,
        age: const Duration(minutes: 20),
      ),
      _seed(
        id: 'msg_nd_5',
        author: MessageAuthor.staff,
        text:
            'Yes, easily. Bring it in tomorrow and we will have it ready for '
            'Thursday.',
        status: MessageStatus.delivered,
        age: const Duration(minutes: 12),
      ),
    ],
    'cnv_chathurika' => [
      _seed(
        id: 'msg_ch_1',
        author: MessageAuthor.staff,
        text: 'Good news - the blouse is pinned and ready for tomorrow.',
        status: MessageStatus.read,
        age: const Duration(hours: 26),
      ),
      _seed(
        id: 'msg_ch_2',
        author: MessageAuthor.client,
        text: 'Perfect. See you at 10:30.',
        age: const Duration(minutes: 41),
      ),
    ],
    'cnv_menaka' => [
      _seed(
        id: 'msg_mn_1',
        author: MessageAuthor.client,
        text: 'Thank you, but 12% is more than I had hoped for.',
        age: const Duration(hours: 3),
      ),
      _seed(
        id: 'msg_mn_2',
        author: MessageAuthor.staff,
        text: 'Let me see what I can do for the wedding party.',
        status: MessageStatus.read,
        age: const Duration(hours: 2, minutes: 50),
      ),
      _seed(
        id: 'msg_mn_3',
        author: MessageAuthor.agent,
        text:
            'I can hold the 12% as a goodwill gesture on order #4821 and confirm '
            'it this afternoon.',
        status: MessageStatus.awaitingSignOff,
        contentHash: 'hash_menaka_goodwill',
        age: const Duration(hours: 2),
      ),
    ],
    'cnv_kasun' => [
      _seed(
        id: 'msg_ks_1',
        author: MessageAuthor.client,
        text: 'Does this one come in a second colour?',
        age: const Duration(hours: 3),
      ),
    ],
    'cnv_hasini' => [
      _seed(
        id: 'msg_hs_1',
        author: MessageAuthor.staff,
        text: 'I have put the evening wear aside for your next visit.',
        status: MessageStatus.delivered,
        age: const Duration(days: 1, hours: 2),
      ),
    ],
    'cnv_amaya' => [
      _seed(
        id: 'msg_am_1',
        author: MessageAuthor.client,
        text: 'Thank you, the alterations are perfect.',
        age: const Duration(days: 5),
      ),
    ],
    // A conversation this build has nothing for opens as an empty thread, which is
    // what a thread opened before anyone has spoken in looks like.
    _ => <ThreadMessage>[],
  };
}
