import 'dart:async';
import 'dart:io';

import 'package:app_links/app_links.dart';
import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:dio/dio.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/auth/clerk_bootstrap.dart';
import 'core/auth/clerk_session_persistor.dart';
import 'core/config/app_config.dart';
import 'core/network/api_client.dart';
import 'core/network/auth_token_provider.dart';
import 'core/notifications/device_token_api.dart';
import 'core/notifications/firebase_push_message_source.dart';
import 'core/notifications/firebase_push_token_source.dart';
import 'core/notifications/notification_payload.dart';
import 'core/notifications/notification_provider.dart';
import 'core/notifications/push_message_handler.dart';
import 'core/notifications/push_notification_service.dart';
import 'core/notifications/realtime_connection_factory.dart';
import 'core/notifications/realtime_notification_service.dart';
import 'core/providers/boutique_provider.dart';
import 'core/providers/onboarding_provider.dart';
import 'core/providers/owner_onboarding_provider.dart';
import 'core/providers/user_provider.dart';
import 'core/router/route_guards.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/data/clerk_auth_repository.dart';
import 'features/auth/domain/auth_repository.dart';
import 'features/auth/domain/aveline_user.dart';
import 'features/auth/presentation/screens/auth_screen.dart';
import 'core/auth/permission_guard.dart';
import 'core/auth/permissions.dart';
import 'features/catalog/data/api_catalog_product_repository.dart';
import 'features/catalog/data/catalog_product_repository.dart';
import 'features/catalog/domain/catalog_filters.dart';
import 'features/catalog/presentation/screens/catalog_filter_screen.dart';
import 'features/catalog/presentation/screens/catalog_product_screen.dart';
import 'features/catalog/presentation/screens/catalog_screen.dart';
import 'features/conversations/data/api_conversation_repository.dart';
import 'features/conversations/data/api_thread_repository.dart';
import 'features/conversations/data/conversation_repository.dart';
import 'features/conversations/data/thread_repository.dart';
import 'features/conversations/presentation/screens/conversations_screen.dart';
import 'features/conversations/presentation/screens/thread_route_screen.dart';
import 'features/customers/data/customer_repository.dart';
import 'features/customers/data/demo_customer_repository.dart';
import 'features/customers/presentation/screens/customer_screen.dart';
import 'features/customers/presentation/screens/customers_screen.dart';
import 'features/home/data/api_home_repository.dart';
import 'features/home/presentation/home_controller.dart';
import 'features/home/presentation/screens/main_shell.dart';
import 'features/notifications/data/api_notification_repository.dart';
import 'features/notifications/presentation/notifications_controller.dart';
import 'features/notifications/presentation/screens/notifications_screen.dart';
import 'features/settings/presentation/screens/settings_screen.dart';
import 'features/onboarding/data/onboarding_preferences.dart';
import 'features/onboarding/data/owner_onboarding_api.dart';
import 'features/onboarding/presentation/screens/account_type_screen.dart';
import 'features/onboarding/presentation/screens/invite_screen.dart';
import 'features/onboarding/presentation/screens/onboarding_screen.dart';
import 'features/onboarding/presentation/screens/org_setup_screen.dart';
import 'features/onboarding/presentation/screens/owner_onboarding_screen.dart';
import 'features/onboarding/presentation/screens/suspended_screen.dart';
import 'shared/widgets/aveline_loading_screen.dart';

/// Root widget: bootstraps Clerk behind an opening screen, wires DI, and
/// configures routing.
class AvelineApp extends StatefulWidget {
  const AvelineApp({
    super.key,
    required this.config,
    required this.preferences,
    required this.sessionStore,
  });

  final AppConfig config;
  final OnboardingPreferences preferences;

  /// Where the Clerk session is kept between launches.
  final ClerkSessionPersistor sessionStore;

  @override
  State<AvelineApp> createState() => _AvelineAppState();
}

class _AvelineAppState extends State<AvelineApp> {
  /// Shortest time the opening screen stays up, so the blossom always finishes
  /// unfurling even when Clerk answers immediately.
  static const Duration _minimumOpening = Duration(milliseconds: 1600);

  /// How long "Aveline is ready" stays up before the router takes over.
  static const Duration _readyLinger = Duration(milliseconds: 700);

  /// How long the opening screen will wait for the Clerk SDK to initialise
  /// before treating it as a failure.
  ///
  /// The SDK awaits a session-token poll during initialisation, and against a
  /// network whose lookups never answer that await can block indefinitely,
  /// which would leave the opening screen spinning forever. Bounding it routes
  /// the user into the drop-the-session retry instead.
  static const Duration _authAttemptTimeout = Duration(seconds: 10);

  late final ClerkAuthConfig _clerkConfig;
  ClerkAuthState? _authState;

  /// Built once the auth state exists, and reused on every retry. The profile
  /// load below needs them, and every post-auth route depends on the profile,
  /// so they belong to the bootstrap rather than to the shell.
  AuthRepository? _authRepository;
  Dio? _dio;
  UserProvider? _userProvider;

  AvelineBootStatus _status = AvelineBootStatus.preparing;
  ClerkBootstrapFailure? _failure;
  bool _handedOff = false;

  @override
  void initState() {
    super.initState();
    _clerkConfig = ClerkAuthConfig(
      publishableKey: widget.config.clerkPublishableKey,
      // Aveline owns the store, so a session Clerk refuses to accept can be
      // dropped here instead of stranding the app on the opening screen.
      persistor: widget.sessionStore,
    );
    unawaited(_bootstrap());
  }

  /// Brings the app up behind the opening screen, and is also the opening
  /// screen's retry action.
  ///
  /// The screen stays up until the account state is known, not merely until
  /// Clerk is ready. The router cannot choose between the home screen and an
  /// onboarding step while the account state is unknown, so handing off earlier
  /// parks a returning user on the account-type picker for as long as the
  /// profile takes to load.
  Future<void> _bootstrap() async {
    if (_status != AvelineBootStatus.preparing || _failure != null) {
      setState(() {
        _status = AvelineBootStatus.preparing;
        _failure = null;
      });
    }

    final startedAt = DateTime.now();
    final failure = await _prepare();

    // Hold the opening screen for at least the length of its entrance.
    final elapsed = DateTime.now().difference(startedAt);
    if (elapsed < _minimumOpening) {
      await Future<void>.delayed(_minimumOpening - elapsed);
    }
    if (!mounted) {
      return;
    }

    if (failure != null) {
      setState(() {
        _status = AvelineBootStatus.failed;
        _failure = failure;
      });
      return;
    }

    setState(() => _status = AvelineBootStatus.ready);
    await Future<void>.delayed(_readyLinger);
    if (!mounted) {
      return;
    }
    setState(() => _handedOff = true);
  }

  /// Creates whatever is still missing, returning the failure to show the user.
  ///
  /// Each step is skipped when it already succeeded, so a retry only repeats
  /// the part that failed.
  Future<ClerkBootstrapFailure?> _prepare() async {
    if (_authState == null) {
      try {
        final authState = await createAuthStateWithRecovery(
          config: _clerkConfig,
          clearSession: widget.sessionStore.clear,
          attemptTimeout: _authAttemptTimeout,
          onStaleSession: (error, _) => debugPrint(
            '[bootstrap] dropping the stored Clerk session after: $error',
          ),
        );
        final authRepository = ClerkAuthRepository(
          authState,
          jwtTemplateName: widget.config.jwtTemplateName,
        );
        _authState = authState;
        _authRepository = authRepository;
        _dio = ApiClientFactory.create(
          baseUrl: widget.config.apiBaseUrl,
          tokenProvider: authRepository,
        );
        _userProvider = UserProvider();
      } catch (error, stackTrace) {
        debugPrint('[bootstrap] could not start Clerk: $error\n$stackTrace');
        return describeBootstrapFailure(error);
      }
    }

    final authRepository = _authRepository!;
    final userProvider = _userProvider!;
    if (authRepository.isSignedIn && userProvider.user == null) {
      await userProvider.fetchUser(_dio!);
      if (userProvider.hasLoadFailed) {
        final detail = userProvider.errorMessage ?? 'unknown';
        debugPrint('[bootstrap] could not load the profile: $detail');
        return describeBootstrapFailure(detail);
      }
    }

    return null;
  }

  @override
  void dispose() {
    _userProvider?.dispose();
    _authState?.terminate();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final authState = _authState;
    if (authState == null || !_handedOff) {
      return MaterialApp(
        title: 'Aveline',
        theme: AppTheme.light,
        debugShowCheckedModeBanner: false,
        home: AvelineLoadingScreen(
          status: _status,
          failure: _failure,
          onRetry: _status == AvelineBootStatus.failed ? _bootstrap : null,
        ),
      );
    }

    return ClerkAuth(
      authState: authState,
      child: ClerkAuthBuilder(
        builder: (context, authState) => AvelineAppShell(
          config: widget.config,
          clerkAuthState: authState,
          preferences: widget.preferences,
          authRepository: _authRepository!,
          userProvider: _userProvider!,
          dio: _dio!,
        ),
      ),
    );
  }
}

/// Composition root: owns long-lived services and the router.
///
/// Built inside [ClerkAuthBuilder] because it needs the initialized
/// [ClerkAuthState] to drive the router's auth redirects.
class AvelineAppShell extends StatefulWidget {
  const AvelineAppShell({
    super.key,
    required this.config,
    required this.clerkAuthState,
    required this.preferences,
    required this.authRepository,
    required this.userProvider,
    required this.dio,
  });

  final AppConfig config;
  final ClerkAuthState clerkAuthState;
  final OnboardingPreferences preferences;

  /// Owned by the bootstrap, which loads the signed-in profile with [dio]
  /// before the shell exists.
  final AuthRepository authRepository;
  final UserProvider userProvider;
  final Dio dio;

  @override
  State<AvelineAppShell> createState() => _AvelineAppShellState();
}

class _AvelineAppShellState extends State<AvelineAppShell> {
  late final AuthRepository _authRepository;
  late final UserProvider _userProvider;
  late final BoutiqueProvider _boutiqueProvider;
  late final OnboardingProvider _onboardingProvider;
  late final OwnerOnboardingProvider _ownerOnboardingProvider;
  late final NotificationProvider _notificationProvider;
  late final NotificationsController _notificationsController;

  /// The inbox's API source, kept so a push tap can mark one notification read
  /// without the controller having to hold that row on screen.
  late final ApiNotificationRepository _notificationRepository;

  /// Handles FCM messages: a foreground arrival refreshes the inbox through the
  /// same seam realtime uses; a tap marks that notification read and routes.
  /// `null` when Firebase is unavailable or after sign-out.
  PushMessageHandler? _pushMessageHandler;

  /// One source for the Home tab's three data blocks (the focus deck, the client
  /// row and the Blossom meter), provided beside the other long-lived
  /// controllers so the screen and the shell's pull-to-refresh read one state.
  late final HomeController _homeController;
  late final PushNotificationService _pushNotificationService;
  late final RealtimeNotificationService _realtimeNotificationService;

  /// One paged source for the whole catalog, so the grid and the detail screen
  /// read the same pool rather than each building their own.
  late final CatalogProductRepository _catalogRepository;

  /// One source for the client book, for the same reason: the tab and whatever
  /// opens a client from it must read the same clients.
  late final CustomerRepository _customerRepository;

  /// One source for the message inbox, so the tab and whatever opens a thread
  /// from it read the same conversations.
  late final ConversationRepository _conversationRepository;

  /// One source for a client's thread, so the inbox's client rows and the thread
  /// screen they open read the same history.
  late final ThreadRepository _threadRepository;
  late final Dio _dio;
  late final GoRouter _router;
  final AppLinks _appLinks = AppLinks();
  StreamSubscription<Uri>? _appLinksSub;

  @override
  void initState() {
    super.initState();
    _authRepository = widget.authRepository;
    _userProvider = widget.userProvider;
    _dio = widget.dio;
    _boutiqueProvider = BoutiqueProvider();
    _onboardingProvider = OnboardingProvider(widget.preferences);
    _catalogRepository = ApiCatalogProductRepository(
      _dio,
      organizationId: () => _boutiqueProvider.organizationId,
    );
    _customerRepository = DemoCustomerRepository();
    // The inbox reads the API through one repository, the same way Home does. The
    // organization id is read at call time because it arrives with `/orgs/my`,
    // after this controller is built; until then the screen stays in its loading
    // state rather than reporting an error it does not have. The id is the active
    // membership's, never the JWT's `org_id` claim, which can be stale.
    _conversationRepository = ApiConversationRepository(
      _dio,
      organizationId: () => _boutiqueProvider.organizationId,
    );
    // The thread reads the same active membership's org id, read at call time for the same
    // reason: it arrives with `/orgs/my` after this shell is built. Until it does the thread
    // stays in its loading state, which is a "not yet" rather than an error.
    _threadRepository = ApiThreadRepository(
      _dio,
      organizationId: () => _boutiqueProvider.organizationId,
    );
    _ownerOnboardingProvider = OwnerOnboardingProvider(OwnerOnboardingApi(_dio));
    _notificationProvider = NotificationProvider();
    // Home reads the API through one repository: the derived focus feed, the
    // client highlights and the Blossom balance. The organization id is read at
    // call time because it arrives with `/orgs/my`, after this controller is
    // built; until then the screen stays in its loading state.
    _homeController = HomeController(
      ApiHomeRepository(_dio, organizationId: () => _boutiqueProvider.organizationId),
    );
    _notificationRepository = ApiNotificationRepository(_dio);
    _notificationsController = NotificationsController(
      // The inbox reads the live API. The endpoints are mapped (`Program.cs`) and
      // the route group is `/users/me`-shaped: it takes no organization id, so
      // the repository needs only the shared Dio.
      _notificationRepository,
    );
    // The header's badge and the inbox are the same number: the controller owns
    // it and reports it up, so a notification read on the tab clears the dot that
    // sent the associate there.
    _notificationsController.addListener(_syncUnreadBadge);
    _pushNotificationService = PushNotificationService(
      // Firebase is initialized best-effort in `main()`. When it is unavailable
      // (no `google-services.json`, no network at startup) fall back to a no-op
      // source so push is disabled instead of crashing the whole shell.
      Firebase.apps.isEmpty
          ? const NoopPushTokenSource()
          : FirebasePushTokenSource(FirebaseMessaging.instance),
      DioDeviceTokenApi(_dio),
      Platform.isIOS ? 'IOS' : 'Android',
    );
    _realtimeNotificationService = RealtimeNotificationService(
      defaultRealtimeConnectionFactory,
    );
    _router = _buildRouter();

    _syncOnboardingContext();
    if (_authRepository.isSignedIn) {
      // The bootstrap already loaded the profile for a restored session, so
      // only the connection and the boutique identity are left to fetch here.
      _startNotifications();
      _boutiqueProvider.fetchBoutique(_dio);
    }

    widget.clerkAuthState.addListener(_onAuthChanged);
    _listenForDeepLinks();
  }

  /// Handles invitation deep links (`aveline://invite?code=…`), including the
  /// initial link that launched the app.
  void _listenForDeepLinks() {
    _appLinksSub = _appLinks.uriLinkStream.listen(_handleDeepLink, onError: (_) {});
    _appLinks.getInitialLink().then((uri) {
      if (uri != null) _handleDeepLink(uri);
    });
  }

  void _handleDeepLink(Uri uri) {
    if (uri.scheme != 'aveline' || uri.host != 'invite') {
      return;
    }
    final code = uri.queryParameters['code'];
    _onboardingProvider.setPendingInviteCode(code);
    _router.go(AppRoutes.invite);
  }

  void _onAuthChanged() {
    if (_authRepository.isSignedIn) {
      _userProvider.fetchUser(_dio);
      _boutiqueProvider.fetchBoutique(_dio);
      _syncOnboardingContext();
      _startNotifications();
    } else {
      _userProvider.clear();
      _boutiqueProvider.clear();
      _onboardingProvider.clear();
      _ownerOnboardingProvider.reset();
      _stopNotifications();
    }
  }

  /// Starts push registration and the foreground realtime connection for the signed-in user.
  void _startNotifications() {
    _pushNotificationService.initialize();
    // Awaited inside [_connectRealtime] rather than left as an unawaited future: a
    // connect failure that escapes becomes an unhandled async error, which is
    // neither recorded nor actionable.
    unawaited(_connectRealtime());
    _startPushMessages();
    // A restored session opens on an inbox that is already stale. A sign-in that
    // fires again while the inbox is loaded only re-reads it, so the tab does not
    // blank out under a token refresh.
    if (_notificationsController.hasLoadedOnce) {
      _notificationsController.refresh();
    } else {
      _notificationsController.load();
    }
  }

  /// Starts the FCM message handler, when Firebase is available.
  ///
  /// A foreground arrival refreshes the inbox through the same "an arrival
  /// happened" seam the realtime path uses. A tap opens the app (the OS does
  /// that), marks **that notification** read, and routes through the one shared
  /// rule. A cold-start tap may arrive before the session is restored, so the
  /// mark-read is best-effort and the route is still opened.
  void _startPushMessages() {
    if (_pushMessageHandler != null || Firebase.apps.isEmpty) {
      return;
    }
    final handler = _pushMessageHandler = PushMessageHandler(
      FirebasePushMessageSource(FirebaseMessaging.instance),
    );
    handler.start(
      onMessage: _onNotificationReceived,
      markRead: _markNotificationRead,
      open: _openNotificationRoute,
    );
    unawaited(
      handler.handleInitialMessage(
        markRead: _markNotificationRead,
        open: _openNotificationRoute,
      ),
    );
  }

  /// Marks the notification a push tap addressed as read, best-effort.
  ///
  /// Deliberately the **notification**, never the conversation or the message:
  /// the thread owns its own read state and writes it when it is opened on the
  /// newest message (Q7). A session that is not restored yet must not lose the
  /// tap, so a failure is swallowed here.
  Future<void> _markNotificationRead(String notificationId) async {
    try {
      await _notificationRepository.markRead(notificationId);
      // The badge and the list must agree with the server after the tap.
      await _notificationsController.refresh();
    } catch (_) {
      // Best-effort.
    }
  }

  /// Opens the route a push tap asked for.
  ///
  /// Deferred to the next frame: a cold-start tap can be handled before the
  /// router has settled, and navigating from outside a frame would throw.
  void _openNotificationRoute(String location) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        _router.go(location);
      }
    });
  }

  /// Opens the foreground realtime connection, recording a failure.
  ///
  /// A reconnection re-joins the hub's groups and then re-reads the inbox: anything
  /// the socket missed while it was down is only recoverable from the API, and
  /// SignalR does not preserve group membership across a rebuilt socket.
  Future<void> _connectRealtime() async {
    try {
      await _realtimeNotificationService.connect(
        baseUrl: widget.config.apiBaseUrl,
        getToken: _authRepository.getToken,
        onNotification: _onNotificationReceived,
        onReconnected: _notificationsController.refresh,
        onRejoinFailed: _onRejoinFailed,
      );
    } catch (error, stackTrace) {
      debugPrint('[notifications] realtime connect failed: $error\n$stackTrace');
    }
  }

  /// Records a failed re-join: the socket is up but subscribed to nothing.
  void _onRejoinFailed(Object error) {
    debugPrint('[notifications] realtime re-subscribe failed: $error');
  }

  /// Records a notification that arrived while the app was open.
  ///
  /// When the payload carries the recipient's unread count the badge moves at
  /// once, before the inbox's own reply lands. The list is re-read regardless,
  /// because the rows remain the API's authority, and a payload without a count
  /// behaves exactly as it did before.
  void _onNotificationReceived(NotificationPayload payload) {
    _notificationProvider.push(payload);
    final count = payload.unreadCount;
    if (count != null) {
      _notificationsController.applyUnreadCount(count);
    }
    _notificationsController.refresh();
  }

  /// Reports the inbox's unread count to the header badge.
  void _syncUnreadBadge() {
    _notificationProvider.setUnreadCount(_notificationsController.unreadCount);
  }

  /// Stops the realtime connection and unregisters the push token on sign-out.
  void _stopNotifications() {
    _realtimeNotificationService.disconnect();
    _pushNotificationService.unregister();
    // The message listeners belong to the session that is ending; the next
    // sign-in starts them again.
    final pushMessages = _pushMessageHandler;
    _pushMessageHandler = null;
    if (pushMessages != null) {
      unawaited(pushMessages.dispose());
    }
    _notificationProvider.clear();
    _notificationsController.clearInbox();
  }

  /// Loads the persisted onboarding account-type choice for the signed-in user.
  void _syncOnboardingContext() {
    final clerkId = _authRepository.currentUser?.id;
    if (clerkId != null) {
      _onboardingProvider.load(clerkId);
    }
  }

  @override
  void dispose() {
    widget.clerkAuthState.removeListener(_onAuthChanged);
    _appLinksSub?.cancel();
    // `_userProvider` is owned by the bootstrap and outlives this shell.
    _onboardingProvider.dispose();
    _ownerOnboardingProvider.dispose();
    _boutiqueProvider.dispose();
    _notificationsController
      ..removeListener(_syncUnreadBadge)
      ..dispose();
    _notificationProvider.dispose();
    final pushMessages = _pushMessageHandler;
    _pushMessageHandler = null;
    if (pushMessages != null) {
      unawaited(pushMessages.dispose());
    }
    super.dispose();
  }

  String _initialLocation() {
    if (!_authRepository.isSignedIn) {
      return AppRoutes.auth;
    }
    final state = _userProvider.accountState;
    if (state == AvelineAccountState.suspended) {
      return AppRoutes.suspended;
    }
    if (state == AvelineAccountState.active) {
      return AppRoutes.home;
    }
    if (!_userProvider.hasCompletedOnboarding) {
      return _onboardingProvider.accountType == null
          ? AppRoutes.accountType
          : AppRoutes.onboarding;
    }
    return _onboardingProvider.isOwner
        ? AppRoutes.ownerOnboarding
        : AppRoutes.orgSetup;
  }

  GoRouter _buildRouter() {
    return GoRouter(
      initialLocation: _initialLocation(),
      refreshListenable: Listenable.merge(
        [widget.clerkAuthState, _userProvider, _onboardingProvider],
      ),
      redirect: (context, state) {
        final result = RouteGuards.redirectForAuth(
          state.matchedLocation,
          isSignedIn: _authRepository.isSignedIn,
          hasCompletedOnboarding: _authRepository.isSignedIn
              ? _userProvider.hasCompletedOnboarding
              : null,
          accountState: _authRepository.isSignedIn
              ? _userProvider.accountState?.wireValue
              : null,
          accountType: _authRepository.isSignedIn
              ? _onboardingProvider.accountType?.wireValue
              : null,
          // A signed-in profile load can still fail after the bootstrap, for
          // instance when signing in from the auth screen. Without this the
          // guards would park the user on the account-type picker with no
          // explanation and no way to retry.
          //
          // Only when no profile is held: the auth listener refetches the
          // profile periodically, and a background refresh that fails must not
          // pull a user who is already using the app onto the retry screen.
          profileFailed: _authRepository.isSignedIn &&
              _userProvider.user == null &&
              _userProvider.hasLoadFailed,
        );
        debugPrint(
          '[router] ${state.matchedLocation} signedIn=${_authRepository.isSignedIn} '
          'onboarded=${_userProvider.hasCompletedOnboarding} '
          'state=${_userProvider.accountState?.wireValue} '
          'type=${_onboardingProvider.accountType?.wireValue} -> $result',
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
          builder: (context, state) => MainShell(
            child: CatalogScreen(repository: _catalogRepository),
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
          builder: (context, state) => CatalogProductScreen(
            productId: state.pathParameters['productId'] ?? '',
            repository: _catalogRepository,
          ),
        ),
        GoRoute(
          path: AppRoutes.customers,
          name: 'customers',
          builder: (context, state) => MainShell(
            child: CustomersScreen(repository: _customerRepository),
          ),
        ),
        GoRoute(
          path: AppRoutes.customerPattern,
          name: 'customer',
          // Declared after the static `/customers` route so that segment is not
          // read as a client id.
          //
          // The client is deliberately not passed as `extra`: the router
          // re-parses its location whenever the auth or profile listenable
          // fires, and `extra` does not survive that, so the screen resolves the
          // profile from the id the location already carries.
          builder: (context, state) => CustomerScreen(
            customerId: state.pathParameters['customerId'] ?? '',
            repository: _customerRepository,
          ),
        ),
        GoRoute(
          path: AppRoutes.conversations,
          name: 'conversations',
          builder: (context, state) => MainShell(
            child: ConversationsScreen(
              repository: _conversationRepository,
              threadRepository: _threadRepository,
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
            conversationRepository: _conversationRepository,
            threadRepository: _threadRepository,
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

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        Provider<AppConfig>.value(value: widget.config),
        Provider<AuthRepository>.value(value: _authRepository),
        Provider<AuthTokenProvider>.value(value: _authRepository),
        ChangeNotifierProvider<UserProvider>.value(value: _userProvider),
        ChangeNotifierProvider<BoutiqueProvider>.value(
          value: _boutiqueProvider,
        ),
        ChangeNotifierProvider<OnboardingProvider>.value(
          value: _onboardingProvider,
        ),
        ChangeNotifierProvider<OwnerOnboardingProvider>.value(
          value: _ownerOnboardingProvider,
        ),
        ChangeNotifierProvider<NotificationProvider>.value(
          value: _notificationProvider,
        ),
        ChangeNotifierProvider<NotificationsController>.value(
          value: _notificationsController,
        ),
        ChangeNotifierProvider<HomeController>.value(value: _homeController),
        Provider<Dio>.value(value: _dio),
      ],
      child: MaterialApp.router(
        title: 'Aveline',
        theme: AppTheme.light,
        debugShowCheckedModeBanner: false,
        routerConfig: _router,
      ),
    );
  }
}
