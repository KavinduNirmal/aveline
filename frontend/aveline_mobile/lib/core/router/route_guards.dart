import 'package:go_router/go_router.dart';

/// App-wide route paths. Kept in one place so guards and navigation agree.
abstract final class AppRoutes {
  static const String home = '/';
  static const String auth = '/auth';
}

/// Pure auth-based redirect rules for the [GoRouter].
abstract final class RouteGuards {
  /// Redirects unauthenticated users to the sign-in screen and signed-in
  /// users away from the sign-in screen. Returns `null` when no redirect is
  /// needed.
  static String? redirectForAuth(
    GoRouterState state, {
    required bool isSignedIn,
  }) {
    final atAuthScreen = state.matchedLocation == AppRoutes.auth;

    if (!isSignedIn && !atAuthScreen) {
      return AppRoutes.auth;
    }
    if (isSignedIn && atAuthScreen) {
      return AppRoutes.home;
    }
    return null;
  }
}
