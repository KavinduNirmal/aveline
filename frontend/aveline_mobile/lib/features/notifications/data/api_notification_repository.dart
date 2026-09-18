import 'package:dio/dio.dart';

import '../domain/notification_page.dart';
import 'notification_repository.dart';

/// The notification inbox, over the Aveline API.
///
/// Endpoints follow `docs/api/openapi.yaml`:
/// - `GET    /api/v1/notifications`               paged, `unreadOnly` narrowing
/// - `GET    /api/v1/notifications/unread-count`  the badge
/// - `PATCH  /api/v1/notifications/{id}/read`     mark one read (idempotent)
/// - `POST   /api/v1/notifications/read-all`      mark every visible one read
/// - `DELETE /api/v1/notifications/{id}`          dismiss one (soft delete)
///
/// The auth interceptor on the shared [Dio] attaches the Clerk token, so nothing
/// here handles credentials.
class ApiNotificationRepository implements NotificationRepository {
  ApiNotificationRepository(this._dio);

  final Dio _dio;

  static const String _inboxPath = '/api/v1/notifications';

  @override
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  }) async {
    final response = await _dio.get<Map<String, dynamic>>(
      _inboxPath,
      queryParameters: {
        'page': page,
        'pageSize': pageSize,
        'unreadOnly': unreadOnly,
      },
    );
    return NotificationPage.fromJson(response.data ?? const {});
  }

  @override
  Future<int> fetchUnreadCount() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '$_inboxPath/unread-count',
    );
    return (response.data?['count'] as num?)?.toInt() ?? 0;
  }

  @override
  Future<void> markRead(String id) async {
    await _dio.patch<void>('$_inboxPath/$id/read');
  }

  @override
  Future<void> markAllRead() async {
    await _dio.post<void>('$_inboxPath/read-all');
  }

  @override
  Future<void> dismiss(String id) async {
    await _dio.delete<void>('$_inboxPath/$id');
  }
}
