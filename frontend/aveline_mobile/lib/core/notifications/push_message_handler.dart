import 'dart:async';

import 'notification_payload.dart';
import 'notification_route.dart';

/// A small seam over FCM's message streams so the handler can be unit tested
/// without the Firebase plugin or a device.
abstract interface class PushMessageSource {
  /// A message received while the app is in the foreground.
  Stream<Map<String, dynamic>> get onMessage;

  /// A message the user tapped while the app was backgrounded.
  Stream<Map<String, dynamic>> get onMessageOpenedApp;

  /// The message that launched the app, or `null` when it was not a push tap.
  Future<Map<String, dynamic>?> getInitialMessage();
}

/// Marks the notification a tap addressed as read, best-effort.
typedef NotificationMarkRead = Future<void> Function(String notificationId);

/// Opens a route location.
typedef NotificationRouteOpener = void Function(String location);

/// Handles FCM messages: a foreground arrival refreshes the inbox, and a tap
/// opens the app, marks **that notification** read, and routes through the one
/// shared rule.
///
/// A tap deliberately does not mark the conversation or the message read: the
/// thread owns its own read state and writes it when it is opened on the newest
/// message (Q7).
class PushMessageHandler {
  PushMessageHandler(this._source);

  final PushMessageSource _source;
  final List<StreamSubscription<Map<String, dynamic>>> _subscriptions = [];

  /// Starts the foreground and tap listeners.
  void start({
    required void Function(NotificationPayload payload) onMessage,
    required NotificationMarkRead markRead,
    required NotificationRouteOpener open,
  }) {
    _subscriptions.add(
      _source.onMessage.listen((data) {
        onMessage(NotificationPayload.fromJson(data));
      }),
    );
    _subscriptions.add(
      _source.onMessageOpenedApp.listen((data) {
        unawaited(handleTap(data, markRead: markRead, open: open));
      }),
    );
  }

  /// Applies the tap that launched the app, when there was one.
  ///
  /// Called once after sign-in. A tap can arrive before the session is restored,
  /// so the mark-read is best-effort and the route is opened regardless: nothing
  /// is thrown and the tap is never lost.
  Future<void> handleInitialMessage({
    required NotificationMarkRead markRead,
    required NotificationRouteOpener open,
  }) async {
    final data = await _source.getInitialMessage();
    if (data == null) {
      return;
    }
    await handleTap(data, markRead: markRead, open: open);
  }

  /// The tap semantics, shared by a warm and a cold start.
  ///
  /// The OS has already opened the app; this marks the notification read and
  /// routes. A payload that carries no destination still marks the notification
  /// read, and returns to the inbox it was already on.
  Future<void> handleTap(
    Map<String, dynamic> data, {
    required NotificationMarkRead markRead,
    required NotificationRouteOpener open,
  }) async {
    final payload = NotificationPayload.fromJson(data);

    final notificationId = payload.notificationId;
    if (notificationId != null && notificationId.isNotEmpty) {
      try {
        await markRead(notificationId);
      } catch (_) {
        // Best-effort: a session that is not restored yet must not lose the tap.
      }
    }

    // FCM flattens the notification's own keys into the message data, while the
    // hub nests them under `data`. Both are accepted so one handler serves both.
    String? routeId(String key) {
      final flat = data[key];
      if (flat is String && flat.isNotEmpty) {
        return flat;
      }
      final nested = payload.data[key];
      return nested == null || nested.isEmpty ? null : nested;
    }

    final location = notificationRouteForIds(
      conversationId: routeId('conversationId'),
      messageId: routeId('messageId'),
      customerId: routeId('customerId'),
    );
    if (location != null) {
      open(location);
    }
  }

  /// Stops listening. Safe to call more than once.
  Future<void> dispose() async {
    final subscriptions = [..._subscriptions];
    _subscriptions.clear();
    for (final subscription in subscriptions) {
      await subscription.cancel();
    }
  }
}
