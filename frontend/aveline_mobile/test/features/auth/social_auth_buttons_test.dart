import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/auth/domain/sign_up_field.dart';
import 'package:aveline_mobile/features/auth/domain/social_auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/social_provider.dart';
import 'package:aveline_mobile/features/auth/presentation/widgets/social_auth_buttons.dart';
import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

const _google = SocialProvider(
  strategy: clerk.Strategy.oauthGoogle,
  name: 'Google',
);
const _facebook = SocialProvider(
  strategy: clerk.Strategy.oauthFacebook,
  name: 'Facebook',
);

/// An [AuthRepository] that knows nothing about consent flows.
///
/// This is what a test fake for the password forms looks like, and it is also the
/// shape of a build with no social providers: the SSO section has to render
/// nothing rather than assume the cast succeeds.
class _PasswordOnlyAuth implements AuthRepository {
  _PasswordOnlyAuth({this.providers = const []});

  final List<SocialProvider> providers;

  @override
  bool get isSignedIn => false;

  @override
  AuthUser? get currentUser => null;

  @override
  bool get needsSecondFactor => false;

  @override
  String? get secondFactorStrategy => null;

  @override
  AuthCapabilities get capabilities =>
      AuthCapabilities(socialProviders: providers);

  @override
  Future<String?> getToken() async => null;

  @override
  Future<String?> refreshToken() async => null;

  @override
  Future<void> signOut() async {}

  @override
  Future<AuthFailure?> signInWithPassword({
    required String identifier,
    required String password,
  }) async => null;

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
  }) async => null;

  @override
  Future<AuthFailure?> sendEmailVerificationCode() async => null;

  @override
  Future<AuthFailure?> verifyEmailCode({required String code}) async => null;
}

/// A repository that can also run the consent flow, recording what it was asked.
class _RecordingAuth extends _PasswordOnlyAuth implements SocialAuthRepository {
  _RecordingAuth({
    super.providers,
    this.pending = const [],
    this.incomplete = false,
    this.emailVerification = false,
    this.signInFailure,
    this.signUpFailure,
    this.submitFailure,
    this.verifyFailure,
  });

  List<SignUpField> pending;
  bool incomplete;
  bool emailVerification;
  AuthFailure? signInFailure;
  AuthFailure? signUpFailure;
  AuthFailure? submitFailure;
  AuthFailure? verifyFailure;

  /// Set after construction by the resend test, so it can let the first code
  /// through and refuse the next one.
  AuthFailure? sendCodeFailure;

  int signInCalls = 0;
  int signUpCalls = 0;
  int submitCalls = 0;
  int verifyCalls = 0;
  int sendCodeCalls = 0;
  SocialProvider? lastProvider;
  Map<SignUpField, String>? lastValues;
  String? lastCode;

  @override
  Future<AuthFailure?> signInWithOAuth(
    SocialProvider provider, {
    required BuildContext context,
  }) async {
    signInCalls++;
    lastProvider = provider;
    return signInFailure;
  }

  @override
  Future<AuthFailure?> signUpWithOAuth(
    SocialProvider provider, {
    required BuildContext context,
  }) async {
    signUpCalls++;
    lastProvider = provider;
    return signUpFailure;
  }

  @override
  List<SignUpField> get pendingSignUpFields => pending;

  @override
  bool get signUpIncomplete => incomplete;

  @override
  bool get signUpNeedsEmailVerification => emailVerification;

  @override
  Future<AuthFailure?> submitSignUpFields(Map<SignUpField, String> values) async {
    submitCalls++;
    lastValues = values;
    return submitFailure;
  }

  @override
  Future<AuthFailure?> sendEmailVerificationCode() async {
    sendCodeCalls++;
    return sendCodeFailure;
  }

  @override
  Future<AuthFailure?> verifyEmailCode({required String code}) async {
    verifyCalls++;
    lastCode = code;
    return verifyFailure;
  }
}

Future<void> _pump(
  WidgetTester tester,
  AuthRepository auth, {
  bool isSignUp = false,
}) async {
  // Taller than the default 800x600 test surface so nothing under test is off
  // screen and needs a scroll before it can be tapped.
  tester.view.physicalSize = const Size(1170, 2400);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);

  await tester.pumpWidget(
    Provider<AuthRepository>.value(
      value: auth,
      child: MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          body: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: SocialAuthButtons(isSignUp: isSignUp),
          ),
        ),
      ),
    ),
  );
  await tester.pump();
}

void main() {
  group('SocialAuthButtons providers', () {
    testWidgets('offers a button for each provider the instance enabled', (
      tester,
    ) async {
      await _pump(tester, _RecordingAuth(providers: const [_google, _facebook]));

      expect(find.text('Continue with Google'), findsOneWidget);
      expect(find.text('Continue with Facebook'), findsOneWidget);
      expect(find.text('or continue with'), findsOneWidget);
    });

    testWidgets('renders nothing at all when the instance has no providers', (
      tester,
    ) async {
      // Not even the divider: a rule with nothing under it separates the form
      // from nothing.
      await _pump(tester, _RecordingAuth());

      expect(find.text('or continue with'), findsNothing);
      expect(find.byType(OutlinedButton), findsNothing);
    });

    testWidgets('renders nothing when the repository cannot run the flow', (
      tester,
    ) async {
      await _pump(tester, _PasswordOnlyAuth(providers: const [_google]));

      expect(find.text('Continue with Google'), findsNothing);
      expect(find.text('or continue with'), findsNothing);
    });
  });

  group('SocialAuthButtons flow', () {
    testWidgets('signing in asks the repository to sign in', (tester) async {
      final auth = _RecordingAuth(providers: const [_google]);
      await _pump(tester, auth);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(auth.signInCalls, 1);
      expect(auth.signUpCalls, 0);
      expect(auth.lastProvider, _google);
    });

    testWidgets('signing up asks the repository to sign up', (tester) async {
      final auth = _RecordingAuth(providers: const [_google]);
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(auth.signUpCalls, 1);
      expect(auth.signInCalls, 0);
    });

    testWidgets('a rejected consent page is reported', (tester) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        signInFailure: const AuthFailure(
          kind: AuthFailureKind.unknown,
          title: 'Could not sign in',
          message: 'Google refused the request.',
        ),
      );
      await _pump(tester, auth);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(find.text('Could not sign in'), findsOneWidget);
      expect(find.text('Google refused the request.'), findsOneWidget);
    });

    testWidgets('a rejected sign-up is reported', (tester) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        signUpFailure: const AuthFailure(
          kind: AuthFailureKind.identifierTaken,
          title: 'Account already exists',
          message: 'Sign in with that provider instead.',
        ),
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(find.text('Account already exists'), findsOneWidget);
      expect(find.text('Sign in with that provider instead.'), findsOneWidget);
    });
  });

  group('SocialAuthButtons sign-up completion', () {
    testWidgets('asks for the detail the provider could not supply', (
      tester,
    ) async {
      // What both Aveline instances do: the provider hands over an identity, the
      // instance still wants a username, and the account is not created until it
      // has one.
      final auth = _RecordingAuth(
        providers: const [_google],
        pending: const [SignUpField.username],
        incomplete: true,
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(find.text('A little more information'), findsOneWidget);
      expect(find.text('Enter your username'), findsNothing);

      await tester.enterText(
        find.widgetWithText(TextFormField, 'Username'),
        'kasun_d',
      );
      await tester.tap(find.text('Continue'));
      await tester.pumpAndSettle();

      expect(auth.submitCalls, 1);
      expect(auth.lastValues, {SignUpField.username: 'kasun_d'});
    });

    testWidgets('will not submit an empty detail', (tester) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        pending: const [SignUpField.username],
        incomplete: true,
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Continue'));
      await tester.pumpAndSettle();

      expect(find.text('Enter your username'), findsOneWidget);
      expect(auth.submitCalls, 0);
    });

    testWidgets('a rejected detail lands on the input that was rejected', (
      tester,
    ) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        pending: const [SignUpField.username],
        incomplete: true,
        submitFailure: const AuthFailure(
          kind: AuthFailureKind.unknown,
          title: 'That username is taken',
          message: 'Choose a different username.',
          field: 'username',
        ),
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Username'),
        'kasun_d',
      );
      await tester.tap(find.text('Continue'));
      await tester.pumpAndSettle();

      // Shown against the field rather than in a toast that vanishes before it is
      // acted on.
      expect(find.text('Choose a different username.'), findsOneWidget);
    });

    testWidgets('goes back to the buttons once the last detail is accepted', (
      tester,
    ) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        pending: const [SignUpField.username],
        incomplete: true,
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      // The instance accepted it, so the sign-up is done and a session exists;
      // the router is already moving the user on.
      auth.pending = const [];
      auth.incomplete = false;

      await tester.enterText(
        find.widgetWithText(TextFormField, 'Username'),
        'kasun_d',
      );
      await tester.tap(find.text('Continue'));
      await tester.pumpAndSettle();

      expect(find.text('A little more information'), findsNothing);
      expect(find.text('Continue with Google'), findsOneWidget);
    });

    testWidgets('says so when the instance wants something it cannot collect', (
      tester,
    ) async {
      // A sign-up that is still incomplete with nothing this screen can prompt
      // for would otherwise leave the account half-made and the user silent.
      final auth = _RecordingAuth(
        providers: const [_google],
        incomplete: true,
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(find.text('Almost there'), findsOneWidget);
      expect(
        find.textContaining('this app cannot collect'),
        findsOneWidget,
      );
    });

    testWidgets('confirms an email the provider left unverified', (
      tester,
    ) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        emailVerification: true,
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();

      expect(auth.sendCodeCalls, 1);
      expect(find.text('Confirm your email'), findsOneWidget);

      await tester.enterText(
        find.widgetWithText(TextFormField, 'Verification code'),
        '123456',
      );
      await tester.tap(find.text('Verify & continue'));
      await tester.pumpAndSettle();

      expect(auth.verifyCalls, 1);
      expect(auth.lastCode, '123456');
    });

    testWidgets('a rejected code lands on the code input', (tester) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        emailVerification: true,
        verifyFailure: const AuthFailure(
          kind: AuthFailureKind.invalidCode,
          title: 'That code did not work',
          message: 'Check the code and try again.',
          field: 'code',
        ),
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Verification code'),
        '000000',
      );
      await tester.tap(find.text('Verify & continue'));
      await tester.pumpAndSettle();

      expect(find.text('Check the code and try again.'), findsOneWidget);
    });

    testWidgets('a code that could not be sent is reported', (tester) async {
      final auth = _RecordingAuth(
        providers: const [_google],
        emailVerification: true,
      );
      await _pump(tester, auth, isSignUp: true);

      await tester.tap(find.text('Continue with Google'));
      await tester.pumpAndSettle();
      expect(find.text('Confirm your email'), findsOneWidget);

      // The instance starts refusing the resend after the first one landed.
      auth.sendCodeFailure = const AuthFailure(
        kind: AuthFailureKind.rateLimited,
        title: 'Slow down',
        message: 'Try again in a minute.',
      );
      await tester.tap(find.text('Resend code'));
      await tester.pumpAndSettle();

      expect(auth.sendCodeCalls, 2);
      expect(find.text('Slow down'), findsOneWidget);
    });
  });
}
