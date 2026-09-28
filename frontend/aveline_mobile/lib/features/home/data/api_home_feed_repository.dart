import 'package:dio/dio.dart';

import '../domain/focus_task.dart';

/// Reads Home's focus deck and records a decision about one docket.
///
/// Narrower than `HomeRepository` on purpose: this is the feed half, which the
/// production repository composes with the balance and the client row.
abstract interface class HomeFeedRepository {
  /// Today's deck, as the server ordered it.
  Future<List<FocusTask>> fetchTasks({required bool ownerDeck});

  /// Records one decision. Throws when the server refuses.
  Future<void> dismiss(FocusTask task, {required String idempotencyKey});
}

/// The derived focus feed, from `GET /api/v1/orgs/{id}/stats/home`, and the
/// dismissal record at `POST /api/v1/orgs/{id}/focus/dismissals`.
class ApiHomeFeedRepository implements HomeFeedRepository {
  ApiHomeFeedRepository(this._dio, {required this.organizationId});

  final Dio _dio;
  final String organizationId;

  @override
  Future<List<FocusTask>> fetchTasks({required bool ownerDeck}) async {
    final response = await _dio.get('/api/v1/orgs/$organizationId/stats/home');
    final data = response.data;
    if (data is! Map) {
      return const [];
    }

    final items = data['items'];
    if (items is! List) {
      return const [];
    }

    final tasks = <FocusTask>[];
    for (final raw in items) {
      final task = _toTask(raw);
      if (task != null) {
        tasks.add(task);
      }
    }
    return tasks;
  }

  @override
  Future<void> dismiss(FocusTask task, {required String idempotencyKey}) async {
    await _dio.post(
      '/api/v1/orgs/$organizationId/focus/dismissals',
      data: {
        'sourceKey': task.sourceKey ?? task.id,
        'domain': task.domain.wireValue,
        'decision': _decisionFor(task.actionLabel),
        'contentHash': null,
        'note': null,
      },
      options: Options(headers: {'Idempotency-Key': idempotencyKey}),
    );
  }

  FocusTask? _toTask(Object? raw) {
    if (raw is! Map) {
      return null;
    }

    final domain = FocusDomain.fromWire(raw['domain'] as String?);
    final id = raw['id'] as String?;
    if (domain == null || id == null || id.isEmpty) {
      return null;
    }

    return FocusTask(
      id: id,
      sourceKey: raw['sourceKey'] as String?,
      domain: domain,
      title: (raw['title'] as String?) ?? '',
      detail: (raw['detail'] as String?) ?? '',
      timeLabel: raw['timeLabel'] as String?,
      dueAtUtc: _parseUtc(raw['dueAtUtc']),
      actionLabel: (raw['actionLabel'] as String?) ?? 'Sign Off',
      doneMessage: (raw['doneMessage'] as String?) ?? 'Done.',
    );
  }

  static DateTime? _parseUtc(Object? value) {
    if (value is! String) {
      return null;
    }
    return DateTime.tryParse(value)?.toUtc();
  }

  /// The wire verb the card's label means.
  static String _decisionFor(String actionLabel) {
    switch (actionLabel.trim().toLowerCase()) {
      case 'approve':
        return 'approve';
      case 'reject':
        return 'reject';
      case 'acknowledge':
        return 'acknowledge';
      case 'mark ready':
        return 'markReady';
      default:
        return 'signOff';
    }
  }
}
