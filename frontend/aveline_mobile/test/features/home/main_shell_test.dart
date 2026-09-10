import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/home/presentation/screens/main_shell.dart';
import 'package:aveline_mobile/shared/widgets/floating_dock.dart';
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
  Widget wrap() => Provider<AuthRepository>(
        create: (_) => _FakeAuthRepository(),
        child: const MaterialApp(home: MainShell()),
      );

  testWidgets('shows the Home tab and the floating dock by default',
      (tester) async {
    await tester.pumpWidget(wrap());

    expect(find.textContaining('Good day'), findsOneWidget);
    expect(find.byType(FloatingDock), findsOneWidget);
  });

  testWidgets('switches to the Customers placeholder tab', (tester) async {
    await tester.pumpWidget(wrap());

    await tester.tap(find.text('Customers'));
    await tester.pumpAndSettle();

    expect(find.text('Customer concierge & memory'), findsOneWidget);
  });

  testWidgets('opens the full-screen Salon from the center launcher',
      (tester) async {
    await tester.pumpWidget(wrap());

    await tester.tap(find.bySemanticsLabel('Open Salon'));
    // The Salon's blossom avatar animates continuously, so pump a fixed duration
    // rather than pumpAndSettle (which would never settle).
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));

    expect(find.text('The Salon'), findsOneWidget);
  });
}
