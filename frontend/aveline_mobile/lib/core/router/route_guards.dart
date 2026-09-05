import 'package:go_router/go_router.dart';

import '../../features/auth/domain/aveline_user.dart';

/// App-wide route paths. Kept in one place so guards and navigation agree.
abstract final class AppRoutes {
  static const String home = '/';
  static const String auth = '/auth';
  static const String onboarding = '/onboarding';
  static const String orgSetup = '/org-setup';
  static const String suspended = '/suspended';
}

/// Pure auth/account-state redirect rules for the [GoRouter].
abstract final class RouteGuards {
  /// Redirects based on sign-in status and account lifecycle state. Returns
  /// `null` when no redirect is needed.
  ///
  /// Takes only [matchedLocation] plus plain values so the rule stays free of
  /// router internals and is trivially testable. When [accountState] is not yet
  /// known (`null`) the legacy [hasCompletedOnboarding] flag drives profile
  /// routing only.
  static String? redirectForAuth(
    String matchedLocation, {
    required bool isSignedIn,
    bool? hasCompletedOnboarding,
    String? accountState,
  }) {
    final atAuthScreen = matchedLocation == AppRoutes.auth;
    final atOnboardingScreen = matchedLocation == AppRoutes.onboarding;
    final atOrgSetupScreen = matchedLocation == AppRoutes.orgSetup;
    final atSuspendedScreen = matchedLocation == AppRoutes.suspended;

    if (!isSignedIn) {
      return atAuthScreen ? null : AppRoutes.auth;
    }

    final state = accountState != null
        ? AvelineAccountState.parse(
            accountState,
            hasCompletedOnboarding: hasCompletedOnboarding ?? false,
            isActive: accountState != 'Suspended',
            hasOrgContext: true,
          )
        : null;

    if (state == AvelineAccountState.suspended) {
      return atSuspendedScreen ? null : AppRoutes.suspended;
    }

    if (state == AvelineAccountState.active) {
      if (atAuthScreen ||
          atOnboardingScreen ||
          atOrgSetupScreen ||
          atSuspendedScreen) {
        return AppRoutes.home;
      }
      return null;
    }

    if (state == AvelineAccountState.onboardingPending) {
      if (hasCompletedOnboarding == false) {
        return atOnboardingScreen ? null : AppRoutes.onboarding;
      }
      return atOrgSetupScreen ? null : AppRoutes.orgSetup;
    }

    // Account state not yet known (profile still loading): route on the legacy flag.
    if (hasCompletedOnboarding == false) {
      return atOnboardingScreen ? null : AppRoutes.onboarding;
    }
    if (hasCompletedOnboarding == true) {
      if (atAuthScreen ||
          atOnboardingScreen ||
          atOrgSetupScreen ||
          atSuspendedScreen) {
        return AppRoutes.home;
      }
      return null;
    }

    // Signed in but the profile is still loading: avoid a redirect loop.
    return null;
  }
}
