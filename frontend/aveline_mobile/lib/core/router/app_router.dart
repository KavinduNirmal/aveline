import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../features/auth/domain/auth_repository.dart';
import '../../features/auth/domain/aveline_user.dart';
import '../../features/auth/presentation/screens/auth_screen.dart';
import '../../features/catalog/data/catalog_product_repository.dart';
import '../../features/catalog/domain/catalog_filters.dart';
import '../../features/catalog/presentation/screens/catalog_filter_screen.dart';
import '../../features/catalog/presentation/screens/catalog_product_screen.dart';
import '../../features/catalog/presentation/screens/catalog_screen.dart';
import '../../features/commerce/domain/repositories/commerce_repository.dart';
import '../../features/commerce/presentation/screens/create_order_screen.dart';
import '../../features/commerce/presentation/screens/order_detail_screen.dart';
import '../../features/commerce/presentation/screens/orders_list_screen.dart';
import '../../features/conversations/data/conversation_repository.dart';
import '../../features/conversations/data/thread_repository.dart';
import '../../features/conversations/presentation/screens/conversations_screen.dart';
import '../../features/conversations/presentation/screens/thread_route_screen.dart';
import '../../features/customers/data/customer_repository.dart';
import '../../features/customers/presentation/screens/customer_screen.dart';
import '../../features/customers/presentation/screens/customers_screen.dart';
import '../../features/home/presentation/screens/main_shell.dart';
import '../../features/notifications/presentation/screens/notifications_screen.dart';
import '../../features/onboarding/presentation/screens/account_type_screen.dart';
import '../../features/onboarding/presentation/screens/invite_screen.dart';
import '../../features/onboarding/presentation/screens/onboarding_screen.dart';
import '../../features/onboarding/presentation/screens/org_setup_screen.dart';
import '../../features/onboarding/presentation/screens/owner_onboarding_screen.dart';
import '../../features/onboarding/presentation/screens/suspended_screen.dart';
import '../../features/settings/presentation/screens/settings_screen.dart';
import '../../shared/widgets/aveline_loading_screen.dart';
import '../auth/clerk_bootstrap.dart';
import '../auth/permission_guard.dart';
import '../auth/permissions.dart';
import '../providers/onboarding_provider.dart';
import '../providers/user_provider.dart';
import 'route_guards.dart';

/// The app's router: one place that owns the route table and the redirect that
/// guards it.
///
/// This is deliberately a plain factory over the providers and repositories it
/// needs rather than a method on the shell's `State`: the shell is only
/// reachable behind an initialised Clerk session, so a test could not otherwise
/// pump the *real* router. Production constructs it with [build] from
/// `AvelineAppShell`; a widget test can construct the same router with fakes and
/// assert where a navigation actually lands.
abstract final class AppRouter {
  /// Where the app opens for the account state the providers describe.
  static String initialLocation({
    required AuthRepository authRepository,
    required UserProvider userProvider,
    required OnboardingProvider onboardingProvider,
  }) {
    if (!authRepository.isSignedIn) {
      return AppRoutes.auth;
    }
    final state = userProvider.accountState;
    if (state == AvelineAccountState.suspended) {
      return AppRoutes.suspended;
    }
    if (state == AvelineAccountState.active) {
      return AppRoutes.home;
    }
    if (!userProvider.hasCompletedOnboarding) {
      return onboardingProvider.accountType == null
          ? AppRoutes.accountType
          : AppRoutes.onboarding;
    }
    return onboardingProvider.isOwner
        ? AppRoutes.ownerOnboarding
        : AppRoutes.orgSetup;
  }

  /// Builds the real router.
  ///
  /// [refreshListenable] re-runs the redirect whenever the auth or account state
  /// changes; production merges the Clerk auth state with both providers, while a
  /// test that has no Clerk session passes the providers alone.
  static GoRouter build({
    required AuthRepository authRepository,
    required UserProvider userProvider,
    required OnboardingProvider onboardingProvider,
    required CatalogProductRepository catalogRepository,
    required CustomerRepository customerRepository,
    required ConversationRepository conversationRepository,
    required ThreadRepository threadRepository,
    required CommerceRepository commerceRepository,
    required Listenable refreshListenable,
    required String initialLocation,
  }) {
    return GoRouter(
      initialLocation: initialLocation,
      refreshListenable: refreshListenable,
      redirect: (context, state) {
        final result = RouteGuards.redirectForAuth(
          state.matchedLocation,
          isSignedIn: authRepository.isSignedIn,
          hasCompletedOnboarding: authRepository.isSignedIn
              ? userProvider.hasCompletedOnboarding
              : null,
          accountState: authRepository.isSignedIn
              ? userProvider.accountState?.wireValue
              : null,
          accountType: authRepository.isSignedIn
              ? onboardingProvider.accountType?.wireValue
              : null,
          // A signed-in profile load can still fail after the bootstrap, for
          // instance when signing in from the auth screen. Without this the
          // guards would park the user on the account-type picker with no
          // explanation and no way to retry.
          //
          // Only when no profile is held: the auth listener refetches the
          // profile periodically, and a background refresh that fails must not
          // pull a user who is already using the app onto the retry screen.
          profileFailed: authRepository.isSignedIn &&
              userProvider.user == null &&
              userProvider.hasLoadFailed,
        );
        debugPrint(
          '[router] ${state.matchedLocation} signedIn=${authRepository.isSignedIn} '
          'onboarded=${userProvider.hasCompletedOnboarding} '
          'state=${userProvider.accountState?.wireValue} '
          'type=${onboardingProvider.accountType?.wireValue} -> $result',
        );
        return result;
      },
      routes: [
        GoRoute(
          path: AppRoutes.home,
          name: 'home',
          builder: (context, state) => const MainShell(),
        ),
        GoRoute(
          path: AppRoutes.catalog,
          name: 'catalog',
          builder: (context, state) => PermissionGuard(
            permission: Permissions.catalogView,
            child: MainShell(
              child: CatalogScreen(repository: catalogRepository),
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.catalogFilters,
          name: 'catalogFilters',
          // A full-screen editor rather than a panel: it is reached from the
          // field row, returns its draft to the catalog, and carries its own
          // back affordance so it does not need the shell's header.
          builder: (context, state) {
            final initial = state.extra is CatalogFilters
                ? state.extra as CatalogFilters
                : (state.uri.queryParameters.isNotEmpty
                    ? CatalogFilters.fromQueryParameters(state.uri.queryParameters)
                    : null);

            return PermissionGuard(
              permission: Permissions.catalogView,
              child: CatalogFilterScreen(initial: initial),
            );
          },
        ),
        GoRoute(
          path: AppRoutes.catalogProductPattern,
          name: 'catalogProduct',
          // Declared after the static `/catalog/filters` route so that segment
          // is not read as a product id.
          //
          // The piece is deliberately not passed as `extra`: the router
          // re-parses its location whenever the auth or profile listenable
          // fires, and `extra` does not survive that, so the screen resolves
          // the piece from the id the location already carries.
          builder: (context, state) => PermissionGuard(
            permission: Permissions.catalogView,
            child: CatalogProductScreen(
              productId: state.pathParameters['productId'] ?? '',
              repository: catalogRepository,
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.customers,
          name: 'customers',
          builder: (context, state) => PermissionGuard(
            permission: Permissions.customersView,
            child: MainShell(
              child: CustomersScreen(repository: customerRepository),
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.customerPattern,
          name: 'customer',
          // Declared after the static `/customers` route so that segment is
          // not read as a client id.
          //
          // The client is deliberately not passed as `extra`: the router
          // re-parses its location whenever the auth or profile listenable
          // fires, and `extra` does not survive that, so the screen resolves the
          // profile from the id the location already carries.
          builder: (context, state) => PermissionGuard(
            permission: Permissions.customersView,
            child: CustomerScreen(
              customerId: state.pathParameters['customerId'] ?? '',
              repository: customerRepository,
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.conversations,
          name: 'conversations',
          builder: (context, state) => PermissionGuard(
            permission: Permissions.conversationsView,
            child: MainShell(
              child: ConversationsScreen(
                repository: conversationRepository,
                threadRepository: threadRepository,
              ),
            ),
          ),
        ),
        GoRoute(
          // A notification knows a thread only by its id, so the row is read before the thread
          // screen is shown. The anchored message travels as a query parameter: the router
          // re-parses its location and `extra` does not survive that.
          path: AppRoutes.threadPattern,
          name: 'thread',
          builder: (context, state) => ThreadRouteScreen(
            conversationId: state.pathParameters['conversationId'] ?? '',
            messageId: state.uri.queryParameters['messageId'],
            conversationRepository: conversationRepository,
            threadRepository: threadRepository,
          ),
        ),
        GoRoute(
          path: AppRoutes.settings,
          name: 'settings',
          builder: (context, state) => const MainShell(child: SettingsScreen()),
        ),
        GoRoute(
          // Profile was merged into Settings, so the old path forwards rather than
          // serving a second screen with the same content on it. The header's
          // avatar and any stored link still name it.
          path: AppRoutes.profile,
          redirect: (context, state) => AppRoutes.settings,
        ),
        GoRoute(
          path: AppRoutes.notifications,
          name: 'notifications',
          builder: (context, state) => const MainShell(
            child: NotificationsScreen(),
          ),
        ),
        GoRoute(
          path: AppRoutes.orders,
          name: 'orders',
          builder: (context, state) => MainShell(
            child: OrdersListScreen(repository: commerceRepository),
          ),
        ),
        GoRoute(
          path: AppRoutes.createOrder,
          name: 'createOrder',
          builder: (context, state) => CreateOrderScreen(
            commerceRepository: commerceRepository,
            catalogRepository: catalogRepository,
          ),
        ),
        GoRoute(
          path: AppRoutes.orderDetailPattern,
          name: 'orderDetail',
          builder: (context, state) => OrderDetailScreen(
            orderId: state.pathParameters['orderId'] ?? '',
            repository: commerceRepository,
          ),
        ),
        GoRoute(
          path: AppRoutes.auth,
          name: 'auth',
          builder: (context, state) => const AuthScreen(),
        ),
        GoRoute(
          path: AppRoutes.connection,
          name: 'connection',
          // Reuses the opening screen so a failed profile load looks and reads
          // exactly like a failed start, retry included.
          builder: (context, state) => Consumer<UserProvider>(
            builder: (context, userProvider, _) => AvelineLoadingScreen(
              status: userProvider.isLoading
                  ? AvelineBootStatus.preparing
                  : AvelineBootStatus.failed,
              failure: describeBootstrapFailure(
                userProvider.errorMessage ?? '',
              ),
              onRetry: userProvider.isLoading
                  ? null
                  : () => userProvider.fetchUser(context.read<Dio>()),
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.accountType,
          name: 'accountType',
          builder: (context, state) => const AccountTypeScreen(),
        ),
        GoRoute(
          path: AppRoutes.onboarding,
          name: 'onboarding',
          builder: (context, state) => const OnboardingScreen(),
        ),
        GoRoute(
          path: AppRoutes.ownerOnboarding,
          name: 'ownerOnboarding',
          builder: (context, state) => const OwnerOnboardingScreen(),
        ),
        GoRoute(
          path: AppRoutes.orgSetup,
          name: 'orgSetup',
          builder: (context, state) => const OrgSetupScreen(),
        ),
        GoRoute(
          path: AppRoutes.suspended,
          name: 'suspended',
          builder: (context, state) => const SuspendedScreen(),
        ),
        GoRoute(
          path: AppRoutes.invite,
          name: 'invite',
          builder: (context, state) => const InviteScreen(),
        ),
      ],
    );
  }
}
