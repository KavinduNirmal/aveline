import '../domain/notification_page.dart';
import 'notification_repository.dart';

/// An inbox with nothing in it, used wherever no real source has been injected.
///
/// The screen used to fall back to `DemoNotificationRepository`, which put nine
/// seeded notifications on a production path: a widget test or preview that
/// mounted [NotificationsScreen] alone rendered a full, invented inbox. This
/// repository is the honest stand-in — no inbox, and a refusal that says why —
/// so the screen draws its real empty state instead of fiction. It follows
/// `EmptyThreadRepository`/`EmptyConversationRepository`.
///
/// A mutation is a programming error on this path rather than a user action:
/// nothing can be written into an inbox that has no source. The failure is still
/// returned as a readable [StateError] so a caller that reaches it sees why
/// rather than a silent no-op.
class EmptyNotificationRepository implements NotificationRepository {
  const EmptyNotificationRepository();

  @override
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  }) async => NotificationPage(
    items: const [],
    total: 0,
    page: page,
    pageSize: 0,
  );

  @override
  Future<int> fetchUnreadCount() async => 0;

  @override
  Future<void> markRead(String id) async => throw StateError(
    'There is no inbox source, so nothing can be marked as read.',
  );

  @override
  Future<void> markAllRead() async => throw StateError(
    'There is no inbox source, so nothing can be marked as read.',
  );

  @override
  Future<void> dismiss(String id) async => throw StateError(
    'There is no inbox source, so nothing can be deleted.',
  );
}
