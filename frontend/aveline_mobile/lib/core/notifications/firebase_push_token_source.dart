import 'package:firebase_messaging/firebase_messaging.dart';

import 'push_notification_service.dart';

/// A [PushTokenSource] backed by the Firebase Cloud Messaging plugin.
class FirebasePushTokenSource implements PushTokenSource {
  FirebasePushTokenSource(this._messaging);

  final FirebaseMessaging _messaging;

  @override
  Future<String?> getToken() async {
    final settings = await _messaging.requestPermission();
    if (settings.authorizationStatus != AuthorizationStatus.authorized) {
      return null;
    }
    return _messaging.getToken();
  }

  @override
  Stream<String> get onTokenRefresh => _messaging.onTokenRefresh;
}
