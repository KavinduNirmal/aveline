import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/auth/presentation/widgets/sign_up_form.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

/// An [AuthRepository] that records what it was asked to do and answers with a
/// session, so a valid submission stops at the form instead of walking into the
/// email-verification step.
class _RecordingAuthRepository implements AuthRepository {
  int signUpCalls = 0;
  String? lastEmail;
  String? lastPassword;

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
  AuthCapabilities get capabilities => AuthCapabilities.unknown;

  @override
  Future<String?> getToken() async => 'token';

  @override
  Future<String?> refreshToken() async => 'token';

  @override
  Future<void> signOut() async {}

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
  }) async {
    signUpCalls++;
    lastEmail = emailAddress;
    lastPassword = password;
    return null;
  }

  @override
  Future<AuthFailure?> sendEmailVerificationCode() async => null;

  @override
  Future<AuthFailure?> verifyEmailCode({required String code}) async => null;
}

/// A form field by the label it renders, so a test never has to count field
/// positions (which move when the instance enables the username input).
Finder _field(String label) => find.widgetWithText(TextFormField, label);

Future<void> _openForm(WidgetTester tester, AuthRepository auth) async {
  // Taller than the default 800x600 test surface so the submit button is on
  // screen without scrolling.
  tester.view.physicalSize = const Size(1170, 2400);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);

  await tester.pumpWidget(
    Provider<AuthRepository>.value(
      value: auth,
      child: MaterialApp(
        theme: AppTheme.light,
        home: const Scaffold(
          body: SingleChildScrollView(
            padding: EdgeInsets.all(24),
            child: SignUpForm(),
          ),
        ),
      ),
    ),
  );
  await tester.pump();
}

void main() {
  group('SignUpForm validation', () {
    testWidgets('shows the errors for invalid input and blocks submission', (
      tester,
    ) async {
      final auth = _RecordingAuthRepository();
      await _openForm(tester, auth);

      await tester.enterText(_field('Email'), 'not-an-email');
      await tester.enterText(_field('Password'), 'short');
      await tester.tap(find.widgetWithText(FilledButton, 'Create account'));
      await tester.pump();

      // Both validators report, and the repository is never asked to create an
      // account: `_create` returns before its first `await`.
      expect(find.text('Enter a valid email address'), findsOneWidget);
      expect(
        find.text('Password must be at least 8 characters'),
        findsOneWidget,
      );
      expect(auth.signUpCalls, 0);
    });

    testWidgets('submits the same form once the input is corrected', (
      tester,
    ) async {
      final auth = _RecordingAuthRepository();
      await _openForm(tester, auth);

      // Invalid first, to prove the block is validation and not luck.
      await tester.enterText(_field('Email'), 'not-an-email');
      await tester.enterText(_field('Password'), 'short');
      await tester.tap(find.widgetWithText(FilledButton, 'Create account'));
      await tester.pump();
      expect(auth.signUpCalls, 0);

      await tester.enterText(_field('Email'), 'kasun@example.com');
      await tester.enterText(_field('Password'), 'longenough1');
      await tester.tap(find.widgetWithText(FilledButton, 'Create account'));
      await tester.pump();

      expect(auth.signUpCalls, 1);
      expect(auth.lastEmail, 'kasun@example.com');
      expect(auth.lastPassword, 'longenough1');
      expect(find.text('Enter a valid email address'), findsNothing);
      expect(find.text('Password must be at least 8 characters'), findsNothing);
    });
  });
}
