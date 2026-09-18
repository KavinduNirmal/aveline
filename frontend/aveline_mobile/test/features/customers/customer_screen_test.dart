import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/customers/presentation/screens/customer_screen.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_level_badge.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

/// A client with everything the concierge module can hold, so every card has
/// something in it.
CustomerDetail _detail() {
  return CustomerDetail(
    customer: Customer(
      id: 'CUS-9001',
      organizationId: 'org-1',
      phoneNumber: '+94 77 641 2098',
      level: CustomerLevel.vip,
      status: CustomerStatus.vip,
      fullName: 'Chamari Silva',
      nickname: 'Chami',
      email: 'chamari@example.com',
      totalSpent: 512000,
      visitCount: 14,
      lastVisitAtUtc: DateTime.now().toUtc().subtract(const Duration(days: 2)),
      createdAtUtc: DateTime.now().toUtc().subtract(const Duration(days: 400)),
      tags: const {'bridal', 'atelier-pick'},
    ),
    preferences: const [
      CustomerPreference(
        id: 'pref-colour',
        key: 'Colour',
        value: 'Wine',
        isExplicit: true,
        confidence: 0.9,
      ),
      CustomerPreference(
        id: 'pref-fabric',
        key: 'Fabric',
        value: 'Raw silk',
        isExplicit: false,
        confidence: 0.6,
      ),
    ],
    consent: CustomerConsent(
      status: ConsentStatus.granted,
      grantedAtUtc: DateTime.utc(2026, 8, 3, 12),
    ),
    memories: [
      CustomerMemory(
        id: 'mem-1',
        content: 'Prefers ivory and dislikes heavy zari.',
        category: MemoryCategory.preference,
        source: MemorySource.conversation,
        isExplicit: true,
        confidence: 0.92,
        createdAtUtc: DateTime.utc(2026, 9, 1, 12),
      ),
      CustomerMemory(
        id: 'mem-2',
        content: 'Usually shops with her daughter.',
        category: MemoryCategory.fact,
        source: MemorySource.inferred,
        confidence: 0.52,
        createdAtUtc: DateTime.utc(2026, 8, 20, 12),
      ),
    ],
    events: [
      CustomerEvent(
        id: 'evt-1',
        type: CustomerEventType.wedding,
        dateUtc: DateTime.now().toUtc().add(const Duration(days: 12)),
        description: 'Wants a reception saree.',
      ),
      CustomerEvent(
        id: 'evt-2',
        type: CustomerEventType.birthday,
        dateUtc: DateTime.now().toUtc().subtract(const Duration(days: 40)),
        isActive: false,
      ),
    ],
    interactions: [
      CustomerInteraction(
        id: 'int-1',
        channel: InteractionChannel.whatsapp,
        direction: InteractionDirection.inbound,
        messageContent: 'Is the wine raw silk back in stock yet?',
        createdAtUtc: DateTime.now().toUtc().subtract(const Duration(hours: 3)),
      ),
      CustomerInteraction(
        id: 'int-2',
        channel: InteractionChannel.inPerson,
        direction: InteractionDirection.outbound,
        messageContent: 'Held the ivory organza until Friday.',
        createdAtUtc: DateTime.now().toUtc().subtract(const Duration(days: 6)),
      ),
    ],
  );
}

/// Serves one client, and refuses to serve anyone else.
class _StubRepository implements CustomerRepository {
  _StubRepository(this.detail);

  final CustomerDetail? detail;
  int calls = 0;

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    calls++;
    final found = detail;
    return found != null && found.customer.id == id ? found : null;
  }

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async => CustomerBook.empty;
}

/// A source that fails the first call and answers afterwards.
class _FlakyRepository implements CustomerRepository {
  _FlakyRepository(this.detail);

  final CustomerDetail detail;
  int failures = 1;
  int calls = 0;

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    calls++;
    if (failures > 0) {
      failures--;
      throw Exception('The concierge is unavailable.');
    }
    return detail;
  }

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async => CustomerBook.empty;
}

/// A phone-shaped viewport: the profile is a column of cards, and the cards have
/// to lay out the way they do on the devices this app ships to.
void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

Future<void> _pumpProfile(
  WidgetTester tester, {
  String customerId = 'CUS-9001',
  CustomerDetail? seeded,
  CustomerRepository? repository,
}) async {
  _usePhoneSurface(tester);
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: CustomerScreen(
            customerId: customerId,
            customer: seeded,
            repository: repository ?? _StubRepository(seeded ?? _detail()),
          ),
        ),
      ),
    ),
  );
  await tester.pump();
  await tester.pump();
}

/// Scrolls the profile until [finder] has been laid out, and settles the frame.
///
/// `scrollUntilVisible` moves the scroll position but returns without pumping a
/// final frame, so the geometry a later `tap` reads is still the layout from
/// before the scroll. Pumping here is what makes a control found by scrolling
/// answer a tap where it is now rather than where it used to be.
Future<void> _scrollTo(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(
    finder,
    300,
    scrollable: find.descendant(
      of: find.byKey(const Key('customer_scroll')),
      matching: find.byType(Scrollable),
    ),
  );
  await tester.pump();
}

/// Asserts that [earlier] comes before [later] in the profile's scroll view.
///
/// Read as document order rather than as screen position. Once the profile is
/// scrolled to one of the two, the other is above the cache extent and the list
/// has recycled it, so it has no position left to compare; the order it was
/// built in is the same fact and does not depend on how far the test scrolled.
void _expectBefore(WidgetTester tester, Finder earlier, Finder later) {
  final children = find.descendant(
    of: find.byKey(const Key('customer_scroll')),
    matching: find.byWidgetPredicate((widget) => true),
    matchRoot: true,
  );
  final elements = children.evaluate().toList();
  final earlierIndex = elements.indexWhere(
    (element) => earlier.evaluate().contains(element),
  );
  final laterIndex = elements.indexWhere(
    (element) => later.evaluate().contains(element),
  );

  expect(earlierIndex, isNonNegative, reason: 'the earlier widget was not found');
  expect(laterIndex, isNonNegative, reason: 'the later widget was not found');
  expect(earlierIndex, lessThan(laterIndex));
}

void main() {
  group('CustomerScreen identity', () {
    testWidgets('names the client, their id and their grade', (tester) async {
      await _pumpProfile(tester);

      expect(find.byKey(const Key('customer_identity')), findsOneWidget);
      expect(
        tester.widget<Text>(find.byKey(const Key('customer_name'))).data,
        'Chamari Silva',
      );
      expect(
        tester.widget<Text>(find.byKey(const Key('customer_id'))).data,
        'CUS-9001',
      );

      // One grade and only one. The profile also carries `Customer.level`, but
      // that is a grade the mobile book invents; the API derives a status, and a
      // client cannot hold two grades at once.
      final grade = find.byKey(const Key('customer_grade'));
      expect(grade, findsOneWidget);
      expect(
        tester
            .widgetList<Text>(
              find.descendant(of: grade, matching: find.byType(Text)),
            )
            .map((text) => text.data)
            .whereType<String>()
            .join(),
        'VIP',
      );
      expect(find.byType(CustomerLevelBadge), findsNothing);
    });

    testWidgets('says which nickname the counter uses', (tester) async {
      await _pumpProfile(tester);

      expect(find.text('Called "Chami" at the counter'), findsOneWidget);
    });
  });

  group('CustomerScreen facts', () {
    testWidgets('leads with the numbers the floor works from', (tester) async {
      await _pumpProfile(tester);

      String metric(String key) => tester
          .widgetList<Text>(
            find.descendant(
              of: find.byKey(ValueKey('customer_metric_$key')),
              matching: find.byType(Text),
            ),
          )
          .map((text) => text.data)
          .whereType<String>()
          .join(' | ');

      expect(metric('spend'), contains('Rs 512,000'));
      expect(metric('visits'), contains('14'));
      expect(metric('visits'), contains('VISITS'));
      // Recency is what an associate reads, so the figure is relative and the
      // date sits under it.
      expect(metric('last-visit'), contains('days ago'));
    });

    testWidgets('reads the contact details', (tester) async {
      await _pumpProfile(tester);

      expect(
        find.descendant(
          of: find.byKey(const Key('customer_contact_phone')),
          matching: find.text('+94 77 641 2098'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.byKey(const Key('customer_contact_email')),
          matching: find.text('chamari@example.com'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('shows consent as something to read, not to set', (
      tester,
    ) async {
      await _pumpProfile(tester);

      final consent = find.byKey(const Key('customer_consent'));
      expect(consent, findsOneWidget);
      expect(
        find.descendant(of: consent, matching: find.text('Consent · Granted')),
        findsOneWidget,
      );
      expect(
        find.descendant(of: consent, matching: find.text('Granted 3 Aug 2026')),
        findsOneWidget,
      );

      // The floor has no control for it: the API records consent from the
      // client's own channel.
      expect(
        find.descendant(of: consent, matching: find.byType(InkWell)),
        findsNothing,
      );
      expect(
        find.descendant(of: consent, matching: find.byType(IconButton)),
        findsNothing,
      );
    });
  });

  group('CustomerScreen preferences', () {
    testWidgets('lists what the boutique knows, and how it knows it', (
      tester,
    ) async {
      await _pumpProfile(tester);

      await _scrollTo(tester, find.byKey(const Key('customer_preferences')));

      expect(find.text('Colour'), findsOneWidget);
      expect(find.text('Wine'), findsOneWidget);
      expect(find.text('Stated 90%'), findsOneWidget);
      expect(find.text('Fabric'), findsOneWidget);
      // An inferred preference says so rather than reading as fact, and the
      // rail under it is only as full as the confidence behind it.
      expect(find.text('Inferred 60%'), findsOneWidget);

      final stated = tester.widget<LinearProgressIndicator>(
        find.descendant(
          of: find.byKey(const ValueKey('customer_preference_pref-colour')),
          matching: find.byType(LinearProgressIndicator),
        ),
      );
      expect(stated.value, 0.9);
    });

    testWidgets('shows the shop tags as read-only pills', (tester) async {
      await _pumpProfile(tester);
      await _scrollTo(tester, find.byKey(const Key('customer_tags')));

      expect(find.byKey(const ValueKey('customer_tag_bridal')), findsOneWidget);
      // The dash in the shop's own vocabulary is what the pill prints without.
      expect(find.text('atelier pick'), findsOneWidget);
    });
  });

  group('CustomerScreen occasions', () {
    testWidgets('gives the next occasion its own band, above everything', (
      tester,
    ) async {
      await _pumpProfile(tester);

      final banner = find.byKey(const ValueKey('customer_occasion_evt-1'));
      expect(banner, findsOneWidget);
      expect(
        find.descendant(of: banner, matching: find.text('Wedding')),
        findsOneWidget,
      );
      expect(
        find.descendant(of: banner, matching: find.text('in 12 days')),
        findsOneWidget,
      );

      // Ahead of the client's taste, their tags and their history: it is the one
      // thing on the page with a deadline.
      _expectBefore(
        tester,
        banner,
        find.byKey(const Key('customer_preferences')),
      );
      _expectBefore(
        tester,
        banner,
        find.byKey(const Key('customer_tags')),
      );
      _expectBefore(
        tester,
        banner,
        find.byKey(const Key('customer_activity')),
      );
    });

    testWidgets('keeps the occasions that are no longer ahead quiet', (
      tester,
    ) async {
      await _pumpProfile(tester);
      await _scrollTo(tester, find.byKey(const Key('customer_past_occasions')));

      final past = find.byKey(const ValueKey('customer_event_evt-2'));
      expect(past, findsOneWidget);
      expect(
        find.descendant(of: past, matching: find.text('Birthday')),
        findsOneWidget,
      );
      expect(find.textContaining('40 days ago'), findsOneWidget);
    });
  });

  group('CustomerScreen memories', () {
    testWidgets('says what Aveline remembers and how sure it is', (
      tester,
    ) async {
      await _pumpProfile(tester);
      await _scrollTo(tester, find.byKey(const Key('customer_memories')));

      expect(find.text('Prefers ivory and dislikes heavy zari.'), findsOneWidget);
      expect(find.text('Preference'), findsOneWidget);
      expect(
        find.text('Stated by the client · Conversation · 92%'),
        findsOneWidget,
      );
      expect(find.text('Inferred · Inferred · 52%'), findsOneWidget);
    });
  });

  group('CustomerScreen activity', () {
    testWidgets('lists the exchanges, newest first', (tester) async {
      await _pumpProfile(tester);
      await _scrollTo(tester, find.byKey(const Key('customer_activity')));

      expect(
        find.text('Is the wine raw silk back in stock yet?'),
        findsOneWidget,
      );
      expect(find.text('From the client · WhatsApp'), findsOneWidget);
      expect(find.text('From the boutique · In person'), findsOneWidget);

      final newest = tester.getTopLeft(
        find.byKey(const ValueKey('customer_interaction_int-1')),
      );
      final older = tester.getTopLeft(
        find.byKey(const ValueKey('customer_interaction_int-2')),
      );
      expect(newest.dy, lessThan(older.dy));
    });
  });

  group('CustomerScreen actions', () {
    testWidgets('adds a logged visit to the activity and to the count', (
      tester,
    ) async {
      await _pumpProfile(tester);

      await _scrollTo(tester, find.byKey(const Key('customer_action_visit')));
      await tester.tap(find.byKey(const Key('customer_action_visit')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.text('Visit added to Chamari Silva.'), findsOneWidget);
      await _scrollTo(tester, find.text('Walked in; logged at the counter.'));
      expect(
        find.text('Walked in; logged at the counter.'),
        findsOneWidget,
      );

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('recomputes the tier from spend and visits', (tester) async {
      await _pumpProfile(tester);

      await _scrollTo(tester, find.byKey(const Key('customer_action_tier')));
      await tester.tap(find.byKey(const Key('customer_action_tier')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      // 512,000 spent over 14 visits is a VIP by `CustomerLoyaltyService`'s own
      // rule, which is the rule this action runs.
      expect(find.text('Tier recomputed: VIP.'), findsOneWidget);

      await tester.pump(const Duration(seconds: 4));
    });

  });

  group('CustomerScreen resolution', () {
    testWidgets('resolves the profile from the id alone', (tester) async {
      await _pumpProfile(
        tester,
        repository: _StubRepository(_detail()),
      );

      expect(find.text('Chamari Silva'), findsWidgets);
      expect(find.byKey(const Key('customer_identity')), findsOneWidget);
    });

    testWidgets('stands on the id with a retry when the client is not found', (
      tester,
    ) async {
      await _pumpProfile(
        tester,
        customerId: 'CUS-9999',
        repository: _StubRepository(_detail()),
      );

      expect(find.text('Client not found'), findsOneWidget);
      expect(find.textContaining('CUS-9999'), findsWidgets);

      await tester.tap(find.byKey(const Key('customer_retry')));
      await tester.pump();
      await tester.pump();

      // Asking again does not invent a client who is not there.
      expect(find.text('Client not found'), findsOneWidget);
    });

    testWidgets('reports a failed load and retries it', (tester) async {
      final repository = _FlakyRepository(_detail());
      await _pumpProfile(
        tester,
        repository: repository,
      );

      expect(find.text('Client not found'), findsOneWidget);

      await tester.tap(find.byKey(const Key('customer_retry')));
      await tester.pump();
      await tester.pump();

      expect(repository.calls, 2);
      expect(find.byKey(const Key('customer_identity')), findsOneWidget);
    });

    testWidgets('survives a refresh from the router listenable', (
      tester,
    ) async {
      _usePhoneSurface(tester);

      final refresh = ChangeNotifier();
      addTearDown(refresh.dispose);

      final router = GoRouter(
        initialLocation: '/customers',
        // The app refreshes the router whenever Clerk, the profile or
        // onboarding notifies. This is that refresh.
        refreshListenable: refresh,
        routes: [
          GoRoute(
            path: '/customers',
            builder: (context, state) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () => context.push(AppRoutes.customer('CUS-9001')),
                  child: const Text('open client'),
                ),
              ),
            ),
          ),
          GoRoute(
            path: AppRoutes.customerPattern,
            builder: (context, state) => CustomerScreen(
              customerId: state.pathParameters['customerId'] ?? '',
              repository: _StubRepository(_detail()),
            ),
          ),
        ],
      );
      addTearDown(router.dispose);

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );
      await tester.pump();

      await tester.tap(find.text('open client'));
      // The route transition, on the same clock the other tests pump by hand.
      // `pumpAndSettle` cannot be used here: the profile draws the brand's own
      // backdrop, whose blossoms drift on a controller that repeats for as long
      // as the screen is alive, so there is no last frame to settle on.
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      expect(find.byKey(const Key('customer_identity')), findsOneWidget);

      refresh.notifyListeners();
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      // The id survives the refresh, and the profile is resolved from it rather
      // than from a route extra that does not survive.
      expect(find.byKey(const Key('customer_identity')), findsOneWidget);
      expect(find.text('Client not found'), findsNothing);
    });
  });
}
