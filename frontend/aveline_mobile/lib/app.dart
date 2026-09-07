import 'dart:async';

import 'package:app_links/app_links.dart';
import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/config/app_config.dart';
import 'core/network/api_client.dart';
import 'core/network/auth_token_provider.dart';
import 'core/providers/onboarding_provider.dart';
import 'core/providers/owner_onboarding_provider.dart';
import 'core/providers/user_provider.dart';
import 'core/router/route_guards.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/data/clerk_auth_repository.dart';
import 'features/auth/domain/auth_repository.dart';
import 'features/auth/domain/aveline_user.dart';
import 'features/auth/presentation/screens/auth_screen.dart';
import 'features/home/presentation/screens/home_screen.dart';
import 'features/onboarding/data/onboarding_preferences.dart';
import 'features/onboarding/data/owner_onboarding_api.dart';
import 'features/onboarding/presentation/screens/account_type_screen.dart';
import 'features/onboarding/presentation/screens/invite_screen.dart';
import 'features/onboarding/presentation/screens/onboarding_screen.dart';
import 'features/onboarding/presentation/screens/org_setup_screen.dart';
import 'features/onboarding/presentation/screens/owner_onboarding_screen.dart';
import 'features/onboarding/presentation/screens/suspended_screen.dart';

/// Root widget: wraps the app in Clerk, wires DI, and configures routing.
class AvelineApp extends StatelessWidget {
  const AvelineApp({super.key, required this.config, required this.preferences});

  final AppConfig config;
  final OnboardingPreferences preferences;

  @override
  Widget build(BuildContext context) {
    return ClerkAuth(
      config: ClerkAuthConfig(publishableKey: config.clerkPublishableKey),
      child: ClerkAuthBuilder(
        builder: (context, authState) => AvelineAppShell(
          config: config,
          clerkAuthState: authState,
          preferences: preferences,
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
  });

  final AppConfig config;
  final ClerkAuthState clerkAuthState;
  final OnboardingPreferences preferences;

  @override
  State<AvelineAppShell> createState() => _AvelineAppShellState();
}

class _AvelineAppShellState extends State<AvelineAppShell> {
  late final AuthRepository _authRepository;
  late final UserProvider _userProvider;
  late final OnboardingProvider _onboardingProvider;
  late final OwnerOnboardingProvider _ownerOnboardingProvider;
  late final Dio _dio;
  late final GoRouter _router;
  final AppLinks _appLinks = AppLinks();
  StreamSubscription<Uri>? _appLinksSub;

  @override
  void initState() {
    super.initState();
    _authRepository = ClerkAuthRepository(
      widget.clerkAuthState,
      jwtTemplateName: widget.config.jwtTemplateName,
    );
    _userProvider = UserProvider();
    _onboardingProvider = OnboardingProvider(widget.preferences);
    _dio = ApiClientFactory.create(
      baseUrl: widget.config.apiBaseUrl,
      tokenProvider: _authRepository,
    );
    _ownerOnboardingProvider = OwnerOnboardingProvider(OwnerOnboardingApi(_dio));
    _router = _buildRouter();

    _syncOnboardingContext();
    if (_authRepository.isSignedIn) {
      _userProvider.fetchUser(_dio);
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
      _syncOnboardingContext();
    } else {
      _userProvider.clear();
      _onboardingProvider.clear();
      _ownerOnboardingProvider.reset();
    }
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
    _userProvider.dispose();
    _onboardingProvider.dispose();
    _ownerOnboardingProvider.dispose();
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
          builder: (context, state) => const HomeScreen(),
        ),
        GoRoute(
          path: AppRoutes.auth,
          name: 'auth',
          builder: (context, state) => const AuthScreen(),
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
        ChangeNotifierProvider<OnboardingProvider>.value(
          value: _onboardingProvider,
        ),
        ChangeNotifierProvider<OwnerOnboardingProvider>.value(
          value: _ownerOnboardingProvider,
        ),
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
