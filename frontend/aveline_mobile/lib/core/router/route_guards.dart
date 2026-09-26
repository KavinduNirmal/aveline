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

  /// Shown when a signed-in user's profile could not be loaded, so the account
  /// state is unknown and no other screen can be chosen honestly.
  static const String connection = '/connection';
  static const String catalog = '/catalog';

  /// The catalog's search filter options, pushed from the catalog screen.
  static const String catalogFilters = '/catalog/filters';

  /// Route pattern for a single piece. The location is built with
  /// [catalogProduct]; the static `filters` segment above must stay declared
  /// before this pattern so it is not swallowed as a product id.
  static const String catalogProductPattern = '/catalog/:productId';

  /// A single piece, addressed by its own id.
  static String catalogProduct(String productId) => '/catalog/$productId';

  /// The boutique's client book.
  static const String customers = '/customers';

  /// Route pattern for one client's profile. The location is built with
  /// [customer]; the static `customers` segment above must stay declared before
  /// this pattern so it is not swallowed as a client id.
  static const String customerPattern = '/customers/:customerId';

  /// One client, addressed by their own id.
  static String customer(String customerId) => '/customers/$customerId';
  static const String conversations = '/conversations';

  /// Route pattern for one client thread, opened from somewhere other than the inbox (a
  /// notification today).
  ///
  /// The message to open on travels as a query parameter rather than `extra`, because the
  /// router re-parses its location whenever the auth or profile listenable fires and `extra`
  /// does not survive that.
  static const String threadPattern =
      '/conversations/thread/:conversationId';

  /// One client thread, optionally anchored to the message the caller was sent to.
  static String thread(String conversationId, {String? messageId}) =>
      messageId == null || messageId.isEmpty
      ? '/conversations/thread/$conversationId'
      : '/conversations/thread/$conversationId?messageId=$messageId';

  /// The associate's own account and preferences.
  static const String settings = '/settings';

  /// The retired Profile screen's path.
  ///
  /// The account it showed is a section of [settings] now. The path stays
  /// declared because the header's avatar and any stored link still name it, so
  /// the router forwards it rather than answering with nothing.
  static const String profile = '/profile';
  static const String notifications = '/notifications';

  /// Boutique commerce orders register.
  static const String orders = '/orders';

  /// Floor associate on-the-fly order creation.
  static const String createOrder = '/orders/create';

  /// Route pattern for a single order details screen.
  static const String orderDetailPattern = '/orders/:orderId';

  /// A single order, addressed by its own id.
  static String orderDetail(String orderId) => '/orders/$orderId';
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
  ///
  /// [profileFailed] reports that loading the signed-in user's profile failed,
  /// which leaves [accountState] unknown. Without it the guards would hold the
  /// user on the first onboarding screen with no explanation and no way to
  /// retry, which is indistinguishable from being stuck.
  static String? redirectForAuth(
    String matchedLocation, {
    required bool isSignedIn,
    bool? hasCompletedOnboarding,
    String? accountState,
    String? accountType,
    bool profileFailed = false,
  }) {
    final atAuthScreen = matchedLocation == AppRoutes.auth;
    final atAccountTypeScreen = matchedLocation == AppRoutes.accountType;
    final atOnboardingScreen = matchedLocation == AppRoutes.onboarding;
    final atOwnerOnboardingScreen = matchedLocation == AppRoutes.ownerOnboarding;
    final atOrgSetupScreen = matchedLocation == AppRoutes.orgSetup;
    final atSuspendedScreen = matchedLocation == AppRoutes.suspended;
    final atInviteScreen = matchedLocation == AppRoutes.invite;
    final atConnectionScreen = matchedLocation == AppRoutes.connection;

    if (!isSignedIn) {
      // Unauthenticated users may only reach the auth screen (and the invite
      // deep link, which is handled by the invite flow before sign-in).
      if (atAuthScreen || atInviteScreen) {
        return null;
      }
      return AppRoutes.auth;
    }

    if (profileFailed) {
      return atConnectionScreen ? null : AppRoutes.connection;
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
          atSuspendedScreen ||
          atConnectionScreen) {
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
