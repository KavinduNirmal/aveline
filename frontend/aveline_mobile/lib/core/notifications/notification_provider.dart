import 'package:flutter/foundation.dart';

import 'notification_payload.dart';

/// Holds the most recent notification received by the app (realtime or push) and
/// notifies listeners so UI can react (e.g. show a banner or refresh an inbox).
class NotificationProvider extends ChangeNotifier {
  NotificationPayload? _latest;

  /// The most recent notification, or `null` when none has arrived yet.
  NotificationPayload? get latest => _latest;

  /// Records a new notification and notifies listeners.
  void push(NotificationPayload payload) {
    _latest = payload;
    notifyListeners();
  }

  /// Clears the latest notification (e.g. on sign-out).
  void clear() {
    if (_latest == null) {
      return;
    }
    _latest = null;
    notifyListeners();
  }
}
