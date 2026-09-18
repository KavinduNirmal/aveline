import '../domain/notification_page.dart';

/// The signed-in user's notification inbox.
///
/// One interface for both the API and the demo inbox the app ships with until
/// the notification endpoints are live, so the tab does not know which one it is
/// talking to. Every method is scoped to the caller on the server side: there is
/// no user id in these calls on purpose.
abstract interface class NotificationRepository {
  /// One page of the inbox, newest first.
  ///
  /// [unreadOnly] is the narrowing behind the tab's Unread filter. Dismissed
  /// notifications are already excluded by the server.
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  });

  /// How many visible notifications are unread.
  ///
  /// Fetched from its own endpoint rather than counted from the loaded page,
  /// because the page is only part of the inbox and the badge has to be right
  /// before the associate scrolls to the end.
  Future<int> fetchUnreadCount();

  /// Marks one notification read. Idempotent: marking a read one is not an
  /// error, and there is deliberately no way back, because the API has none.
  Future<void> markRead(String id);

  /// Marks every visible notification read.
  Future<void> markAllRead();

  /// Dismisses one notification. A soft delete on the server; the audit trail
  /// behind it is untouched.
  Future<void> dismiss(String id);
}
