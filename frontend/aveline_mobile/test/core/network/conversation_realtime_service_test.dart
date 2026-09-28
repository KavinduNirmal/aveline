import 'package:aveline_mobile/core/network/conversation_realtime_service.dart';
import 'package:aveline_mobile/core/notifications/realtime_connection.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:flutter_test/flutter_test.dart';

const String _org = '11111111-1111-4111-8111-111111111111';
const String _conversation = '22222222-2222-4222-8222-222222222222';

class FakeRealtimeConnection implements RealtimeConnection {
  final handlers = <String, void Function(List<Object?>?)>{};
  final invocations = <({String method, List<Object?> arguments})>[];
  bool started = false;
  bool stopped = false;
  String? url;
  Future<String?> Function()? accessTokenFactory;

  /// When set, `JoinSalon` fails with it.
  Object? joinError;

  void Function()? reconnectedHandler;
  void Function(Object?)? closedHandler;

  List<String> get invokedMethods =>
      [for (final invocation in invocations) invocation.method];

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) {
    handlers[method] = handler;
  }

  @override
  Future<void> invoke(String method, List<Object?> arguments) async {
    invocations.add((method: method, arguments: arguments));
    if (method == 'JoinSalon' && joinError != null) {
      throw joinError!;
    }
  }

  @override
  Future<void> start() async {
    started = true;
  }

  @override
  Future<void> stop() async {
    stopped = true;
  }

  @override
  void onReconnected(void Function() handler) {
    reconnectedHandler = handler;
  }

  @override
  void onClosed(void Function(Object? error) handler) {
    closedHandler = handler;
  }

  void emit(String method, List<Object?>? args) => handlers[method]?.call(args);

  /// Simulates the socket being rebuilt by `withAutomaticReconnect`.
  void emitReconnected() => reconnectedHandler?.call();
}

void main() {
  group('ConversationRealtimeService', () {
    test('connect builds a connection, joins the salon, and starts it', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection()
            ..url = url
            ..accessTokenFactory = accessTokenFactory;
          return created!;
        },
      );

      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
      );

      expect(created, isNotNull);
      expect(created!.url, 'https://api.example.com/hubs/conversations');
      expect(created!.started, isTrue);
      expect(created!.invokedMethods, contains('JoinSalon'));
    });

    test('JoinSalon is invoked with two scalar arguments', () async {
      // `invoke` takes the argument *list*, and the concrete connection spreads it into the
      // SignalR client's `args:`, so each element is one hub method argument.
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
      );

      final join = created!.invocations.singleWhere(
        (invocation) => invocation.method == 'JoinSalon',
      );
      expect(join.arguments, [_org, _conversation]);
    });

    test('refuses a non-UUID id before it builds a connection', () async {
      var built = false;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          built = true;
          return FakeRealtimeConnection();
        },
      );

      await expectLater(
        service.connect(
          baseUrl: 'https://api.example.com',
          getToken: () async => 'token',
          organizationId: _org,
          conversationId: 'conv-1',
          onAgentState: (_) {},
        ),
        throwsA(isA<ArgumentError>()),
      );
      expect(built, isFalse);
    });

    test('forwards ReceiveAgentState payloads to the callback', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      AgentStatePayload? received;
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (payload) => received = payload,
      );

      created!.emit('ReceiveAgentState', [
        {'conversationId': _conversation, 'state': 'searching', 'agentKey': 'aveline'},
      ]);

      expect(received, isNotNull);
      expect(received!.conversationId, _conversation);
      expect(received!.state, 'searching');
      expect(received!.agentKey, 'aveline');
    });

    test('a malformed agent-state payload is ignored rather than read as idle', () async {
      // A missing `state` is contract drift, not an idle agent: rendering it as idle would
      // silently stop the working strip.
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      final states = <AgentStatePayload>[];
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: states.add,
      );

      created!.emit('ReceiveAgentState', [
        {'conversationId': _conversation},
      ]);
      created!.emit('ReceiveAgentState', [
        {'conversationId': _conversation, 'state': ''},
      ]);
      created!.emit('ReceiveAgentState', ['not a map']);

      expect(states, isEmpty);
    });

    test('forwards ReceiveMessage as the raw payload the hub sent', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      final received = <Map<String, dynamic>>[];
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
        onMessage: received.add,
      );

      created!.emit('ReceiveMessage', [
        {
          'id': 'm1',
          'authorKind': 'Agent',
          'agentKey': 'aveline',
          'contentBlocks': [
            {'type': 'text', 'text': 'Here is the answer.'},
          ],
          'createdAt': '2026-09-09T10:00:00Z',
        },
      ]);

      expect(received, hasLength(1));
      expect(received.single['id'], 'm1');
    });

    test('ReceiveMessage is registered even without a callback, and a later listener gets it', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
      );

      expect(created!.handlers.containsKey('ReceiveMessage'), isTrue);

      final received = <Map<String, dynamic>>[];
      service.addMessageListener(received.add);
      created!.emit('ReceiveMessage', [
        {'id': 'm2'},
      ]);

      expect(received, hasLength(1));
      expect(received.single['id'], 'm2');
    });

    test('a reconnect re-joins the salon and tells the listener', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      var reconnects = 0;
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
        onReconnected: () => reconnects++,
      );

      created!.emitReconnected();

      expect(
        created!.invocations.where((i) => i.method == 'JoinSalon'),
        hasLength(2),
      );
      expect(reconnects, 1);
    });

    test('a join failure is surfaced rather than swallowed', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection()..joinError = StateError('no membership');
          return created!;
        },
      );

      Object? failure;
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
        onJoinFailed: (error) => failure = error,
      );

      expect(failure, isA<StateError>());
      // The connection still started; only the group membership failed.
      expect(created!.started, isTrue);
    });

    test('disconnect stops the active connection', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: _org,
        conversationId: _conversation,
        onAgentState: (_) {},
      );

      await service.disconnect();
      expect(created!.stopped, isTrue);
    });

    test('a JoinSalon-free connect forwards conversation tiles', () async {
      // The inbox has no single conversation to join: the hub adds the connection to the org
      // and user groups on connect, and the tile arrives on one of those.
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      final tiles = <Conversation>[];
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        onConversationChanged: tiles.add,
      );

      expect(created!.started, isTrue);
      expect(created!.invokedMethods, isNot(contains('JoinSalon')));

      created!.emit('ReceiveConversationChanged', [
        {
          'id': 'cnv_1',
          'kind': 'Salon',
          'customerId': 'cus_9',
          'customerName': 'Nadeesha Perera',
          'lastMessagePreview': 'A draft is ready.',
          'lastMessageBlock': 'suggestion',
          'lastMessageAuthor': 'Agent',
          'lastMessageAgentKey': 'ava',
          'markers': ['draft'],
        },
      ]);

      expect(tiles, hasLength(1));
      expect(tiles.single.id, 'cnv_1');
      expect(tiles.single.customerName, 'Nadeesha Perera');
      expect(tiles.single.markers, [ConversationMarker.draft]);
    });
  });
}
