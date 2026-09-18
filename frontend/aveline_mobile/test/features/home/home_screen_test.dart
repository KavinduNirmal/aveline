import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/home/presentation/screens/home_screen.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/blossom_usage_card.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/client_link_section.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/focus_deck.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/quick_actions_row.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/today_strip.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

/// Minimal in-memory [AuthRepository] exposing just the signed-in identity.
class _FakeAuthRepository implements AuthRepository {
  _FakeAuthRepository({this.firstName, this.userRole});

  final String? firstName;
  final String? userRole;

  @override
  bool get isSignedIn => true;

  @override
  AuthUser? get currentUser =>
      AuthUser(id: 'u1', firstName: firstName, userRole: userRole);

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

/// A phone-shaped viewport, so the whole Home column (including the deck's
/// footer) is on screen and can be tapped without scrolling.
void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// Reduced motion is on, matching the rest of the suite: Home carries ambient
/// animation (the veil, the client status card) that would never settle
/// otherwise, and a rotating page is not what these tests are about.
Widget _wrap({String? firstName, String? userRole, DateTime? now}) {
  return Provider<AuthRepository>.value(
    value: _FakeAuthRepository(firstName: firstName, userRole: userRole),
    child: MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: Scaffold(body: HomeScreen(now: now)),
        ),
      ),
    ),
  );
}

/// Home is taller than one screen, so the lower sections are reached by rolling
/// the column until they are on screen.
Future<void> _scrollTo(WidgetTester tester, Finder target) async {
  await tester.scrollUntilVisible(
    target,
    320,
    scrollable: find.byType(Scrollable).first,
  );
}

/// The greeting heading's rendered plain text, e.g. `Good morning, Nadia.`
String _greetingText(WidgetTester tester) {
  final greeting = tester.widget<Text>(find.byKey(const Key('home_greeting')));
  return greeting.textSpan!.toPlainText();
}

void main() {
  group('HomeScreen greeting', () {
    testWidgets('wishes the time of day and accents the name in italics',
        (tester) async {
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      expect(_greetingText(tester), 'Good morning, Nadia.');

      // The addressee carries the accent colour in italics; the salutation
      // stays in the default headline colour.
      final greeting =
          tester.widget<Text>(find.byKey(const Key('home_greeting')));
      final spans = (greeting.textSpan! as TextSpan).children!.cast<TextSpan>();
      final nameSpan = spans.last;

      expect(nameSpan.text, ' Nadia.');
      expect(nameSpan.style?.color, AppTheme.colorScheme.primary);
      expect(nameSpan.style?.fontStyle, FontStyle.italic);
      expect(spans.first.style?.color, isNot(AppTheme.colorScheme.primary));
    });

    testWidgets('sets the greeting in the display scale', (tester) async {
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      final greeting =
          tester.widget<Text>(find.byKey(const Key('home_greeting')));
      final spans = (greeting.textSpan! as TextSpan).children!.cast<TextSpan>();

      // `DESIGN.md` gives `display-lg` to a personalized greeting by name. At
      // `headlineMedium` (24) the page had no expressive type on it at all.
      expect(greeting.style?.fontSize, 32);
      expect(
        greeting.style?.fontSize,
        AppTheme.textTheme.displayLarge?.fontSize,
      );
      // `google_fonts` names the family with its weight variant appended.
      expect(greeting.style?.fontFamily, startsWith('PlayfairDisplay'));

      // The addressee inherits the size and only takes the accent and italics.
      expect(spans.last.style?.fontSize, greeting.style?.fontSize);
    });

    testWidgets('switches salutation as the day moves on', (tester) async {
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 14, 5)),
      );
      expect(_greetingText(tester), 'Good afternoon, Nadia.');

      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 19, 5)),
      );
      expect(_greetingText(tester), 'Good evening, Nadia.');
    });

    testWidgets('falls back to a bare salutation without a first name',
        (tester) async {
      await tester.pumpWidget(
        _wrap(firstName: null, now: DateTime(2026, 1, 1, 8)),
      );

      expect(_greetingText(tester), 'Good morning.');

      final greeting =
          tester.widget<Text>(find.byKey(const Key('home_greeting')));
      final spans = (greeting.textSpan! as TextSpan).children!.cast<TextSpan>();
      expect(spans, hasLength(1));
    });

    testWidgets('no longer shows the AVELINE overline above the greeting',
        (tester) async {
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      expect(find.text('AVELINE'), findsNothing);
    });
  });

  group('HomeScreen sections', () {
    testWidgets('replaces the placeholder cards with the focus deck',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      expect(find.text('Blossoms'), findsNothing);
      expect(find.text('Approvals'), findsNothing);
      expect(find.text('Remaining this billing period.'), findsNothing);

      // The row is the greeting's toolbar, so it carries no overline of its own:
      // a second heading next to the greeting competed with it.
      expect(find.text('QUICK ACTIONS'), findsNothing);
      expect(find.byType(QuickActionsRow), findsOneWidget);
      expect(find.text("TODAY'S FOCUS"), findsOneWidget);
      expect(find.byType(FocusDeck), findsOneWidget);
    });

    testWidgets('opens with the floor at a glance', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      // What is inside today's focus, by kind of work, then the next one due.
      expect(find.byType(TodayStrip), findsOneWidget);
      expect(find.text('TODAY AT A GLANCE'), findsOneWidget);
      expect(find.text('clients arriving'), findsOneWidget);
      expect(find.text('deliveries today'), findsOneWidget);
      expect(find.text('intake pieces'), findsOneWidget);
      expect(find.text('Next delivery'), findsOneWidget);
      expect(find.text('8 left'), findsOneWidget);
    });

    testWidgets('signing a docket off moves the count', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      expect(find.text('8 left'), findsOneWidget);

      await tester.tap(
        find.descendant(
          of: find.byKey(const Key('focus_deck_top')),
          matching: find.byType(FilledButton),
        ),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.text('7 left'), findsOneWidget);
    });

    testWidgets('closes with the direct client link', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      await _scrollTo(tester, find.text('DIRECT CLIENT LINK'));

      expect(find.text('DIRECT CLIENT LINK'), findsOneWidget);
      expect(find.byType(ClientLinkSection), findsOneWidget);
      expect(find.text('Add New'), findsOneWidget);
      expect(find.text('See all'), findsOneWidget);
    });

    testWidgets('ends with the Blossom meter', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      await _scrollTo(tester, find.text('BLOSSOM USAGE'));

      expect(find.byType(BlossomUsageCard), findsOneWidget);
      expect(find.text('Request additional blossoms'), findsOneWidget);
    });

    testWidgets(
      'leads an owner with an approval and staff with the floor plan',
      (tester) async {
        _usePhoneSurface(tester);

        await tester.pumpWidget(
          _wrap(firstName: 'Nadia', userRole: AppRoles.owner),
        );
        expect(
          find.text('Approve delivery courier for Mrs. Silva'),
          findsOneWidget,
        );

        await tester.pumpWidget(
          _wrap(firstName: 'Nadia', userRole: AppRoles.staff),
        );
        expect(
          find.text('Acknowledge the floor plan for today'),
          findsOneWidget,
        );
        expect(
          find.text('Approve delivery courier for Mrs. Silva'),
          findsNothing,
        );
      },
    );

    testWidgets('says so when a quick action has no slice behind it yet', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      await tester.tap(find.text('Clock in'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.text('Clock-in is not on mobile yet.'), findsOneWidget);
    });

    testWidgets('opens the catalog route from the quick actions',
        (tester) async {
      _usePhoneSurface(tester);
      final router = GoRouter(
        initialLocation: AppRoutes.home,
        routes: [
          GoRoute(
            path: AppRoutes.home,
            builder: (context, state) => const HomeScreen(),
          ),
          GoRoute(
            path: AppRoutes.catalog,
            builder: (context, state) =>
                const Scaffold(body: Text('Catalog destination')),
          ),
        ],
      );
      addTearDown(router.dispose);

      await tester.pumpWidget(
        Provider<AuthRepository>.value(
          value: _FakeAuthRepository(firstName: 'Nadia'),
          child: MaterialApp.router(theme: AppTheme.light, routerConfig: router),
        ),
      );

      await tester.tap(find.text('Catalog'));
      await tester.pumpAndSettle();

      expect(find.text('Catalog destination'), findsOneWidget);
    });

    testWidgets('opens the customers route from the quick actions', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      final router = GoRouter(
        initialLocation: AppRoutes.home,
        routes: [
          GoRoute(
            path: AppRoutes.home,
            builder: (context, state) => const HomeScreen(),
          ),
          GoRoute(
            path: AppRoutes.customers,
            builder: (context, state) =>
                const Scaffold(body: Text('Customers destination')),
          ),
        ],
      );
      addTearDown(router.dispose);

      await tester.pumpWidget(
        Provider<AuthRepository>.value(
          value: _FakeAuthRepository(firstName: 'Nadia'),
          child: MaterialApp.router(theme: AppTheme.light, routerConfig: router),
        ),
      );

      await tester.tap(find.text('Customers'));
      await tester.pumpAndSettle();

      expect(find.text('Customers destination'), findsOneWidget);
    });
  });
}
