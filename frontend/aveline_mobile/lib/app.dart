import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import 'core/config/app_config.dart';
import 'core/network/api_client.dart';
import 'core/network/auth_token_provider.dart';
import 'core/router/route_guards.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/data/clerk_auth_repository.dart';
import 'features/auth/domain/auth_repository.dart';
import 'features/auth/presentation/screens/auth_screen.dart';
import 'features/home/presentation/screens/home_screen.dart';

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
  late final Dio _dio;
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    _authRepository = ClerkAuthRepository(
      widget.clerkAuthState,
      jwtTemplateName: widget.config.jwtTemplateName,
    );
    _dio = ApiClientFactory.create(
      baseUrl: widget.config.apiBaseUrl,
      tokenProvider: _authRepository,
    );
    _router = _buildRouter();
  }

  GoRouter _buildRouter() {
    return GoRouter(
      initialLocation: _authRepository.isSignedIn
          ? AppRoutes.home
          : AppRoutes.auth,
      refreshListenable: widget.clerkAuthState,
      redirect: (context, state) => RouteGuards.redirectForAuth(
        state.matchedLocation,
        isSignedIn: _authRepository.isSignedIn,
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
