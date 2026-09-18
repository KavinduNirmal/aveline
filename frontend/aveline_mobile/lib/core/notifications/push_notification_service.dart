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

/// A [PushTokenSource] used when Firebase could not be initialized: it reports no
/// token and never emits refreshes, so push notifications are simply disabled.
class NoopPushTokenSource implements PushTokenSource {
  const NoopPushTokenSource();

  @override
  Future<String?> getToken() async => null;

  @override
  Stream<String> get onTokenRefresh => const Stream.empty();
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
  Future<void>? _initFuture;
  bool _initialized = false;

  /// Requests permission, registers the current token, and subscribes to rotations.
  ///
  /// Safe to call more than once: repeated or overlapping calls share a single
  /// run. `initState` and the Clerk auth listener can both fire during startup,
  /// and `FirebaseMessaging.requestPermission()` throws
  /// `[firebase_messaging/unknown] A request for permissions is already running`
  /// when two requests overlap.
  Future<void> initialize() {
    if (_initialized) {
      return Future<void>.value();
    }
    return _initFuture ??= _initialize().whenComplete(() {
      _initFuture = null;
      _initialized = true;
    });
  }

  Future<void> _initialize() async {
    try {
      final token = await _tokenSource.getToken();
      if (token != null && token.isNotEmpty) {
        await _register(token);
      }
    } catch (_) {
      // Best-effort: permission may be denied or unavailable, or a request may
      // already be in flight. Push stays disabled instead of throwing.
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
    // Allow a later sign-in to request permission and register again.
    _initialized = false;
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
