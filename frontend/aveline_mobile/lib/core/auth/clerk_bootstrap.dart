import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:flutter/foundation.dart';

/// Progress of the Clerk bootstrap that gates the rest of the app.
enum AvelineBootStatus {
  /// Creating the auth state, and holding the opening screen for at least the
  /// length of its entrance animation.
  preparing,

  /// The auth state exists; the opening screen is showing its closing line
  /// before handing off to the router.
  ready,

  /// Creating the auth state failed even after dropping the stored session.
  failed,
}

/// A bootstrap failure, classified so the opening screen can say something
/// true about it.
@immutable
class ClerkBootstrapFailure {
  const ClerkBootstrapFailure({required this.isNetwork, required this.detail});

  /// Whether the underlying error looks like a transport problem, which is
  /// worth telling the user to fix, rather than a problem with the app.
  final bool isNetwork;

  /// The underlying error's text, for logs and the debug-only detail line.
  final String detail;

  @override
  String toString() =>
      'ClerkBootstrapFailure(isNetwork: $isNetwork, detail: $detail)';
}

/// Creates the [ClerkAuthState] the app runs on, recovering from a stored
/// session that Clerk will not accept.
///
/// `ClerkAuthState.create` finishes initialising by polling for a session token
/// whenever a session was restored from storage. When that request fails, which
/// is what happens with no network, a DNS failure, or a session revoked from
/// another device, the SDK reports it by throwing out of `initialize()`. The
/// caller is then left with no auth state at all and the app never leaves its
/// opening screen.
///
/// The failure belongs to the stored session, so dropping it and trying once
/// more starts from a signed-out client, which initialises without touching the
/// network. Throws the second attempt's error when that does not help.
///
/// [attemptTimeout] bounds each attempt. The SDK awaits its session-token poll
/// during `initialize`, and on a network whose lookups never answer (a dead
/// hotspot, DNS that times out) that await can block far longer than any user
/// will wait. Bounding it also makes the recovery work: the attempt that would
/// hang is the one that polls for a token, which is the attempt whose session
/// gets cleared before the retry.
Future<ClerkAuthState> createAuthStateWithRecovery({
  required ClerkAuthConfig config,
  required Future<void> Function() clearSession,
  Duration? attemptTimeout,
  void Function(Object error, StackTrace stackTrace)? onStaleSession,
}) {
  return withSessionReset<ClerkAuthState>(
    create: () => ClerkAuthState.create(config: config),
    clearSession: clearSession,
    attemptTimeout: attemptTimeout,
    onStaleSession: onStaleSession,
  );
}

/// Runs [create], and when it fails clears the stored session and runs it once
/// more.
///
/// Returns the first success. Throws the last failure, so a second attempt that
/// fails reports its own error rather than the one that triggered the recovery.
@visibleForTesting
Future<T> withSessionReset<T>({
  required Future<T> Function() create,
  required Future<void> Function() clearSession,
  Duration? attemptTimeout,
  void Function(Object error, StackTrace stackTrace)? onStaleSession,
}) async {
  Future<T> attempt() =>
      attemptTimeout == null ? create() : create().timeout(attemptTimeout);

  try {
    return await attempt();
  } catch (error, stackTrace) {
    onStaleSession?.call(error, stackTrace);
    await clearSession();
    return attempt();
  }
}

/// Classifies [error] for the retry screen's copy.
///
/// The SDK surfaces transport failures wrapped (`ClientException with
/// SocketException`), so the type test is on the whole message rather than on
/// the exception's own class.
ClerkBootstrapFailure describeBootstrapFailure(Object error) {
  final text = error.toString().toLowerCase();
  const networkMarkers = [
    'socketexception',
    'clientexception',
    'failed host lookup',
    'connection refused',
    'connection closed',
    'connection reset',
    'network is unreachable',
    'timeoutexception',
    'connectiontimeout',
    'connectionerror',
    'sendtimeout',
    'receivetimeout',
    'badcertificate',
    'software caused connection abort',
  ];
  return ClerkBootstrapFailure(
    isNetwork: networkMarkers.any(text.contains),
    detail: error.toString(),
  );
}
