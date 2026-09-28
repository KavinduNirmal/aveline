import 'package:firebase_messaging/firebase_messaging.dart';

import 'push_message_handler.dart';

/// A [PushMessageSource] backed by the Firebase Cloud Messaging plugin.
///
/// The plugin's `RemoteMessage.data` is the string map the API's FCM `data`
/// contract describes: the notification's own keys plus `type` and
/// `notificationId`.
class FirebasePushMessageSource implements PushMessageSource {
  FirebasePushMessageSource(this._messaging);

  final FirebaseMessaging _messaging;

  @override
  Stream<Map<String, dynamic>> get onMessage =>
      FirebaseMessaging.onMessage.map((message) => message.data);

  @override
  Stream<Map<String, dynamic>> get onMessageOpenedApp =>
      FirebaseMessaging.onMessageOpenedApp.map((message) => message.data);

  @override
  Future<Map<String, dynamic>?> getInitialMessage() async =>
      (await _messaging.getInitialMessage())?.data;
}
