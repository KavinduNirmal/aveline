import 'dart:async';

import 'device_token_api.dart';

/// Abstraction over the FCM token source so the push service can be unit tested
/// without the Firebase plugin.
abstract interface class PushTokenSource {
  /// Requests notification permission and returns the FCM registration token,
  /// or `null` when permission was denied / unavailable.
  Future<String?> getToken();

  /// Emits a new FCM token whenever Firebase rotates it.
  Stream<String> get onTokenRefresh;
}

/// Registers the device's FCM token with the backend and keeps it in sync when the
/// token rotates. On sign-out, [unregister] removes the token so the user stops
/// receiving pushes on this device.
class PushNotificationService {
  PushNotificationService(this._tokenSource, this._api, this._platform);

  final PushTokenSource _tokenSource;
  final DeviceTokenApi _api;
  final String _platform;

  StreamSubscription<String>? _refreshSub;
  String? _currentToken;

  /// Requests permission, registers the current token, and subscribes to rotations.
  Future<void> initialize() async {
    final token = await _tokenSource.getToken();
    if (token != null && token.isNotEmpty) {
      await _register(token);
    }
    _refreshSub ??= _tokenSource.onTokenRefresh.listen((newToken) {
      if (newToken.isNotEmpty) {
        _register(newToken);
      }
    });
  }

  /// Removes the registered token (called on sign-out).
  Future<void> unregister() async {
    await _refreshSub?.cancel();
    _refreshSub = null;
    final token = _currentToken;
    _currentToken = null;
    if (token != null && token.isNotEmpty) {
      try {
        await _api.unregister(token);
      } catch (_) {
        // Best-effort: the session is ending anyway.
      }
    }
  }

  Future<void> _register(String token) async {
    _currentToken = token;
    try {
      await _api.register(token, _platform);
    } catch (_) {
      // Best-effort: a failed registration is retried on the next token refresh.
    }
  }
}
