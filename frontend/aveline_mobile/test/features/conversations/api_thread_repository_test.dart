import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/conversations/data/api_thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
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

({ApiThreadRepository repository, _RecordingAdapter adapter}) _api(
  String body, {
  int statusCode = 200,
}) {
  final adapter = _RecordingAdapter(body, statusCode: statusCode);
  final dio = Dio()..httpClientAdapter = adapter;
  return (
    repository: ApiThreadRepository(dio, organizationId: 'org_7'),
    adapter: adapter,
  );
}

String _messageBody({
  String id = 'msg_1',
  String text = 'On my way.',
  String kind = 'Note',
  String authorKind = 'User',
  String status = 'Sent',
  String? contentHash,
}) => jsonEncode({
  'id': id,
  'conversationId': 'cnv_1',
  'authorKind': authorKind,
  'agentKey': null,
  'authorUserId': null,
  'kind': kind,
  'contentBlocks': [
    {'type': 'text', 'text': text},
  ],
  'contentHash': contentHash,
  'replyToMessageId': null,
  'status': status,
  'createdAt': '2026-09-18T09:14:00Z',
});

void main() {
  group('ApiThreadRepository.fetchMessages', () {
    test('asks the conversation for one page of its messages', () async {
      final api = _api(
        jsonEncode({
          'total': 0,
          'page': 1,
          'pageSize': 50,
          'items': <Object>[],
        }),
      );

      await api.repository.fetchMessages('cnv_1');

      expect(api.adapter.requests.single.method, 'GET');
      expect(
        api.adapter.requests.single.path,
        '/api/v1/orgs/org_7/conversations/cnv_1/messages',
      );
      expect(api.adapter.requests.single.queryParameters, {
        'page': 1,
        'pageSize': 50,
      });
    });

    test('carries the page it was asked for', () async {
      final api = _api(
        jsonEncode({'total': 0, 'page': 3, 'pageSize': 20, 'items': <Object>[]}),
      );

      await api.repository.fetchMessages('cnv_1', page: 3, pageSize: 20);

      expect(api.adapter.requests.single.queryParameters, {
        'page': 3,
        'pageSize': 20,
      });
    });

    test('parses the page it is handed', () async {
      final api = _api(
        jsonEncode({
          'total': 2,
          'page': 1,
          'pageSize': 50,
          'items': [
            jsonDecode(_messageBody(id: 'msg_1', authorKind: 'System', kind: 'ClientMessage')),
            jsonDecode(_messageBody(id: 'msg_2')),
          ],
        }),
      );

      final page = await api.repository.fetchMessages('cnv_1');

      expect(page.total, 2);
      expect(page.items, hasLength(2));
      expect(page.items.first.author, MessageAuthor.client);
      expect(page.items.last.author, MessageAuthor.staff);
    });

    test('propagates a refusal so the caller can show its error state', () async {
      final api = _api('{"message":"nope"}', statusCode: 500);

      expect(
        api.repository.fetchMessages('cnv_1'),
        throwsA(isA<DioException>()),
      );
    });
  });

  group('ApiThreadRepository.sendMessage', () {
    test('posts the text and reads the stored message back', () async {
      final api = _api(_messageBody(text: 'On my way.'));

      final sent = await api.repository.sendMessage('cnv_1', 'On my way.');

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/org_7/conversations/cnv_1/messages',
      );
      expect(request.data, {'text': 'On my way.'});
      expect(sent.text, 'On my way.');
      expect(sent.status, MessageStatus.sent);
    });
  });

  group('ApiThreadRepository.decideSignOff', () {
    ThreadMessage draft({String? contentHash}) => ThreadMessage(
      id: 'msg_9',
      author: MessageAuthor.agent,
      status: MessageStatus.awaitingSignOff,
      text: 'Shall I confirm the fitting?',
      contentHash: contentHash,
      createdAt: DateTime.utc(2026, 9, 18, 9),
    );

    test('posts the decision against the message', () async {
      final api = _api('{}');

      await api.repository.decideSignOff(
        conversationId: 'cnv_1',
        message: draft(contentHash: 'abc123'),
        approved: true,
      );

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/org_7/conversations/cnv_1/messages/msg_9/sign-off',
      );
      expect(request.data, {'approved': true, 'contentHash': 'abc123'});
    });

    test('carries a rejection as a rejection', () async {
      final api = _api('{}');

      await api.repository.decideSignOff(
        conversationId: 'cnv_1',
        message: draft(contentHash: 'abc123'),
        approved: false,
      );

      expect(api.adapter.requests.single.data, {
        'approved': false,
        'contentHash': 'abc123',
      });
    });

    test('binds the decision to the hash of the message it was shown', () async {
      // The API rejects a decision whose hash does not match the content the
      // approver saw, so the hash has to come off the message itself rather than
      // being re-derived or left blank.
      final api = _api('{}');

      await api.repository.decideSignOff(
        conversationId: 'cnv_1',
        message: draft(contentHash: 'the-hash-as-seen'),
        approved: true,
      );

      expect(
        (api.adapter.requests.single.data as Map)['contentHash'],
        'the-hash-as-seen',
      );
    });

    test('refuses to decide a draft it has no hash for', () async {
      // Better to say so than to send a blank hash the API will reject with a 400
      // nobody can act on.
      final api = _api('{}');

      expect(
        api.repository.decideSignOff(
          conversationId: 'cnv_1',
          message: draft(),
          approved: true,
        ),
        throwsA(isA<StateError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });
}
