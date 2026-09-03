import 'package:go_router/go_router.dart';

/// App-wide route paths. Kept in one place so guards and navigation agree.
abstract final class AppRoutes {
  static const String home = '/';
  static const String auth = '/auth';
  static const String onboarding = '/onboarding';
}

/// Pure auth-based redirect rules for the [GoRouter].
abstract final class RouteGuards {
  /// Redirects unauthenticated users to the sign-in screen and signed-in
  /// users away from the sign-in screen or into onboarding if not yet completed.
  /// Returns `null` when no redirect is needed.
  ///
  /// Takes only [matchedLocation] (a `GoRouterState` string) so the rule stays
  /// free of router internals and is trivially testable.
  static String? redirectForAuth(
    String matchedLocation, {
    required bool isSignedIn,
    bool? hasCompletedOnboarding,
  }) {
    final atAuthScreen = matchedLocation == AppRoutes.auth;
    final atOnboardingScreen = matchedLocation == AppRoutes.onboarding;

    if (!isSignedIn && !atAuthScreen) {
      return AppRoutes.auth;
    }
    if (isSignedIn) {
      if (hasCompletedOnboarding == false) {
        if (!atOnboardingScreen) {
          return AppRoutes.onboarding;
        }
        return null;
      }

      if (atAuthScreen || (hasCompletedOnboarding == true && atOnboardingScreen)) {
        return AppRoutes.home;
      }
    }
    return null;
  }
}
