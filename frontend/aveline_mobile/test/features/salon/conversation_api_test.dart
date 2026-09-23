import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/salon/data/conversation_api.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

const String _orgId = '33333333-3333-4333-8333-333333333333';
const String _conversationId = '11111111-1111-4111-8111-111111111111';
const String _attachmentId = '44444444-4444-4444-8444-444444444444';

/// Serves one canned response and records every request that reached it.
class _RecordingAdapter implements HttpClientAdapter {
  _RecordingAdapter(this.body);

  final String body;
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
      200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  group('ConversationApi attachments', () {
    test('stores a file against the Salon and returns its id', () async {
      final adapter = _RecordingAdapter(
        jsonEncode({
          'attachmentId': _attachmentId,
          'url': '/api/v1/attachments/$_attachmentId',
          'contentType': 'image/png',
          'fileName': 'dress.png',
          'sizeBytes': 3,
        }),
      );
      final api = ConversationApi(Dio()..httpClientAdapter = adapter);

      final id = await api.uploadAttachment(
        organizationId: _orgId,
        conversationId: _conversationId,
        bytes: Uint8List.fromList([1, 2, 3]),
        contentType: 'image/png',
        fileName: 'dress.png',
      );

      expect(id, _attachmentId);
      final request = adapter.requests.single;
      expect(
        request.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/attachments',
      );
      // The type travels inside the data URL, so the server never has to guess it from
      // the file name.
      expect((request.data as Map)['imageData'], 'data:image/png;base64,AQID');
      expect((request.data as Map)['fileName'], 'dress.png');
    });

    test('binds the stored ids to the note it sends', () async {
      final adapter = _RecordingAdapter(jsonEncode({'id': 'm1', 'text': 'hi'}));
      final api = ConversationApi(Dio()..httpClientAdapter = adapter);

      await api.sendMessage(
        organizationId: _orgId,
        conversationId: _conversationId,
        text: 'hi',
        attachmentIds: const [_attachmentId],
      );

      expect(
        adapter.requests.single.path,
        '/api/v1/orgs/$_orgId/conversations/$_conversationId/messages',
      );
      expect((adapter.requests.single.data as Map)['attachmentIds'], [
        _attachmentId,
      ]);
    });

    test('omits attachmentIds for a note with no files', () async {
      final adapter = _RecordingAdapter(jsonEncode({'id': 'm1', 'text': 'hi'}));
      final api = ConversationApi(Dio()..httpClientAdapter = adapter);

      await api.sendMessage(
        organizationId: _orgId,
        conversationId: _conversationId,
        text: 'hi',
      );

      // Omitted rather than sent empty, so a note with no files stays the payload it was.
      expect(
        (adapter.requests.single.data as Map).containsKey('attachmentIds'),
        isFalse,
      );
    });
  });
}
