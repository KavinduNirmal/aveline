import 'package:go_router/go_router.dart';

import '../../features/auth/domain/aveline_user.dart';

/// App-wide route paths. Kept in one place so guards and navigation agree.
abstract final class AppRoutes {
  static const String home = '/';
  static const String auth = '/auth';
  static const String accountType = '/account-type';
  static const String onboarding = '/onboarding';
  static const String ownerOnboarding = '/owner-onboarding';
  static const String orgSetup = '/org-setup';
  static const String suspended = '/suspended';
  static const String invite = '/invite';
}

/// Pure auth/account-state redirect rules for the [GoRouter].
abstract final class RouteGuards {
  /// Redirects based on sign-in status and account lifecycle state. Returns
  /// `null` when no redirect is needed.
  ///
  /// Takes only [matchedLocation] plus plain values so the rule stays free of
  /// router internals and is trivially testable. When [accountState] is not yet
  /// known (`null`) the legacy [hasCompletedOnboarding] flag drives profile
  /// routing only. [accountType] is the locally persisted onboarding path
  /// (`owner`/`staff`/`null`) used to branch a pending account.
  static String? redirectForAuth(
    String matchedLocation, {
    required bool isSignedIn,
    bool? hasCompletedOnboarding,
    String? accountState,
    String? accountType,
  }) {
    final atAuthScreen = matchedLocation == AppRoutes.auth;
    final atAccountTypeScreen = matchedLocation == AppRoutes.accountType;
    final atOnboardingScreen = matchedLocation == AppRoutes.onboarding;
    final atOwnerOnboardingScreen = matchedLocation == AppRoutes.ownerOnboarding;
    final atOrgSetupScreen = matchedLocation == AppRoutes.orgSetup;
    final atSuspendedScreen = matchedLocation == AppRoutes.suspended;
    final atInviteScreen = matchedLocation == AppRoutes.invite;

    if (!isSignedIn) {
      // Unauthenticated users may only reach the auth screen (and the invite
      // deep link, which is handled by the invite flow before sign-in).
      if (atAuthScreen || atInviteScreen) {
        return null;
      }
      return AppRoutes.auth;
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
          atAccountTypeScreen ||
          atOnboardingScreen ||
          atOwnerOnboardingScreen ||
          atOrgSetupScreen ||
          atSuspendedScreen) {
        return AppRoutes.home;
      }
      return null;
    }

    if (state == AvelineAccountState.onboardingPending) {
      final target = _pendingOnboardingTarget(
        hasCompletedOnboarding: hasCompletedOnboarding ?? false,
        accountType: accountType,
      );
      if (matchedLocation == target) {
        return null;
      }
      return target;
    }

    // Account state not yet known (profile still loading): route on the legacy flag.
    if (hasCompletedOnboarding == false) {
      final target = _pendingOnboardingTarget(
        hasCompletedOnboarding: false,
        accountType: accountType,
      );
      return matchedLocation == target ? null : target;
    }
    if (hasCompletedOnboarding == true) {
      if (atAuthScreen ||
          atAccountTypeScreen ||
          atOnboardingScreen ||
          atOwnerOnboardingScreen ||
          atOrgSetupScreen ||
          atSuspendedScreen) {
        return AppRoutes.home;
      }
      return null;
    }

    // Signed in but the profile is still loading: avoid a redirect loop.
    return null;
  }

  /// The screen a pending (not-yet-active) account should be on, given whether
  /// their profile is complete and which onboarding path they chose.
  static String _pendingOnboardingTarget({
    required bool hasCompletedOnboarding,
    String? accountType,
  }) {
    if (!hasCompletedOnboarding) {
      // Profile not completed: ask for the account type first, then the profile.
      return accountType == null ? AppRoutes.accountType : AppRoutes.onboarding;
    }
    // Profile completed but no active membership yet: branch by account type.
    // Owners continue the boutique wizard; staff (and legacy/unknown) join via
    // the invitation code.
    return accountType == 'owner' ? AppRoutes.ownerOnboarding : AppRoutes.orgSetup;
  }
}
