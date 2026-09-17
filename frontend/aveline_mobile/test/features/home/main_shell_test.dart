import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/home/presentation/screens/main_shell.dart';
import 'package:aveline_mobile/shared/widgets/animated_blossom.dart';
import 'package:aveline_mobile/shared/widgets/aveline_header.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

/// Minimal in-memory [AuthRepository] for widget tests.
class _FakeAuthRepository implements AuthRepository {
  @override
  bool get isSignedIn => true;

  @override
  AuthUser? get currentUser => const AuthUser(
        id: 'u1',
        firstName: 'Kasun',
        email: 'kasun@example.com',
      );

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
  Future<String?> signInWithPassword({
    required String identifier,
    required String password,
  }) async =>
      null;

  @override
  Future<String?> sendSecondFactorCode() async => null;

  @override
  Future<String?> verifySecondFactorCode({required String code}) async => null;

  @override
  Future<String?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  }) async =>
      null;

  @override
  Future<String?> sendEmailVerificationCode() async => null;

  @override
  Future<String?> verifyEmailCode({required String code}) async => null;
}

void main() {
  Widget wrap({bool showHeader = true}) {
    final userProvider = UserProvider();
    userProvider.setUser(
      AvelineUser(
        id: 'u1',
        clerkId: 'c1',
        email: 'kasun@example.com',
        firstName: 'Kasun',
        lastName: 'Peiris',
        username: 'kasun',
        userRole: AppRoles.staff,
        organizationRole: '',
        organizationId: 'org_1',
        hasCompletedOnboarding: true,
        accountState: AvelineAccountState.active,
        contactPreference: 'email',
        pushNotificationsEnabled: true,
        isActive: true,
        createdAt: DateTime(2025, 1, 1),
        updatedAt: DateTime(2025, 1, 1),
      ),
    );

    return MultiProvider(
      providers: [
        Provider<AuthRepository>(create: (_) => _FakeAuthRepository()),
        ChangeNotifierProvider<UserProvider>.value(value: userProvider),
      ],
      child: MaterialApp(
        home: MainShell(showHeader: showHeader),
      ),
    );
  }

  testWidgets('shows the Home screen, universal header, and animated blossom by default',
      (tester) async {
    await tester.pumpWidget(wrap());

    expect(
      find.textContaining(RegExp(r'Good (morning|afternoon|evening), Kasun\.')),
      findsOneWidget,
    );
    expect(find.byType(AvelineHeader), findsOneWidget);
    expect(find.byType(AnimatedBlossom), findsOneWidget);
  });

  testWidgets('hides header when showHeader is false', (tester) async {
    await tester.pumpWidget(wrap(showHeader: false));

    expect(
      find.textContaining(RegExp(r'Good (morning|afternoon|evening), Kasun\.')),
      findsOneWidget,
    );
    expect(find.byType(AvelineHeader), findsNothing);
    expect(find.byType(AnimatedBlossom), findsOneWidget);
  });

  testWidgets('opens the full-screen Salon from the animated blossom launcher',
      (tester) async {
    await tester.pumpWidget(wrap());

    await tester.tap(find.byKey(const Key('animated_blossom_button')));
    // The Salon's blossom avatar animates continuously, so pump a fixed duration
    // rather than pumpAndSettle (which would never settle).
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));

    expect(find.text('The Salon'), findsOneWidget);
  });
}
