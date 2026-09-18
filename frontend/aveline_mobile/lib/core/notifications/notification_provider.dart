import 'package:flutter/foundation.dart';

import 'notification_payload.dart';

/// Holds the most recent notification received by the app (realtime or push), the
/// unread count the header badge wears, and notifies listeners so UI can react
/// (e.g. show a banner or refresh an inbox).
///
/// The inbox itself is not here: it belongs to the notifications feature's own
/// controller, which reports its count up through [setUnreadCount]. Keeping the
/// badge's number in one place is what stops the header and the tab disagreeing.
class NotificationProvider extends ChangeNotifier {
  NotificationPayload? _latest;
  int _unreadCount = 0;
  bool _hasUnreadCount = false;

  /// The most recent notification, or `null` when none has arrived yet.
  NotificationPayload? get latest => _latest;

  /// How many unread notifications the inbox holds.
  int get unreadCount => _unreadCount;

  /// Whether [unreadCount] has been reported by the inbox yet.
  ///
  /// Until it has, the badge falls back to a dot on [latest]: the app knows a
  /// notification arrived but not how many are waiting, and a badge reading
  /// "nothing unread" would be a worse guess than a dot.
  bool get hasUnreadCount => _hasUnreadCount;

  /// Records a new notification and notifies listeners.
  void push(NotificationPayload payload) {
    _latest = payload;
    notifyListeners();
  }

  /// Records the inbox's unread count and notifies listeners.
  void setUnreadCount(int count) {
    final normalized = count < 0 ? 0 : count;
    if (_hasUnreadCount && normalized == _unreadCount) {
      return;
    }
    _unreadCount = normalized;
    _hasUnreadCount = true;
    notifyListeners();
  }

  /// Clears the latest notification and the count (e.g. on sign-out).
  void clear() {
    if (_latest == null && !_hasUnreadCount) {
      return;
    }
    _latest = null;
    _unreadCount = 0;
    // The count is forgotten rather than set to zero: the next session's inbox
    // has not reported yet, and the badge should wait for it.
    _hasUnreadCount = false;
    notifyListeners();
  }
}
