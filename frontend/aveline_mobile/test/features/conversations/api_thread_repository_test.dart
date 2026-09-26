import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/network/org_context.dart';
import 'package:aveline_mobile/features/conversations/data/api_thread_repository.dart';
import 'package:aveline_mobile/features/conversations/data/thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Real GUIDs, because the repository refuses anything that is not UUID-shaped before it
/// builds a path: the API's route template is `{conversationId:guid}`, so a demo id would come
/// back as an opaque 400 with nothing in it the associate could act on.
const String _orgId = '33333333-3333-4333-8333-333333333333';
const String _conversationId = '11111111-1111-4111-8111-111111111111';
const String _messageId = '22222222-2222-4222-8222-222222222222';

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

/// Serves raw bytes, for the attachment fetch.
class _BytesAdapter implements HttpClientAdapter {
  _BytesAdapter(this.bytes);

  final List<int> bytes;
  final List<RequestOptions> requests = [];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    return ResponseBody.fromBytes(
      bytes,
      200,
      headers: {
        Headers.contentTypeHeader: ['image/png'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

({ApiThreadRepository repository, _RecordingAdapter adapter}) _api(
  String body, {
  int statusCode = 200,
  String? Function()? organizationId,
}) {
  final adapter = _RecordingAdapter(body, statusCode: statusCode);
  final dio = Dio()..httpClientAdapter = adapter;
  return (
    repository: ApiThreadRepository(
      dio,
      organizationId: organizationId ?? () => _orgId,
    ),
    adapter: adapter,
  );
}

String _messageBody({
  String id = _messageId,
  String text = 'On my way.',
  String kind = 'Note',
  String authorKind = 'User',
  String status = 'Sent',
  String? contentHash,
}) => jsonEncode({
  'id': id,
  'conversationId': _conversationId,
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

      await api.repository.fetchMessages(_conversationId);

      expect(api.adapter.requests.single.method, 'GET');
      expect(
        api.adapter.requests.single.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages',
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

      await api.repository.fetchMessages(
        _conversationId,
        page: 3,
        pageSize: 20,
      );

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
            jsonDecode(_messageBody(id: 'aaaaaaa1-1111-4111-8111-111111111111', authorKind: 'System', kind: 'ClientMessage')),
            jsonDecode(_messageBody(id: 'aaaaaaa2-1111-4111-8111-111111111111')),
          ],
        }),
      );

      final page = await api.repository.fetchMessages(_conversationId);

      expect(page.total, 2);
      expect(page.items, hasLength(2));
      expect(page.items.first.author, MessageAuthor.client);
      expect(page.items.last.author, MessageAuthor.staff);
    });

    test('reads the echoed pageSize, which is the server\'s clamp', () async {
      // A client that asked for 500 against a 300-message thread must page on what the
      // server actually served, not on what it asked for.
      final api = _api(
        jsonEncode({
          'total': 300,
          'page': 1,
          'pageSize': 200,
          'items': <Object>[],
        }),
      );

      final page = await api.repository.fetchMessages(
        _conversationId,
        pageSize: 500,
      );

      expect(page.pageSize, 200);
      expect(page.pageCount, 2);
    });

    test('carries an anchor when the caller deep-linked to a message', () async {
      final api = _api(
        jsonEncode({'total': 0, 'page': 2, 'pageSize': 50, 'items': <Object>[]}),
      );

      await api.repository.fetchMessages(_conversationId, around: _messageId);

      expect(api.adapter.requests.single.queryParameters, {
        'page': 1,
        'pageSize': 50,
        'around': _messageId,
      });
    });

    test('omits the anchor when there is none', () async {
      final api = _api(
        jsonEncode({'total': 0, 'page': 1, 'pageSize': 50, 'items': <Object>[]}),
      );

      await api.repository.fetchMessages(_conversationId);

      expect(
        (api.adapter.requests.single.queryParameters as Map).containsKey('around'),
        isFalse,
      );
    });

    test('refuses an anchor that is not a UUID, without a request', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.fetchMessages(_conversationId, around: 'msg_8'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });

    test('propagates a refusal so the caller can show its error state', () async {
      final api = _api('{"message":"nope"}', statusCode: 500);

      expect(
        api.repository.fetchMessages(_conversationId),
        throwsA(isA<DioException>()),
      );
    });
  });

  group('ApiThreadRepository organization context', () {
    test('reads the organization id at call time, not at construction', () async {
      // The id arrives from `GET /orgs/my` after the shell mounts, so a repository built
      // before it is known has to see the later value.
      var orgId = '44444444-4444-4444-8444-444444444444';
      final api = _api(
        jsonEncode({'total': 0, 'page': 1, 'pageSize': 50, 'items': <Object>[]}),
        organizationId: () => orgId,
      );

      await api.repository.fetchMessages(_conversationId);
      orgId = '55555555-5555-4555-8555-555555555555';
      await api.repository.fetchMessages(_conversationId);

      expect(
        api.adapter.requests.map((request) => request.path),
        [
          '/api/v1/orgs/44444444-4444-4444-8444-444444444444/conversations/$_conversationId/messages',
          '/api/v1/orgs/55555555-5555-4555-8555-555555555555/conversations/$_conversationId/messages',
        ],
      );
    });

    test('a missing organization id is a "not yet", not a request', () async {
      final api = _api(
        jsonEncode({'total': 0, 'page': 1, 'pageSize': 50, 'items': <Object>[]}),
        organizationId: () => null,
      );

      await expectLater(
        api.repository.fetchMessages(_conversationId),
        throwsA(isA<OrgContextUnavailable>()),
      );
      expect(api.adapter.requests, isEmpty);
    });

    test('a blank organization id is a "not yet" too', () async {
      final api = _api(
        jsonEncode({'total': 0, 'page': 1, 'pageSize': 50, 'items': <Object>[]}),
        organizationId: () => '',
      );

      await expectLater(
        api.repository.fetchMessages(_conversationId),
        throwsA(isA<OrgContextUnavailable>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository id validation', () {
    test('refuses a conversation id that is not a UUID, without a request', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.fetchMessages('cnv_nadeesha'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });

    test('refuses a message id that is not a UUID, without a request', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.decideSignOff(
          conversationId: _conversationId,
          message: ThreadMessage(
            id: 'msg_9',
            author: MessageAuthor.agent,
            status: MessageStatus.awaitingSignOff,
            text: 'Shall I confirm the fitting?',
            contentHash: 'abc123',
            createdAt: DateTime.utc(2026, 9, 18, 9),
          ),
          approved: true,
        ),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });

    test('accepts an uppercase UUID', () async {
      final api = _api(
        jsonEncode({'total': 0, 'page': 1, 'pageSize': 50, 'items': <Object>[]}),
      );

      await api.repository.fetchMessages(_conversationId.toUpperCase());

      expect(api.adapter.requests, hasLength(1));
    });
  });

  group('ApiThreadRepository.sendMessage', () {
    test('posts the text and reads the stored message back', () async {
      final api = _api(_messageBody(text: 'On my way.'));

      final sent = await api.repository.sendMessage(_conversationId, 'On my way.');

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages',
      );
      expect(request.data, {'text': 'On my way.'});
      expect(sent.text, 'On my way.');
      expect(sent.status, MessageStatus.sent);
    });

    test('carries the idempotency key when it has one', () async {
      final api = _api(_messageBody());

      await api.repository.sendMessage(
        _conversationId,
        'On my way.',
        clientMessageId: '22222222-2222-4222-8222-222222222222',
      );

      expect(api.adapter.requests.single.data, {
        'text': 'On my way.',
        'clientMessageId': '22222222-2222-4222-8222-222222222222',
      });
    });

    test('omits the key when there is none', () async {
      final api = _api(_messageBody());

      await api.repository.sendMessage(_conversationId, 'On my way.');

      expect((api.adapter.requests.single.data as Map).containsKey('clientMessageId'), isFalse);
    });
  });

  group('ApiThreadRepository attachments', () {
    test('uploads a file as a data URL and reads the stored row back', () async {
      final api = _api(jsonEncode({
        'attachmentId': _messageId,
        'url': '/api/v1/orgs/$_orgId/conversations/$_conversationId/attachments/$_messageId',
        'contentType': 'image/png',
        'fileName': 'photo.png',
        'sizeBytes': 3,
        'width': 10,
        'height': 20,
      }));

      final attachment = await api.repository.uploadAttachment(
        _conversationId,
        bytes: Uint8List.fromList([1, 2, 3]),
        contentType: 'image/png',
        fileName: 'photo.png',
        width: 10,
        height: 20,
      );

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/attachments',
      );
      // The declared type rides the data URL, so the server does not have to guess from the
      // file name.
      expect((request.data as Map)['imageData'], 'data:image/png;base64,AQID');
      expect(attachment.id, _messageId);
      expect(attachment.contentType, 'image/png');
      expect(attachment.sizeBytes, 3);
      expect(attachment.isImage, isTrue);
    });

    test('carries attachment ids on the send', () async {
      final api = _api(_messageBody());

      await api.repository.sendMessage(
        _conversationId,
        'Here it is.',
        attachmentIds: [_messageId],
      );

      expect(api.adapter.requests.single.data, {
        'text': 'Here it is.',
        'attachmentIds': [_messageId],
      });
    });

    test('omits attachment ids when there are none', () async {
      final api = _api(_messageBody());

      await api.repository.sendMessage(_conversationId, 'Plain.');

      expect(
        (api.adapter.requests.single.data as Map).containsKey('attachmentIds'),
        isFalse,
      );
    });

    test('reads the bytes through the authenticated client, and caches them', () async {
      final adapter = _BytesAdapter([9, 8, 7]);
      final dio = Dio()..httpClientAdapter = adapter;
      final repository = ApiThreadRepository(dio, organizationId: () => _orgId);

      final first = await repository.fetchAttachmentBytes(_conversationId, _messageId);
      final second = await repository.fetchAttachmentBytes(_conversationId, _messageId);

      expect(first, [9, 8, 7]);
      expect(second, [9, 8, 7]);
      // The second read is served from the cache: one request, not two.
      expect(adapter.requests, hasLength(1));
      expect(
        adapter.requests.single.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/attachments/$_messageId',
      );
    });

    test('refuses an attachment id that is not a UUID', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.fetchAttachmentBytes(_conversationId, 'att_1'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository.selectCustomer', () {
    test('posts the chosen client to the resolution route', () async {
      final api = _api('{}');

      await api.repository.selectCustomer(
        _conversationId,
        '44444444-4444-4444-8444-444444444444',
        query: 'Nadeesha',
      );

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/select-customer',
      );
      expect(request.data, {
        'customerId': '44444444-4444-4444-8444-444444444444',
        'query': 'Nadeesha',
      });
    });

    test('refuses a customer id that is not a UUID, without a request', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.selectCustomer(_conversationId, 'cus_1'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository.revokeSignOff', () {
    test('posts the revocation and reads the stored message back', () async {
      final api = _api(_messageBody(status: 'AwaitingSignOff', kind: 'SignOff'));

      final revoked = await api.repository.revokeSignOff(
        _conversationId,
        _messageId,
        reason: 'customer changed their mind',
      );

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages/$_messageId/sign-off/revoke',
      );
      expect(request.data, {'reason': 'customer changed their mind'});
      expect(revoked.status, MessageStatus.awaitingSignOff);
    });

    test('omits the reason when there is none', () async {
      final api = _api(_messageBody(status: 'AwaitingSignOff', kind: 'SignOff'));

      await api.repository.revokeSignOff(_conversationId, _messageId);

      expect(api.adapter.requests.single.data, <String, dynamic>{});
    });

    test('refuses a message id that is not a UUID, without a request', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.revokeSignOff(_conversationId, 'msg_9'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository.markRead', () {
    test('patches the caller\'s marker for the conversation', () async {
      final api = _api('');

      await api.repository.markRead(_conversationId, _messageId);

      final request = api.adapter.requests.single;
      expect(request.method, 'PATCH');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages/read',
      );
      expect(request.data, {'lastReadMessageId': _messageId});
    });

    test('refuses a marker that is not a UUID, without a request', () async {
      final api = _api('');

      await expectLater(
        api.repository.markRead(_conversationId, 'local_1'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository.decideSignOff', () {
    ThreadMessage draft({String? contentHash}) => ThreadMessage(
      id: _messageId,
      author: MessageAuthor.agent,
      status: MessageStatus.awaitingSignOff,
      text: 'Shall I confirm the fitting?',
      contentHash: contentHash,
      createdAt: DateTime.utc(2026, 9, 18, 9),
    );

    test('posts the decision against the message', () async {
      final api = _api('{}');

      await api.repository.decideSignOff(
        conversationId: _conversationId,
        message: draft(contentHash: 'abc123'),
        approved: true,
      );

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages/$_messageId/sign-off',
      );
      expect(request.data, {'approved': true, 'contentHash': 'abc123'});
    });

    test('carries a rejection as a rejection', () async {
      final api = _api('{}');

      await api.repository.decideSignOff(
        conversationId: _conversationId,
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
        conversationId: _conversationId,
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
          conversationId: _conversationId,
          message: draft(),
          approved: true,
        ),
        throwsA(isA<StateError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository.deliver', () {
    test('posts the text to the delivery route and reads where it went', () async {
      final api = _api(
        jsonEncode({
          'delivered': true,
          'channel': 'WhatsApp',
          'providerMessageId': 'wamid.1',
          'message': jsonDecode(_messageBody(text: 'On my way.')),
        }),
      );

      final delivery = await api.repository.deliver(_conversationId, 'On my way.');

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/deliver',
      );
      expect(request.data, {'text': 'On my way.'});
      expect(delivery.delivered, isTrue);
      expect(delivery.channel, 'WhatsApp');
      expect(delivery.providerMessageId, 'wamid.1');
      expect(delivery.message?.text, 'On my way.');
    });

    test('carries the idempotency key when it has one', () async {
      final api = _api('{"delivered":true}');

      await api.repository.deliver(
        _conversationId,
        'On my way.',
        clientMessageId: '22222222-2222-4222-8222-222222222222',
      );

      expect(api.adapter.requests.single.data, {
        'text': 'On my way.',
        'clientMessageId': '22222222-2222-4222-8222-222222222222',
      });
    });

    test('omits the key when there is none', () async {
      final api = _api('{"delivered":true}');

      await api.repository.deliver(_conversationId, 'On my way.');

      expect(
        (api.adapter.requests.single.data as Map).containsKey('clientMessageId'),
        isFalse,
      );
    });

    test('a refusal names the reason rather than surfacing a raw status', () async {
      final api = _api(
        '{"refusal":"no_customer","detail":"This thread has no client."}',
        statusCode: 409,
      );

      await expectLater(
        api.repository.deliver(_conversationId, 'On my way.'),
        throwsA(
          isA<DeliveryRefused>()
              .having((refusal) => refusal.refusal, 'refusal', 'no_customer')
              .having(
                (refusal) => refusal.detail,
                'detail',
                'This thread has no client.',
              ),
        ),
      );
    });

    test('a provider refusal is its own code', () async {
      final api = _api(
        '{"refusal":"provider_refused"}',
        statusCode: 502,
      );

      await expectLater(
        api.repository.deliver(_conversationId, 'On my way.'),
        throwsA(
          isA<DeliveryRefused>()
              .having((refusal) => refusal.refusal, 'refusal', 'provider_refused'),
        ),
      );
    });

    test('an unknown conversation stays a transport failure, not a refusal', () async {
      final api = _api('{"message":"gone"}', statusCode: 404);

      await expectLater(
        api.repository.deliver(_conversationId, 'On my way.'),
        throwsA(
          isA<DioException>().having(
            (error) => error.response?.statusCode,
            'status',
            404,
          ),
        ),
      );
    });

    test('refuses a conversation id that is not a UUID, without a request', () async {
      final api = _api('{}');

      await expectLater(
        api.repository.deliver('cnv_nadeesha', 'On my way.'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });

  group('ApiThreadRepository.regenerate', () {
    test('asks the agent for a fresh reply and accepts the 202 with no body', () async {
      final api = _api('', statusCode: 202);

      await api.repository.regenerate(_conversationId, _messageId);

      final request = api.adapter.requests.single;
      expect(request.method, 'POST');
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages/$_messageId/regenerate',
      );
    });

    test('refuses a message id that is not a UUID, without a request', () async {
      final api = _api('', statusCode: 202);

      await expectLater(
        api.repository.regenerate(_conversationId, 'msg_9'),
        throwsA(isA<ArgumentError>()),
      );
      expect(api.adapter.requests, isEmpty);
    });
  });
}
