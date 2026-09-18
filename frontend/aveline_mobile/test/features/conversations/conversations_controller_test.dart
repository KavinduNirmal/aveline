import 'package:aveline_mobile/features/conversations/data/conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/presentation/conversations_controller.dart';
import 'package:flutter_test/flutter_test.dart';

final DateTime _now = DateTime.utc(2026, 9, 18, 12);

class _FakeInbox implements ConversationRepository {
  _FakeInbox(this.items);

  List<Conversation> items;
  bool fail = false;
  int calls = 0;

  @override
  Future<List<Conversation>> fetchConversations() async {
    calls++;
    if (fail) {
      throw Exception('The inbox is unavailable.');
    }
    return items;
  }
}

Conversation _salon({
  Duration age = const Duration(minutes: 8),
  int unread = 0,
  ConversationStatus status = ConversationStatus.active,
}) => Conversation(
  id: 'cnv_salon',
  kind: ConversationKind.aveline,
  status: status,
  lastMessageAt: _now.subtract(age),
  lastMessagePreview: 'Shall I draft a note for Hasini?',
  lastMessageAuthor: ConversationAuthor.agent,
  unreadCount: unread,
);

Conversation _client(
  String id,
  String name, {
  required Duration age,
  int unread = 0,
  String preview = 'A word about the fitting.',
  ConversationAuthor author = ConversationAuthor.customer,
  ConversationStatus status = ConversationStatus.active,
}) => Conversation(
  id: id,
  kind: ConversationKind.customer,
  customerId: 'cus_$id',
  customerName: name,
  status: status,
  lastMessageAt: _now.subtract(age),
  lastMessagePreview: preview,
  lastMessageAuthor: author,
  unreadCount: unread,
);

Conversation _announcement(
  String id, {
  required Duration age,
  String preview = 'Your month at the boutique.',
}) => Conversation(
  id: id,
  kind: ConversationKind.system,
  lastMessageAt: _now.subtract(age),
  lastMessagePreview: preview,
  lastMessageAuthor: ConversationAuthor.system,
);

/// Nadeesha (newest), Chathurika, Kasun (oldest), plus the Salon and a digest,
/// deliberately handed over out of order so the controller has to order them.
List<Conversation> _inbox() => [
  _client('3', 'Kasun Bandara', age: const Duration(days: 1)),
  _announcement('digest', age: const Duration(days: 6)),
  _client('2', 'Chathurika Silva', age: const Duration(hours: 4)),
  _salon(),
  _client('1', 'Nadeesha Perera', age: const Duration(minutes: 4), unread: 2),
];

ConversationsController _controller(_FakeInbox inbox) =>
    ConversationsController(inbox);

void main() {
  group('ConversationsController.load', () {
    test('pins the Salon first, then the clients, newest word first', () async {
      final controller = _controller(_FakeInbox(_inbox()));

      await controller.load();

      expect(controller.items.map((item) => item.title), [
        'Aveline',
        'Nadeesha Perera',
        'Chathurika Silva',
        'Kasun Bandara',
        'Announcement',
      ]);
    });

    test('exposes the Salon on its own, so the row cannot be lost in the list', () async {
      final controller = _controller(_FakeInbox(_inbox()));

      await controller.load();

      expect(controller.aveline, isNotNull);
      expect(controller.aveline!.isAveline, isTrue);
      expect(controller.clients, hasLength(3));
      expect(controller.clients.every((item) => !item.isAveline), isTrue);
    });

    test('keeps the announcements at the foot of the inbox', () async {
      final controller = _controller(_FakeInbox(_inbox()));

      await controller.load();

      expect(controller.announcements, hasLength(1));
      expect(controller.items.last.kind, ConversationKind.system);
    });

    test('a thread nobody has spoken in yet sorts after the ones that have', () async {
      // A client thread opened from the client book has no messages, and pinning
      // it above a thread with a fresh reply would bury the reply.
      final silent = Conversation(
        id: 'cnv_silent',
        kind: ConversationKind.customer,
        customerId: 'cus_9',
        customerName: 'Amaya Fernando',
      );
      final controller = _controller(_FakeInbox([silent, ..._inbox()]));

      await controller.load();

      expect(controller.clients.last.title, 'Amaya Fernando');
      expect(controller.clients.last.lastMessageAt, isNull);
    });

    test('an inbox the Salon has not been created in yet still lists its clients', () async {
      final controller = _controller(
        _FakeInbox([_client('1', 'Nadeesha Perera', age: const Duration(minutes: 4))]),
      );

      await controller.load();

      expect(controller.aveline, isNull);
      expect(controller.clients, hasLength(1));
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.errorMessage, isNull);
    });

    test('sums the unread count across the whole inbox', () async {
      final controller = _controller(_FakeInbox(_inbox()));

      await controller.load();

      expect(controller.unreadTotal, 2);
    });

    test('an empty inbox is empty rather than broken', () async {
      final controller = _controller(_FakeInbox([]));

      await controller.load();

      expect(controller.items, isEmpty);
      expect(controller.isEmpty, isTrue);
      expect(controller.aveline, isNull);
      expect(controller.unreadTotal, 0);
    });

    test('a failed load surfaces its message and settles', () async {
      final inbox = _FakeInbox(_inbox())..fail = true;
      final controller = _controller(inbox);

      await controller.load();

      expect(controller.errorMessage, 'The inbox is unavailable.');
      expect(controller.items, isEmpty);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
    });

    test('reloading clears the error it was carrying', () async {
      final inbox = _FakeInbox(_inbox())..fail = true;
      final controller = _controller(inbox);
      await controller.load();

      inbox.fail = false;
      await controller.load();

      expect(controller.errorMessage, isNull);
      expect(controller.items, hasLength(5));
    });

    test('notifies its listeners while it loads', () async {
      final controller = _controller(_FakeInbox(_inbox()));
      var notifications = 0;
      controller.addListener(() => notifications++);

      await controller.load();

      expect(notifications, greaterThan(0));
    });
  });

  group('ConversationsController.refresh', () {
    test('keeps the inbox on screen until the newer one lands', () async {
      final inbox = _FakeInbox(_inbox());
      final controller = _controller(inbox);
      await controller.load();

      final pending = controller.refresh();

      expect(controller.items, hasLength(5));
      await pending;
      expect(controller.items, hasLength(5));
    });

    test('picks up a thread that arrived since the inbox was opened', () async {
      final inbox = _FakeInbox(_inbox());
      final controller = _controller(inbox);
      await controller.load();

      inbox.items = [
        _client('4', 'Menaka Rathnayake', age: const Duration(minutes: 1)),
        ...inbox.items,
      ];
      await controller.refresh();

      expect(controller.clients.first.title, 'Menaka Rathnayake');
    });

    test('loads rather than refreshing when nothing has been loaded yet', () async {
      final controller = _controller(_FakeInbox(_inbox()));

      await controller.refresh();

      expect(controller.items, hasLength(5));
      expect(controller.hasLoadedOnce, isTrue);
    });
  });

  group('ConversationsController.search', () {
    test('narrows to the clients whose name matches', () async {
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();

      controller.search('nadeesha');

      expect(controller.clients.map((item) => item.title), ['Nadeesha Perera']);
      expect(controller.isSearching, isTrue);
    });

    test('matches the last word as well as the name', () async {
      final controller = _controller(
        _FakeInbox([
          _client('1', 'Nadeesha Perera', age: const Duration(minutes: 4), preview: 'The wine saree'),
          _client('2', 'Kasun Bandara', age: const Duration(hours: 2), preview: 'A second colour'),
        ]),
      );
      await controller.load();

      controller.search('wine saree');

      expect(controller.clients.map((item) => item.title), ['Nadeesha Perera']);
    });

    test('keeps the Salon pinned while the inbox is narrowed', () async {
      // The concierge is a fixed destination rather than one of the results, so
      // its position never moves - including while a search is in force.
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();

      controller.search('nadeesha');

      expect(controller.aveline, isNotNull);
      expect(controller.items.first.isAveline, isTrue);
    });

    test('ignores case and the space around the query', () async {
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();

      controller.search('  KASUN  ');

      expect(controller.clients.map((item) => item.title), ['Kasun Bandara']);
    });

    test('says so when nothing matches', () async {
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();

      controller.search('nobody at all');

      expect(controller.clients, isEmpty);
      expect(controller.announcements, isEmpty);
      expect(controller.hasMatches, isFalse);
      expect(controller.isEmpty, isFalse);
    });

    test('an empty query is not a search', () async {
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();
      controller.search('nadeesha');

      controller.search('   ');

      expect(controller.isSearching, isFalse);
      expect(controller.clients, hasLength(3));
    });

    test('clearing the search brings every thread back', () async {
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();
      controller.search('nadeesha');

      controller.search('');

      expect(controller.items, hasLength(5));
      expect(controller.query, isEmpty);
    });

    test('narrowing does not change the unread count the header wears', () async {
      // The badge is about the inbox, not about what is on screen.
      final controller = _controller(_FakeInbox(_inbox()));
      await controller.load();

      controller.search('kasun');

      expect(controller.unreadTotal, 2);
    });
  });
}
