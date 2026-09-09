import 'package:aveline_mobile/core/network/conversation_realtime_service.dart';
import 'package:aveline_mobile/core/notifications/realtime_connection.dart';
import 'package:aveline_mobile/features/salon/domain/salon_message.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeRealtimeConnection implements RealtimeConnection {
  final handlers = <String, void Function(List<Object?>?)>{};
  final invokedMethods = <String>[];
  bool started = false;
  bool stopped = false;
  String? url;
  Future<String?> Function()? accessTokenFactory;

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) {
    handlers[method] = handler;
  }

  @override
  Future<void> invoke(String method, List<Object?> arguments) async {
    invokedMethods.add(method);
  }

  @override
  Future<void> start() async {
    started = true;
  }

  @override
  Future<void> stop() async {
    stopped = true;
  }

  void emit(String method, List<Object?>? args) => handlers[method]?.call(args);
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
        organizationId: 'org-1',
        conversationId: 'conv-1',
        onAgentState: (_) {},
      );

      expect(created, isNotNull);
      expect(created!.url, 'https://api.example.com/hubs/conversations');
      expect(created!.started, isTrue);
      expect(created!.invokedMethods, contains('JoinSalon'));
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
        organizationId: 'org-1',
        conversationId: 'conv-1',
        onAgentState: (payload) => received = payload,
      );

      created!.emit('ReceiveAgentState', [
        {'conversationId': 'conv-1', 'state': 'searching', 'agentKey': 'aveline'},
      ]);

      expect(received, isNotNull);
      expect(received!.conversationId, 'conv-1');
      expect(received!.state, 'searching');
      expect(received!.agentKey, 'aveline');
    });

    test('forwards ReceiveMessage payloads to the callback', () async {
      FakeRealtimeConnection? created;
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection();
          return created!;
        },
      );

      SalonMessage? received;
      await service.connect(
        baseUrl: 'https://api.example.com',
        getToken: () async => 'token',
        organizationId: 'org-1',
        conversationId: 'conv-1',
        onAgentState: (_) {},
        onMessage: (message) => received = message,
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

      expect(received, isNotNull);
      expect(received!.id, 'm1');
      expect(received!.text, 'Here is the answer.');
      expect(received!.isOwn, isFalse);
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
        organizationId: 'org-1',
        conversationId: 'conv-1',
        onAgentState: (_) {},
      );

      await service.disconnect();
      expect(created!.stopped, isTrue);
    });
  });
}
