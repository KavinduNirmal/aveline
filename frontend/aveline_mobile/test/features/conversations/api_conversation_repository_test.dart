import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/conversations/data/api_conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/data/conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Serves one canned response and records every request that reached it.
class _RecordingAdapter implements HttpClientAdapter {
  _RecordingAdapter(this.body, {this.statusCode = 200});

  final String body;
  final int statusCode;
  final List<RequestOptions> requests = [];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    return ResponseBody.fromString(
      body,
      statusCode,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

({ApiConversationRepository repository, _RecordingAdapter adapter}) _api(
  String body, {
  int statusCode = 200,
  String? organizationId = 'org_7',
}) {
  final adapter = _RecordingAdapter(body, statusCode: statusCode);
  final dio = Dio()..httpClientAdapter = adapter;
  return (
    repository: ApiConversationRepository(
      dio,
      organizationId: () => organizationId,
    ),
    adapter: adapter,
  );
}

void main() {
  group('ApiConversationRepository.fetchConversations', () {
    test('asks the org for its conversations', () async {
      final api = _api(jsonEncode({'total': 0, 'page': 1, 'pageSize': 50}));

      await api.repository.fetchConversations();

      expect(api.adapter.requests, hasLength(1));
      expect(api.adapter.requests.single.method, 'GET');
      expect(
        api.adapter.requests.single.path,
        '/api/v1/orgs/org_7/conversations',
      );
      expect(api.adapter.requests.single.queryParameters, {
        'page': 1,
        'pageSize': 50,
      });
    });

    test('waits rather than failing when the org is not known yet', () async {
      // The org id arrives from GET /orgs/my after the shell mounts. Until it
      // does there is nothing to call, but that is a "not yet" - not the error
      // state, which is reserved for the server's 403.
      final api = _api('{}', organizationId: null);

      await expectLater(
        api.repository.fetchConversations(),
        throwsA(isA<OrgContextUnavailable>()),
      );
      expect(api.adapter.requests, isEmpty);
    });

    test('asks for the page it needs, and reads the envelope back', () async {
      final api = _api(
        jsonEncode({'total': 137, 'page': 2, 'pageSize': 50, 'items': []}),
      );

      final page = await api.repository.fetchConversations(page: 2);

      expect(api.adapter.requests.single.queryParameters, {
        'page': 2,
        'pageSize': 50,
      });
      expect(page.page, 2);
      expect(page.pageSize, 50);
      expect(page.total, 137);
      expect(page.pageCount, 3);
    });

    test('parses a page of conversations', () async {
      final api = _api(
        jsonEncode({
          'total': 2,
          'page': 1,
          'pageSize': 50,
          'items': [
            {
              'id': 'cnv_salon',
              'kind': 'Salon',
              'customerId': null,
              'threadId': 'thread_1',
              'status': 'Active',
              'lastMessageAt': '2026-09-18T11:52:00Z',
            },
            {
              'id': 'cnv_2',
              'kind': 'Direct',
              'customerId': 'cus_9',
              'threadId': 'thread_2',
              'status': 'AwaitingSignOff',
              'lastMessageAt': '2026-09-18T10:00:00Z',
            },
          ],
        }),
      );

      final page = await api.repository.fetchConversations();

      expect(page.total, 2);
      expect(page.items, hasLength(2));
      expect(page.items.first.kind, ConversationKind.aveline);
      expect(page.items.last.kind, ConversationKind.customer);
      expect(page.items.last.status, ConversationStatus.awaitingSignOff);
    });

    test('reads the row fields the list endpoint carries', () async {
      final api = _api(
        jsonEncode({
          'total': 1,
          'page': 1,
          'pageSize': 50,
          'items': [
            {
              'id': 'cnv_inbound',
              'kind': 'Salon',
              'customerId': null,
              'customerName': null,
              'externalRef': '94771234567',
              'threadId': 'thread_9',
              'status': 'Active',
              'lastMessageAt': '2026-09-18T11:48:00Z',
              'lastMessagePreview':
                  'Can the wine silk saree be taken in before Friday evening?',
              'lastMessageKind': 'ClientMessage',
              'lastMessageBlock': 'client_message',
              'lastMessageAuthor': 'Customer',
              'lastMessageAgentKey': null,
              'markers': <String>[],
            },
            {
              'id': 'cnv_draft',
              'kind': 'Salon',
              'customerId': 'cus_204',
              'customerName': 'Nadeesha Perera',
              'threadId': 'thread_204',
              'status': 'Active',
              'lastMessagePreview': 'Hi Nadeesha - the wine silk can be taken in.',
              'lastMessageKind': 'Note',
              'lastMessageBlock': 'suggestion',
              'lastMessageAuthor': 'Agent',
              'lastMessageAgentKey': 'ava',
              'markers': ['draft'],
            },
          ],
        }),
      );

      final conversations = (await api.repository.fetchConversations()).items;

      final inbound = conversations.first;
      expect(inbound.customerName, isNull);
      expect(inbound.externalRef, '94771234567');
      expect(inbound.lastMessageBlock, 'client_message');
      expect(inbound.kind, ConversationKind.customer);
      expect(inbound.markers, isEmpty);

      final draft = conversations.last;
      expect(draft.customerName, 'Nadeesha Perera');
      expect(draft.lastMessageBlock, 'suggestion');
      expect(draft.lastMessageAgentKey, 'ava');
      expect(draft.lastMessageAuthor, ConversationAuthor.agent);
      expect(draft.markers, [ConversationMarker.draft]);
    });

    test('reads an empty page without inventing threads', () async {
      final api = _api(jsonEncode({'total': 0, 'page': 1, 'pageSize': 50}));

      final page = await api.repository.fetchConversations();

      expect(page.items, isEmpty);
      expect(page.total, 0);
    });

    test('propagates a refusal so the caller can show its error state', () async {
      final api = _api('{"message":"nope"}', statusCode: 500);

      expect(
        api.repository.fetchConversations(),
        throwsA(isA<DioException>()),
      );
    });
  });

  group('ApiConversationRepository.fetchConversation', () {
    test('reads one thread by id', () async {
      final api = _api(
        jsonEncode({
          'id': 'cnv_1',
          'kind': 'Salon',
          'customerId': 'cus_204',
          'customerName': 'Nadeesha Perera',
          'threadId': 'thread_204',
          'status': 'Active',
        }),
      );

      final conversation = await api.repository.fetchConversation('cnv_1');

      expect(
        api.adapter.requests.single.path,
        '/api/v1/orgs/org_7/conversations/cnv_1',
      );
      expect(conversation, isNotNull);
      expect(conversation!.customerName, 'Nadeesha Perera');
      expect(conversation.title, 'Nadeesha Perera');
    });

    test('a thread the caller may not see is null, not a failure', () async {
      // A notification about a thread that has gone, or one this caller cannot see, is the
      // screen's own not-found state rather than an error card about the network.
      final forbidden = _api('{"message":"nope"}', statusCode: 403);
      final missing = _api('{"message":"gone"}', statusCode: 404);

      expect(await forbidden.repository.fetchConversation('cnv_1'), isNull);
      expect(await missing.repository.fetchConversation('cnv_1'), isNull);
    });

    test('a server failure still propagates', () async {
      final api = _api('{"message":"boom"}', statusCode: 500);

      expect(
        api.repository.fetchConversation('cnv_1'),
        throwsA(isA<DioException>()),
      );
    });

    test('a missing organization id is a "not yet"', () async {
      final api = _api('{}', organizationId: null);

      expect(
        api.repository.fetchConversation('cnv_1'),
        throwsA(isA<OrgContextUnavailable>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });
}
