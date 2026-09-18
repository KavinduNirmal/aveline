import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/notifications/data/api_notification_repository.dart';
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
    // The content type matters: Dio only decodes the body into a map when the
    // reply announces JSON, which is what the real API sends.
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

/// Builds the repository over a single canned reply, plus the adapter that saw
/// the request, so a test can assert both sides of the round trip.
({ApiNotificationRepository repository, _RecordingAdapter adapter}) _api(
  String body, {
  int statusCode = 200,
}) {
  final adapter = _RecordingAdapter(body, statusCode: statusCode);
  final dio = Dio()..httpClientAdapter = adapter;
  return (repository: ApiNotificationRepository(dio), adapter: adapter);
}

void main() {
  group('ApiNotificationRepository.fetchInbox', () {
    test('gets the documented page and parses it', () async {
      final api = _api(
        jsonEncode({
          'total': 3,
          'page': 1,
          'pageSize': 20,
          'items': [
            {
              'id': 'un_1',
              'notificationId': 'nr_1',
              'type': 'NewMessage',
              'title': 'Nadeesha replied',
              'body': 'Can the wine saree be altered?',
              'data': {'conversationId': 'c_1'},
              'isRead': false,
              'readAt': null,
              'deliveredAt': '2026-09-17T09:14:02Z',
              'createdAt': '2026-09-17T09:14:00Z',
            },
          ],
        }),
      );

      final page = await api.repository.fetchInbox();

      expect(api.adapter.requests, hasLength(1));
      expect(api.adapter.requests.single.method, 'GET');
      expect(api.adapter.requests.single.path, '/api/v1/notifications');
      expect(api.adapter.requests.single.queryParameters, {
        'page': 1,
        'pageSize': 20,
        'unreadOnly': false,
      });
      expect(page.items.single.id, 'un_1');
      expect(page.items.single.isRead, isFalse);
      expect(page.total, 3);
    });

    test('carries the page and the unread narrowing it was asked for', () async {
      final api = _api(jsonEncode({'total': 0, 'page': 3, 'pageSize': 5}));

      await api.repository.fetchInbox(page: 3, pageSize: 5, unreadOnly: true);

      expect(api.adapter.requests.single.queryParameters, {
        'page': 3,
        'pageSize': 5,
        'unreadOnly': true,
      });
    });

    test('propagates a refusal so the caller can show its error state', () async {
      final api = _api('{"message":"nope"}', statusCode: 500);

      expect(
        api.repository.fetchInbox(),
        throwsA(isA<DioException>()),
      );
    });
  });

  group('ApiNotificationRepository.fetchUnreadCount', () {
    test('reads the count from its own endpoint', () async {
      final api = _api('{"count":7}');

      final count = await api.repository.fetchUnreadCount();

      expect(api.adapter.requests.single.method, 'GET');
      expect(
        api.adapter.requests.single.path,
        '/api/v1/notifications/unread-count',
      );
      expect(count, 7);
    });

    test('reads an absent count as nothing unread', () async {
      final api = _api('{}');

      expect(await api.repository.fetchUnreadCount(), 0);
    });
  });

  group('ApiNotificationRepository mutations', () {
    test('markRead patches the read endpoint for one item', () async {
      final api = _api('', statusCode: 204);

      await api.repository.markRead('un_9');

      expect(api.adapter.requests.single.method, 'PATCH');
      expect(
        api.adapter.requests.single.path,
        '/api/v1/notifications/un_9/read',
      );
    });

    test('markAllRead posts the read-all endpoint', () async {
      final api = _api('{}');

      await api.repository.markAllRead();

      expect(api.adapter.requests.single.method, 'POST');
      expect(
        api.adapter.requests.single.path,
        '/api/v1/notifications/read-all',
      );
    });

    test('dismiss deletes the item', () async {
      final api = _api('', statusCode: 204);

      await api.repository.dismiss('un_9');

      expect(api.adapter.requests.single.method, 'DELETE');
      expect(api.adapter.requests.single.path, '/api/v1/notifications/un_9');
    });
  });
}
