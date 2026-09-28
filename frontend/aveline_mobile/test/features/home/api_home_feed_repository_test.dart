import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/home/data/api_home_feed_repository.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  Object? body;
  int statusCode = 200;
  String? lastPath;
  Object? lastBody;
  Map<String, dynamic>? lastHeaders;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastPath = options.path;
    lastBody = options.data;
    lastHeaders = options.headers;
    if (statusCode != 200) {
      return ResponseBody.fromString('{}', statusCode);
    }
    return ResponseBody.fromString(
      jsonEncode(body),
      200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

Map<String, Object?> _item({
  String id = 'wardrobe:1',
  String sourceKey = '11111111-1111-1111-1111-111111111111',
  String domain = 'wardrobe',
  String title = 'Count in / reorder the raw silk',
  String detail = 'Two left',
  String? dueAtUtc = '2026-09-20T09:30:00Z',
  String actionLabel = 'Sign Off',
}) =>
    {
      'id': id,
      'sourceKey': sourceKey,
      'domain': domain,
      'title': title,
      'detail': detail,
      'dueAtUtc': dueAtUtc,
      'timeLabel': null,
      'actionLabel': actionLabel,
      'doneMessage': 'Signed off.',
      'contentHash': 'abc',
      'caps': {'canComplete': true, 'canAssign': false},
    };

Map<String, Object?> _feed(List<Map<String, Object?>> items) => {
      'generatedAt': '2026-09-18T12:00:00Z',
      'window': {'localDate': '2026-09-18', 'timeZone': 'Asia/Colombo'},
      'dataQuality': {
        'wardrobeAvailable': true,
        'patronAvailable': false,
        'commerceAvailable': false,
        'logisticsAvailable': false,
      },
      'items': items,
      'counts': {
        'total': items.length,
        'overdue': 0,
        'byDomain': {'wardrobe': items.length},
      },
    };

void main() {
  late _StubAdapter adapter;
  late Dio dio;
  late ApiHomeFeedRepository repository;

  setUp(() {
    adapter = _StubAdapter(_feed([_item()]));
    dio = Dio()..httpClientAdapter = adapter;
    repository = ApiHomeFeedRepository(dio, organizationId: 'org-1');
  });

  group('ApiHomeFeedRepository', () {
    test('parses a task with a due timestamp and no time label', () async {
      final tasks = await repository.fetchTasks(ownerDeck: false);

      expect(adapter.lastPath, '/api/v1/orgs/org-1/stats/home');
      expect(tasks, hasLength(1));
      final task = tasks.single;
      expect(task.id, 'wardrobe:1');
      expect(task.sourceKey, '11111111-1111-1111-1111-111111111111');
      expect(task.domain, FocusDomain.wardrobe);
      expect(task.dueAtUtc, DateTime.utc(2026, 9, 20, 9, 30));
      // The client formats the clock from the timestamp; the wire need not carry
      // a display string.
      expect(task.displayTimeLabel, isNotNull);
    });

    test('drops an item whose domain is not a known value', () async {
      adapter.body = _feed([
        _item(),
        _item(id: 'x', sourceKey: 'k', domain: 'banana'),
      ]);

      final tasks = await repository.fetchTasks(ownerDeck: false);

      // A closed enum must never throw on an unknown wire value.
      expect(tasks, hasLength(1));
      expect(tasks.single.domain, FocusDomain.wardrobe);
    });

    test('posts the dismissal with a fresh idempotency key', () async {
      const task = FocusTask(
        id: 'wardrobe:1',
        sourceKey: 'abc',
        domain: FocusDomain.wardrobe,
        title: 'Count in',
        detail: 'Two left',
        timeLabel: '9:30 AM',
        actionLabel: 'Sign Off',
        doneMessage: 'Signed off.',
      );

      await repository.dismiss(task, idempotencyKey: 'key-1');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/focus/dismissals');
      expect(adapter.lastHeaders?['Idempotency-Key'], 'key-1');
      final body = adapter.lastBody as Map;
      expect(body['sourceKey'], 'abc');
      expect(body['domain'], 'wardrobe');
      expect(body['decision'], 'signOff');
    });

    test('maps the card verb onto the wire decision', () async {
      const task = FocusTask(
        id: 'patron:1',
        sourceKey: 'abc',
        domain: FocusDomain.patron,
        title: 'Prepare',
        detail: 'Tomorrow',
        timeLabel: '10:00 AM',
        actionLabel: 'Mark ready',
        doneMessage: 'Ready.',
      );

      await repository.dismiss(task, idempotencyKey: 'key-2');

      expect((adapter.lastBody as Map)['decision'], 'markReady');
    });
  });
}
