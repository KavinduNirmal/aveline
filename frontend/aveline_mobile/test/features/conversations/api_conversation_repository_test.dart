import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/conversations/data/api_conversation_repository.dart';
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
}) {
  final adapter = _RecordingAdapter(body, statusCode: statusCode);
  final dio = Dio()..httpClientAdapter = adapter;
  return (
    repository: ApiConversationRepository(dio, organizationId: 'org_7'),
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

      final conversations = await api.repository.fetchConversations();

      expect(conversations, hasLength(2));
      expect(conversations.first.kind, ConversationKind.aveline);
      expect(conversations.last.kind, ConversationKind.customer);
      expect(conversations.last.status, ConversationStatus.awaitingSignOff);
    });

    test('reads an empty page without inventing threads', () async {
      final api = _api(jsonEncode({'total': 0, 'page': 1, 'pageSize': 50}));

      expect(await api.repository.fetchConversations(), isEmpty);
    });

    test('propagates a refusal so the caller can show its error state', () async {
      final api = _api('{"message":"nope"}', statusCode: 500);

      expect(
        api.repository.fetchConversations(),
        throwsA(isA<DioException>()),
      );
    });
  });
}
