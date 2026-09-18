import 'dart:async';

import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/home/data/demo_blossom_usage.dart';
import 'package:aveline_mobile/features/home/data/demo_client_highlights.dart';
import 'package:aveline_mobile/features/home/data/demo_focus_tasks.dart';
import 'package:aveline_mobile/features/home/data/home_repository.dart';
import 'package:aveline_mobile/features/home/domain/blossom_usage.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:aveline_mobile/features/home/presentation/home_controller.dart';
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

/// A source whose one read is the snapshot the test chose.
class _StubHomeRepository implements HomeRepository {
  _StubHomeRepository(this.snapshot);

  final HomeSnapshot snapshot;
  int completeCount = 0;
  bool completeThrows = false;

  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) async => snapshot;

  @override
  Future<void> completeTask(FocusTask task) async {
    completeCount++;
    if (completeThrows) {
      throw StateError('the server refused');
    }
  }

  @override
  Future<ClientHighlight> createWalkIn(String fullName) async => ClientHighlight(
        id: 'server-walkin-1',
        name: fullName,
        tier: null,
        activity: 'Walk-in added at the counter.',
      );

  @override
  Future<void> recordVisit(String customerId) async {}
}

/// A source whose read never lands, so the loading state can be observed.
class _PendingHomeRepository implements HomeRepository {
  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) =>
      Completer<HomeSnapshot>().future;

  @override
  Future<void> completeTask(FocusTask task) async {}

  @override
  Future<ClientHighlight> createWalkIn(String fullName) async =>
      throw UnimplementedError();

  @override
  Future<void> recordVisit(String customerId) async {}
}

/// A source that refuses, so the error state can be observed.
class _FailingHomeRepository implements HomeRepository {
  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) async =>
      throw StateError('offline');

  @override
  Future<void> completeTask(FocusTask task) async {}

  @override
  Future<ClientHighlight> createWalkIn(String fullName) async =>
      throw UnimplementedError();

  @override
  Future<void> recordVisit(String customerId) async {}
}

const _wardrobeTask = FocusTask(
  id: 'w1',
  domain: FocusDomain.wardrobe,
  title: 'Count in the raw silk',
  detail: 'Four pieces below the reorder line.',
  timeLabel: '3:00 PM',
  actionLabel: 'Sign Off',
  doneMessage: 'Signed off.',
);

const _patronTask = FocusTask(
  id: 'p1',
  domain: FocusDomain.patron,
  title: "Prepare for Mrs. Silva's fitting",
  detail: 'Tomorrow, 10:00 AM.',
  timeLabel: '4:00 PM',
  actionLabel: 'Mark ready',
  doneMessage: 'Ready.',
);

const _testBalance = BlossomUsage(
  used: 40,
  allowance: 200,
  renewsOn: '1 October',
);

const _testSnapshot = HomeSnapshot(
  tasks: [_wardrobeTask, _patronTask],
  clients: [
    ClientHighlight(
      id: 'c1',
      name: 'Eleanor Vane',
      tier: ClientTier.vip,
      activity: 'Asked for the ivory silk to be held.',
    ),
  ],
  balance: _testBalance,
);

/// Pumps a Home bed and lets the controller's first read land, so a test asserts
/// against the loaded screen rather than against its loading state.
Future<void> _pump(WidgetTester tester, Widget widget) async {
  await tester.pumpWidget(widget);
  await tester.pump();
}

/// Reduced motion is on, matching the rest of the suite: Home carries ambient
/// animation (the veil, the client status card) that would never settle
/// otherwise, and a rotating page is not what these tests are about.
///
/// Three providers are mounted because the screen reads its identity from
/// `AuthRepository`, its permissions from `UserProvider` and its data from
/// `HomeController`; the fixtures below stand in for the API the controller will
/// read from S3 onward.
Widget _wrap({
  String? firstName,
  String? userRole,
  String? orgRole = AppRoles.boutiqueStaff,
  DateTime? now,
  HomeSnapshot? snapshot,
  HomeRepository? repository,
}) {
  final owner = AppRoles.isOwnerRole(userRole ?? '');
  final source = repository ??
      _StubHomeRepository(
        snapshot ??
            HomeSnapshot(
              tasks: demoFocusTasks(isOwner: owner),
              clients: demoClientHighlights(),
              balance: demoBlossomUsage,
            ),
      );

  final userProvider = UserProvider()
    ..setUser(
      AvelineUser(
        id: 'u1',
        clerkId: 'clk_u1',
        email: 'nadia@example.com',
        firstName: firstName ?? '',
        lastName: 'Silva',
        username: 'nadia',
        userRole: userRole ?? '',
        organizationRole: orgRole ?? '',
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
      Provider<AuthRepository>.value(
        value: _FakeAuthRepository(firstName: firstName, userRole: userRole),
      ),
      ChangeNotifierProvider<UserProvider>.value(value: userProvider),
      ChangeNotifierProvider<HomeController>.value(
        value: HomeController(source),
      ),
    ],
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
      await _pump(tester, 
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
      await _pump(tester, 
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
      await _pump(tester, 
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 14, 5)),
      );
      expect(_greetingText(tester), 'Good afternoon, Nadia.');

      await _pump(tester, 
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 19, 5)),
      );
      expect(_greetingText(tester), 'Good evening, Nadia.');
    });

    testWidgets('falls back to a bare salutation without a first name',
        (tester) async {
      await _pump(tester, 
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
      await _pump(tester, 
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      expect(find.text('AVELINE'), findsNothing);
    });
  });

  group('HomeScreen sections', () {
    testWidgets('replaces the placeholder cards with the focus deck',
        (tester) async {
      _usePhoneSurface(tester);
      await _pump(tester, 
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
      await _pump(tester, 
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
      await _pump(tester, 
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
      await _pump(tester, 
        _wrap(firstName: 'Nadia', now: DateTime(2026, 1, 1, 8)),
      );

      await _scrollTo(tester, find.text('DIRECT CLIENT LINK'));

      expect(find.text('DIRECT CLIENT LINK'), findsOneWidget);
      expect(find.byType(ClientLinkSection), findsOneWidget);
      expect(find.text('Add New'), findsOneWidget);
      expect(find.text('See all'), findsOneWidget);
    });

    testWidgets('hides the direct client link without customers:view', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await _pump(tester, 
        _wrap(
          firstName: 'Nadia',
          userRole: AppRoles.staff,
          orgRole: '',
          now: DateTime(2026, 1, 1, 8),
        ),
      );

      // The row needs customers:view, which a plain staff account does not hold:
      // rendering it would only promise a call that can 403.
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -1600));
      await tester.pumpAndSettle();

      expect(find.text('DIRECT CLIENT LINK'), findsNothing);
      expect(find.byType(ClientLinkSection), findsNothing);
    });

    testWidgets('shows the direct client link with customers:view', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await _pump(tester, 
        _wrap(
          firstName: 'Nadia',
          userRole: AppRoles.staff,
          orgRole: AppRoles.boutiqueStaff,
          now: DateTime(2026, 1, 1, 8),
        ),
      );

      await _scrollTo(tester, find.text('DIRECT CLIENT LINK'));

      expect(find.text('DIRECT CLIENT LINK'), findsOneWidget);
      expect(find.byType(ClientLinkSection), findsOneWidget);
    });

    testWidgets('ends with the Blossom meter', (tester) async {
      _usePhoneSurface(tester);
      await _pump(tester, 
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

        await _pump(tester, 
          _wrap(firstName: 'Nadia', userRole: AppRoles.owner),
        );
        expect(
          find.text('Approve delivery courier for Mrs. Silva'),
          findsOneWidget,
        );

        await _pump(tester, 
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
      await _pump(tester, 
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

      await _pump(tester, 
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

      await _pump(tester, 
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

  group('HomeScreen states', () {
    testWidgets('renders the controller snapshot rather than demo data', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await _pump(tester, _wrap(firstName: 'Nadia', snapshot: _testSnapshot));

      expect(find.text('Count in the raw silk'), findsOneWidget);
      expect(find.text('2 left'), findsOneWidget);
      // The meter reads the snapshot's balance, not the demo constant.
      await _scrollTo(tester, find.text('BLOSSOM USAGE'));
      expect(find.text('160'), findsOneWidget);
      // The demo deck is not the source any more.
      expect(
        find.text('Acknowledge the floor plan for today'),
        findsNothing,
      );
    });

    testWidgets('shows the loading card before the first read lands', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(firstName: 'Nadia', repository: _PendingHomeRepository()),
      );
      // The load is started after the first frame; this pump lets it begin.
      await tester.pump();

      expect(find.byKey(const Key('home_loading')), findsOneWidget);
      expect(
        find.text('Acknowledge the floor plan for today'),
        findsNothing,
      );
      expect(find.byType(FocusDeck), findsNothing);
    });

    testWidgets('shows an error state without falling back to demo data', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await _pump(
        tester,
        _wrap(firstName: 'Nadia', repository: _FailingHomeRepository()),
      );

      expect(find.byKey(const Key('home_error')), findsOneWidget);
      expect(find.text('Try again'), findsOneWidget);
      expect(
        find.text('Acknowledge the floor plan for today'),
        findsNothing,
      );
      expect(find.byType(BlossomUsageCard), findsNothing);
    });

    testWidgets('retrying loads the screen again', (tester) async {
      _usePhoneSurface(tester);
      await _pump(
        tester,
        _wrap(firstName: 'Nadia', repository: _FailingHomeRepository()),
      );
      expect(find.byKey(const Key('home_error')), findsOneWidget);

      await tester.tap(find.text('Try again'));
      await tester.pump();
      await tester.pump();

      // The same failing source is asked again; the state is honest about it.
      expect(find.byKey(const Key('home_error')), findsOneWidget);
    });

    testWidgets('hides the Blossom meter without the balance grant', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await _pump(
        tester,
        _wrap(
          firstName: 'Nadia',
          userRole: AppRoles.staff,
          orgRole: '',
          snapshot: _testSnapshot,
        ),
      );

      await tester.drag(find.byType(Scrollable).first, const Offset(0, -2400));
      await tester.pumpAndSettle();

      expect(find.byType(BlossomUsageCard), findsNothing);
    });

    testWidgets('shows the Blossom meter with the balance grant', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      await _pump(tester, _wrap(firstName: 'Nadia', snapshot: _testSnapshot));

      await _scrollTo(tester, find.text('BLOSSOM USAGE'));

      expect(find.byType(BlossomUsageCard), findsOneWidget);
    });
  });
}
