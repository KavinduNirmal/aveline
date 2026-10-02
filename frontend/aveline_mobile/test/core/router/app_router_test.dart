import 'package:aveline_mobile/core/providers/onboarding_provider.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/core/router/app_router.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/auth/presentation/screens/auth_screen.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/commerce/data/repositories/api_commerce_repository.dart';
import 'package:aveline_mobile/features/conversations/data/demo_conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/data/empty_thread_repository.dart';
import 'package:aveline_mobile/features/customers/data/demo_customer_repository.dart';
import 'package:aveline_mobile/features/home/presentation/screens/main_shell.dart';
import 'package:aveline_mobile/features/onboarding/data/onboarding_preferences.dart';
import 'package:aveline_mobile/features/settings/presentation/screens/settings_screen.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Minimal in-memory [AuthRepository] for widget tests, mirroring the one the
/// shell tests use. Only the members the router reads are meaningful.
class _FakeAuthRepository implements AuthRepository {
  _FakeAuthRepository({this.isSignedIn = false});

  @override
  final bool isSignedIn;

  @override
  AuthUser? get currentUser => isSignedIn
      ? const AuthUser(
          id: 'u1',
          firstName: 'Kasun',
          email: 'kasun@example.com',
        )
      : null;

  @override
  bool get needsSecondFactor => false;

  @override
  String? get secondFactorStrategy => null;

  @override
  Future<String?> getToken() async => 'token';

  @override
  Future<String?> refreshToken() async => 'token';

  @override
  Future<void> signOut() async {}

  @override
  AuthCapabilities get capabilities => AuthCapabilities.unknown;

  @override
  Future<AuthFailure?> signInWithPassword({
    required String identifier,
    required String password,
  }) async =>
      null;

  @override
  Future<AuthFailure?> sendSecondFactorCode() async => null;

  @override
  Future<AuthFailure?> verifySecondFactorCode({required String code}) async =>
      null;

  @override
  Future<AuthFailure?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  }) async =>
      null;

  @override
  Future<AuthFailure?> sendEmailVerificationCode() async => null;

  @override
  Future<AuthFailure?> verifyEmailCode({required String code}) async => null;
}

/// An active associate, in the shape `GET /users/me` answers with.
AvelineUser _activeUser() => AvelineUser(
  id: 'u1',
  clerkId: 'c1',
  email: 'kasun@example.com',
  firstName: 'Kasun',
  lastName: 'Peiris',
  username: 'kasun',
  userRole: 'staff',
  organizationRole: '',
  organizationId: 'org_1',
  hasCompletedOnboarding: true,
  accountState: AvelineAccountState.active,
  contactPreference: 'email',
  pushNotificationsEnabled: true,
  isActive: true,
  createdAt: DateTime(2025, 1, 1),
  updatedAt: DateTime(2025, 1, 1),
);

/// Pumps the app's **real** router — built by [AppRouter.build], the same
/// factory `AvelineAppShell` uses — so the redirect under test is the one that
/// ships rather than a re-declared copy of it.
///
/// The repositories behind routes the test never visits are real instances over
/// a bare [Dio]; they are never called, because every navigation asserted here
/// lands on a screen that reads no repository.
Future<GoRouter> _pumpApp(
  WidgetTester tester, {
  required bool signedIn,
  AvelineUser? user,
  String initialLocation = AppRoutes.home,
}) async {
  SharedPreferences.setMockInitialValues({});
  final preferences = await SharedPreferences.getInstance();
  final onboardingProvider = OnboardingProvider(
    OnboardingPreferences(preferences),
  );
  final userProvider = UserProvider();
  if (user != null) {
    userProvider.setUser(user);
  }
  final authRepository = _FakeAuthRepository(isSignedIn: signedIn);
  final dio = Dio();

  final router = AppRouter.build(
    authRepository: authRepository,
    userProvider: userProvider,
    onboardingProvider: onboardingProvider,
    catalogRepository: DemoCatalogProductRepository(pageDelay: Duration.zero),
    customerRepository: DemoCustomerRepository(),
    conversationRepository: DemoConversationRepository(),
    threadRepository: EmptyThreadRepository(),
    commerceRepository: ApiCommerceRepository(dio, organizationId: () => null),
    refreshListenable: Listenable.merge([userProvider, onboardingProvider]),
    initialLocation: initialLocation,
  );

  await tester.pumpWidget(
    MultiProvider(
      providers: [
        Provider<AuthRepository>.value(value: authRepository),
        ChangeNotifierProvider<UserProvider>.value(value: userProvider),
        ChangeNotifierProvider<OnboardingProvider>.value(
          value: onboardingProvider,
        ),
        Provider<Dio>.value(value: dio),
      ],
      child: MaterialApp.router(
        theme: AppTheme.light,
        debugShowCheckedModeBanner: false,
        routerConfig: router,
      ),
    ),
  );
  // The auth and home screens animate continuously (the aurora field and the
  // blossom), so settle to a fixed number of frames rather than pumpAndSettle.
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 100));
  return router;
}

void main() {
  group('AppRouter redirect', () {
    testWidgets('sends a signed-out visitor off a protected route to /auth', (
      tester,
    ) async {
      final router = await _pumpApp(
        tester,
        signedIn: false,
        initialLocation: AppRoutes.settings,
      );

      // The real router, not the guard function: the guard is consulted by
      // GoRouter and the location it chose is where the app actually lands.
      expect(router.state.uri.path, AppRoutes.auth);
      expect(find.byType(AuthScreen), findsOneWidget);
      expect(find.byType(SettingsScreen), findsNothing);
    });

    testWidgets('moves a signed-in active account from /auth to the shell', (
      tester,
    ) async {
      final router = await _pumpApp(
        tester,
        signedIn: true,
        user: _activeUser(),
        initialLocation: AppRoutes.auth,
      );

      expect(router.state.uri.path, AppRoutes.home);
      expect(find.byType(MainShell), findsOneWidget);
      expect(find.byType(AuthScreen), findsNothing);
    });

    testWidgets('forwards the retired /profile path onto settings', (
      tester,
    ) async {
      final router = await _pumpApp(
        tester,
        signedIn: true,
        user: _activeUser(),
        initialLocation: AppRoutes.profile,
      );

      // A route-level redirect in the real table, not the auth guard: `/profile`
      // was merged into Settings, and stored links still name it.
      expect(router.state.uri.path, AppRoutes.settings);
      expect(find.byType(SettingsScreen), findsOneWidget);
    });
  });
}
