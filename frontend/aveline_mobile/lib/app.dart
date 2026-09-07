import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/config/app_config.dart';
import 'core/network/api_client.dart';
import 'core/network/auth_token_provider.dart';
import 'core/providers/user_provider.dart';
import 'core/router/route_guards.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/data/clerk_auth_repository.dart';
import 'features/auth/domain/auth_repository.dart';
import 'features/auth/domain/aveline_user.dart';
import 'features/auth/presentation/screens/auth_screen.dart';
import 'features/home/presentation/screens/home_screen.dart';
import 'features/onboarding/presentation/screens/onboarding_screen.dart';
import 'features/onboarding/presentation/screens/org_setup_screen.dart';
import 'features/onboarding/presentation/screens/suspended_screen.dart';

/// Root widget: wraps the app in Clerk, wires DI, and configures routing.
class AvelineApp extends StatelessWidget {
  const AvelineApp({super.key, required this.config});

  final AppConfig config;

  @override
  Widget build(BuildContext context) {
    return ClerkAuth(
      config: ClerkAuthConfig(publishableKey: config.clerkPublishableKey),
      child: ClerkAuthBuilder(
        builder: (context, authState) =>
            AvelineAppShell(config: config, clerkAuthState: authState),
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
  });

  final AppConfig config;
  final ClerkAuthState clerkAuthState;

  @override
  State<AvelineAppShell> createState() => _AvelineAppShellState();
}

class _AvelineAppShellState extends State<AvelineAppShell> {
  late final AuthRepository _authRepository;
  late final UserProvider _userProvider;
  late final Dio _dio;
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    _authRepository = ClerkAuthRepository(
      widget.clerkAuthState,
      jwtTemplateName: widget.config.jwtTemplateName,
    );
    _userProvider = UserProvider();
    _dio = ApiClientFactory.create(
      baseUrl: widget.config.apiBaseUrl,
      tokenProvider: _authRepository,
    );
    _router = _buildRouter();

    if (_authRepository.isSignedIn) {
      _userProvider.fetchUser(_dio);
    }

    widget.clerkAuthState.addListener(_onAuthChanged);
  }

  void _onAuthChanged() {
    if (_authRepository.isSignedIn) {
      _userProvider.fetchUser(_dio);
    } else {
      _userProvider.clear();
    }
  }

  @override
  void dispose() {
    widget.clerkAuthState.removeListener(_onAuthChanged);
    _userProvider.dispose();
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
      return AppRoutes.onboarding;
    }
    return AppRoutes.orgSetup;
  }

  GoRouter _buildRouter() {
    return GoRouter(
      initialLocation: _initialLocation(),
      refreshListenable: Listenable.merge([widget.clerkAuthState, _userProvider]),
      redirect: (context, state) => RouteGuards.redirectForAuth(
        state.matchedLocation,
        isSignedIn: _authRepository.isSignedIn,
        hasCompletedOnboarding: _authRepository.isSignedIn
            ? _userProvider.hasCompletedOnboarding
            : null,
        accountState: _authRepository.isSignedIn
            ? _userProvider.accountState?.wireValue
            : null,
      ),
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
          path: AppRoutes.onboarding,
          name: 'onboarding',
          builder: (context, state) => const OnboardingScreen(),
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
