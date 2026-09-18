import 'package:aveline_mobile/core/navigation/staff_screens.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/conversations/data/conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/presentation/screens/conversations_screen.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/conversation_tile.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The inbox the screen is driven against.
///
/// An explicit fake rather than the demo seed (D5): the screen is what is under
/// test, and every row it draws is named here so an assertion says which thread
/// it means. The demo repository keeps its own contract test in
/// `demo_conversation_repository_test.dart`.
class _FakeInbox implements ConversationRepository {
  _FakeInbox(
    this.items, {
    this.latency = Duration.zero,
    this.pageSize = 50,
    int? total,
  }) : total = total ?? items.length;

  List<Conversation> items;
  Duration latency;
  int pageSize;

  /// What the server says the whole inbox holds, which can exceed what is loaded.
  int total;
  int failures = 0;
  final List<int> requestedPages = [];

  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async {
    requestedPages.add(page);
    if (latency > Duration.zero) {
      await Future<void>.delayed(latency);
    }
    if (failures > 0) {
      failures--;
      throw Exception('The inbox is unavailable.');
    }
    final start = (page - 1) * pageSize;
    final slice = start >= items.length
        ? <Conversation>[]
        : items.sublist(start, (start + pageSize).clamp(0, items.length));
    return ConversationPage(
      items: List.unmodifiable(slice),
      total: total,
      page: page,
      pageSize: pageSize,
    );
  }
}

/// Where a thread stood [age] ago, measured from the wall clock so the ages on
/// screen read the way the row prints them.
DateTime _at(Duration age) => DateTime.now().toUtc().subtract(age);

/// A boutique's worth of threads: a pinned Salon, seven clients of differing
/// recency, rows wearing each marker in the vocabulary and a notice.
List<Conversation> _seed() => [
  Conversation(
    id: 'cnv_salon',
    kind: ConversationKind.aveline,
    threadId: 'thread_salon',
    lastMessageAt: _at(const Duration(minutes: 8)),
    lastMessagePreview:
        'Hasini de Silva has not been in for 96 days. Shall I draft a note for her?',
    lastMessageAuthor: ConversationAuthor.agent,
  ),
  Conversation(
    id: 'cnv_nadeesha',
    kind: ConversationKind.customer,
    customerId: 'cus_204',
    customerName: 'Nadeesha Perera',
    threadId: 'thread_204',
    lastMessageAt: _at(const Duration(minutes: 4)),
    lastMessagePreview:
        'Can the wine silk saree be taken in before Friday evening?',
    lastMessageAuthor: ConversationAuthor.customer,
  ),
  Conversation(
    id: 'cnv_chathurika',
    kind: ConversationKind.customer,
    customerId: 'cus_118',
    customerName: 'Chathurika Silva',
    threadId: 'thread_118',
    lastMessageAt: _at(const Duration(minutes: 40)),
    lastMessagePreview:
        'The blouse is pinned and ready for tomorrow\u2019s 10:30 fitting.',
    lastMessageAuthor: ConversationAuthor.staff,
    markers: const [ConversationMarker.draft],
  ),
  Conversation(
    id: 'cnv_menaka',
    kind: ConversationKind.customer,
    customerId: 'cus_311',
    customerName: 'Menaka Rathnayake',
    status: ConversationStatus.awaitingSignOff,
    threadId: 'thread_311',
    lastMessageAt: _at(const Duration(hours: 2)),
    lastMessagePreview:
        'The 12% goodwill discount on order #4821 needs a signature before I can release it.',
    lastMessageAuthor: ConversationAuthor.agent,
    markers: const [ConversationMarker.approval],
  ),
  Conversation(
    id: 'cnv_kasun',
    kind: ConversationKind.customer,
    customerId: 'cus_233',
    customerName: 'Kasun Bandara',
    threadId: 'thread_233',
    lastMessageAt: _at(const Duration(hours: 3)),
    lastMessagePreview: 'Does this one come in a second colour?',
    lastMessageAuthor: ConversationAuthor.customer,
    markers: const [ConversationMarker.choice],
  ),
  Conversation(
    id: 'cnv_ava',
    kind: ConversationKind.customer,
    customerId: 'cus_142',
    customerName: 'Ishara Weerasinghe',
    threadId: 'thread_142',
    lastMessageAt: _at(const Duration(minutes: 25)),
    lastMessagePreview:
        'I have put together a reply about the ivory organza.',
    lastMessageAuthor: ConversationAuthor.agent,
    lastMessageAgentKey: 'ava',
  ),
  Conversation(
    id: 'cnv_hasini',
    kind: ConversationKind.customer,
    customerId: 'cus_091',
    customerName: 'Hasini de Silva',
    threadId: 'thread_091',
    lastMessageAt: _at(const Duration(days: 1, hours: 2)),
    lastMessagePreview: 'I have put the evening wear aside for your next visit.',
    lastMessageAuthor: ConversationAuthor.staff,
  ),
  Conversation(
    id: 'cnv_amaya',
    kind: ConversationKind.customer,
    customerId: 'cus_045',
    customerName: 'Amaya Fernando',
    threadId: 'thread_045',
    lastMessageAt: _at(const Duration(days: 5)),
    lastMessagePreview: 'Thank you, the alterations are perfect.',
    lastMessageAuthor: ConversationAuthor.customer,
  ),
  Conversation(
    id: 'cnv_digest',
    kind: ConversationKind.system,
    threadId: 'thread_digest',
    lastMessageAt: _at(const Duration(days: 6)),
    lastMessagePreview:
        'Your month at the boutique: 42 clients served, 6 to win back.',
    lastMessageAuthor: ConversationAuthor.system,
  ),
];

class _EmptyInbox implements ConversationRepository {
  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async =>
      ConversationPage.empty;
}

/// An inbox whose organization context has not arrived yet.
class _WaitingInbox implements ConversationRepository {
  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async {
    throw const OrgContextUnavailable();
  }
}

/// A phone-shaped viewport with reduced motion on, matching the rest of the
/// suite: the backdrop carries ambient animation that would never settle
/// otherwise.
Widget _wrap(
  ConversationRepository repository, {
  String boutiqueName = 'Ceylon Atelier',
  VoidCallback? onOpenAveline,
  void Function(Conversation)? onOpenConversation,
}) {
  return MaterialApp(
    theme: AppTheme.light,
    home: MediaQuery(
      data: const MediaQueryData(disableAnimations: true, size: Size(390, 844)),
      child: Scaffold(
        body: ConversationsScreen(
          repository: repository,
          boutiqueName: boutiqueName,
          onOpenAveline: onOpenAveline,
          onOpenConversation: onOpenConversation,
        ),
      ),
    ),
  );
}

Finder _avelineRow() => find.byKey(const Key('conversations_aveline_row'));
Finder _tile(String id) => find.byKey(Key('conversation_tile_$id'));

Future<void> _open(
  WidgetTester tester, {
  ConversationRepository? repository,
  VoidCallback? onOpenAveline,
  void Function(Conversation)? onOpenConversation,
}) async {
  await tester.pumpWidget(
    _wrap(
      repository ?? _FakeInbox(_seed()),
      onOpenAveline: onOpenAveline,
      onOpenConversation: onOpenConversation,
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('ConversationsScreen inbox', () {
    testWidgets('names the boutique and the section in the brand serif', (tester) async {
      await _open(tester);

      final title = tester.widget<Text>(
        find.byKey(const Key('conversations_title')),
      );
      expect(title.data, 'Ceylon Atelier - Messages');
      expect(title.style?.fontFamily, startsWith('PlayfairDisplay'));
    });

    testWidgets('pins the Salon above every client thread', (tester) async {
      await _open(tester);

      expect(_avelineRow(), findsOneWidget);

      // Above, not merely present: the pinned card's top is the first thing in
      // the column, and the client rows all begin below it.
      final salonY = tester.getTopLeft(_avelineRow()).dy;
      for (final id in ['cnv_nadeesha', 'cnv_chathurika', 'cnv_menaka']) {
        expect(
          tester.getTopLeft(_tile(id)).dy,
          greaterThan(salonY),
          reason: '$id should sit below the pinned Salon',
        );
      }
    });

    testWidgets('lists the client threads newest first', (tester) async {
      await _open(tester);

      // Read off the rendered column rather than off the controller: the order
      // is what the associate sees. Nadeesha spoke four minutes ago, Chathurika
      // forty, Menaka two hours.
      final nadeesha = tester.getTopLeft(_tile('cnv_nadeesha')).dy;
      final chathurika = tester.getTopLeft(_tile('cnv_chathurika')).dy;
      final menaka = tester.getTopLeft(_tile('cnv_menaka')).dy;

      expect(nadeesha, lessThan(chathurika));
      expect(chathurika, lessThan(menaka));
    });

    testWidgets('names the group of threads under the pinned card', (tester) async {
      await _open(tester);

      expect(
        find.byKey(const Key('conversations_clients_overline')),
        findsOneWidget,
      );
      expect(find.text('CLIENTS'), findsOneWidget);
    });

    testWidgets('wears the client, the last word and the time it landed', (tester) async {
      await _open(tester);

      expect(find.text('Nadeesha Perera'), findsOneWidget);
      expect(
        find.textContaining('Can the wine silk saree be taken in'),
        findsOneWidget,
      );
      // The row prints how long ago the last word landed, from the thread's own
      // timestamp. The exact wording is `relativeMoment`'s and has its own tests;
      // what matters here is that the row is wired to it, and that the age is in
      // minutes rather than in the far past.
      expect(find.textContaining('m ago'), findsWidgets);
    });

    testWidgets('says who spoke last, so a preview cannot be misread', (tester) async {
      await _open(tester);

      // The associate's own last word is prefixed, the way a message inbox does
      // it, so "the blouse is pinned" is not read as the client saying it.
      expect(find.textContaining('You: The blouse is pinned'), findsOneWidget);
      // An agent with no persona key falls back to the umbrella brand.
      expect(
        find.textContaining('Aveline: The 12% goodwill discount'),
        findsOneWidget,
      );
      // An agent that names its persona is credited to that persona, not to the
      // umbrella brand.
      expect(
        find.textContaining('Ava: I have put together a reply'),
        findsOneWidget,
      );
      expect(
        find.textContaining('Aveline: I have put together a reply'),
        findsNothing,
      );
    });

    testWidgets('leaves a client\u2019s own words unprefixed', (tester) async {
      await _open(tester);

      expect(
        find.textContaining('Can the wine silk saree be taken in'),
        findsOneWidget,
      );
      expect(
        find.textContaining('You: Can the wine silk saree'),
        findsNothing,
      );
    });

    testWidgets('wears the marker the server flagged on the row', (tester) async {
      await _open(tester);

      expect(
        find.byKey(const Key('conversation_marker_cnv_menaka')),
        findsOneWidget,
      );
      expect(find.text('Approval'), findsOneWidget);

      // The marker is a set derived server-side: every vocabulary member renders.
      expect(
        find.byKey(const Key('conversation_marker_cnv_chathurika')),
        findsOneWidget,
      );
      expect(find.text('Draft ready to copy'), findsOneWidget);
      expect(
        find.byKey(const Key('conversation_marker_cnv_kasun')),
        findsOneWidget,
      );
      expect(find.text('Pick the client'), findsOneWidget);

      // The old sign-off key, keyed off the conversation status, is gone.
      expect(
        find.byKey(const Key('conversation_signoff_cnv_menaka')),
        findsNothing,
      );
    });

    testWidgets('a row the server flagged nothing on wears no marker', (tester) async {
      await _open(tester);

      for (final id in ['cnv_nadeesha', 'cnv_ava', 'cnv_hasini']) {
        expect(
          find.byKey(Key('conversation_marker_$id')),
          findsNothing,
          reason: '$id should wear no marker',
        );
      }
    });
  });

  group('ConversationsScreen truthfulness (D2 = c)', () {
    testWidgets('wears no unread summary', (tester) async {
      await _open(tester);

      expect(
        find.byKey(const Key('conversations_unread_summary')),
        findsNothing,
      );
      expect(find.text('All caught up'), findsNothing);
      expect(find.textContaining('unread'), findsNothing);
    });

    testWidgets('wears no unread badge on any row', (tester) async {
      await _open(tester);

      expect(
        find.byKey(const Key('conversations_aveline_unread')),
        findsNothing,
      );
      for (final id in [
        'cnv_nadeesha',
        'cnv_chathurika',
        'cnv_menaka',
        'cnv_kasun',
      ]) {
        expect(
          find.byKey(Key('conversation_unread_$id')),
          findsNothing,
          reason: '$id still wears an unread badge',
        );
      }
    });

    testWidgets('the registry-built screen renders the empty state, not a seed', (tester) async {
      // The drawer builds `const ConversationsScreen()` with no injection point
      // (D5): with no repository supplied it must show the honest empty state
      // rather than eight invented threads.
      final registry = staffScreens().firstWhere(
        (screen) => screen.id == 'conversations',
      );

      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: MediaQuery(
            data: const MediaQueryData(
              disableAnimations: true,
              size: Size(390, 844),
            ),
            child: Scaffold(body: Builder(builder: registry.builder)),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_empty')), findsOneWidget);
      expect(_avelineRow(), findsNothing);
      expect(find.text('Nadeesha Perera'), findsNothing);
    });
  });

  group('ConversationsScreen search', () {
    testWidgets('narrows the clients as the field is typed in', (tester) async {
      await _open(tester);

      await tester.enterText(
        find.byKey(const Key('conversations_search_field')),
        'nadeesha',
      );
      await tester.pumpAndSettle();

      expect(find.text('Nadeesha Perera'), findsOneWidget);
      expect(find.text('Kasun Bandara'), findsNothing);
    });

    testWidgets('matches a word from the last message too', (tester) async {
      await _open(tester);

      await tester.enterText(
        find.byKey(const Key('conversations_search_field')),
        'second colour',
      );
      await tester.pumpAndSettle();

      expect(find.text('Kasun Bandara'), findsOneWidget);
      expect(find.text('Nadeesha Perera'), findsNothing);
    });

    testWidgets('keeps the Salon pinned while the inbox is narrowed', (tester) async {
      await _open(tester);

      await tester.enterText(
        find.byKey(const Key('conversations_search_field')),
        'nadeesha',
      );
      await tester.pumpAndSettle();

      expect(_avelineRow(), findsOneWidget);
    });

    testWidgets('offers a way back when nothing matches', (tester) async {
      await _open(tester);

      await tester.enterText(
        find.byKey(const Key('conversations_search_field')),
        'nobody at all',
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_no_matches')), findsOneWidget);
      // The pinned Salon is not a search result, so it stays put even when the
      // search finds nobody.
      expect(_avelineRow(), findsOneWidget);

      await tester.tap(find.byKey(const Key('conversations_search_clear')));
      await tester.pumpAndSettle();

      expect(find.text('Kasun Bandara'), findsOneWidget);
      expect(find.byKey(const Key('conversations_no_matches')), findsNothing);
    });
  });

  group('ConversationsScreen states', () {
    testWidgets('shows a loading state before the first read lands', (tester) async {
      await tester.pumpWidget(
        _wrap(_FakeInbox(_seed(), latency: const Duration(milliseconds: 200))),
      );
      await tester.pump();

      expect(find.byKey(const Key('conversations_loading')), findsOneWidget);

      await tester.pump(const Duration(milliseconds: 300));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_loading')), findsNothing);
      expect(_avelineRow(), findsOneWidget);
    });

    testWidgets('an inbox with nothing in it explains itself', (tester) async {
      await tester.pumpWidget(_wrap(_EmptyInbox()));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_empty')), findsOneWidget);
      expect(find.text('No conversations yet'), findsOneWidget);
      expect(_avelineRow(), findsNothing);
    });

    testWidgets('an inbox waiting for its org context keeps loading, not an error', (tester) async {
      // A null org id is a "not yet": the error card is the server's 403, and the
      // two must not read the same.
      await tester.pumpWidget(_wrap(_WaitingInbox()));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('conversations_loading')), findsOneWidget);
      expect(find.byKey(const Key('conversations_error')), findsNothing);
      expect(find.byKey(const Key('conversations_empty')), findsNothing);
    });

    testWidgets('a failed read offers a retry that works', (tester) async {
      await tester.pumpWidget(_wrap(_FakeInbox(_seed())..failures = 1));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_error')), findsOneWidget);
      expect(find.textContaining('unavailable'), findsOneWidget);

      await tester.tap(find.byKey(const Key('conversations_retry')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_error')), findsNothing);
      expect(_avelineRow(), findsOneWidget);
    });

    testWidgets('files the notices under their own overline at the foot', (tester) async {
      await _open(tester);

      await tester.scrollUntilVisible(
        find.byKey(const Key('conversations_end_of_list')),
        300,
        scrollable: find.byType(Scrollable).first,
      );

      expect(find.byKey(const Key('conversations_end_of_list')), findsOneWidget);
      expect(
        find.byKey(const Key('conversations_announcements_overline')),
        findsOneWidget,
      );
      expect(_tile('cnv_digest'), findsOneWidget);
      // Last, not merely present.
      expect(
        tester.getTopLeft(_tile('cnv_digest')).dy,
        greaterThan(tester.getTopLeft(_tile('cnv_amaya')).dy),
      );
    });
  });

  group('ConversationsScreen opening a thread', () {
    testWidgets('the pinned card opens the Salon', (tester) async {
      var opened = false;
      await _open(tester, onOpenAveline: () => opened = true);

      await tester.tap(_avelineRow());
      await tester.pumpAndSettle();

      expect(opened, isTrue);
    });

    testWidgets('a client row hands back the thread it stands for', (tester) async {
      Conversation? opened;
      await _open(tester, onOpenConversation: (item) => opened = item);

      await tester.tap(_tile('cnv_nadeesha'));
      await tester.pumpAndSettle();

      expect(opened, isNotNull);
      expect(opened!.customerId, 'cus_204');
      expect(opened!.isAveline, isFalse);
    });
  });

  group('ConversationsScreen paging', () {
    Finder loadMore() => find.byKey(const Key('conversations_load_more'));
    Finder endOfList() => find.byKey(const Key('conversations_end_of_list'));

    testWidgets('the footer counts what is loaded against the whole inbox', (tester) async {
      // The server already sends `total`; claiming the list is complete while it
      // is not is the lie this slice removes.
      await _open(
        tester,
        repository: _FakeInbox(_seed(), pageSize: 3, total: 137),
      );

      await tester.scrollUntilVisible(
        loadMore(),
        300,
        scrollable: find.byType(Scrollable).first,
      );

      expect(find.text('Showing 3 of 137'), findsOneWidget);
      expect(endOfList(), findsNothing);
    });

    testWidgets('load-more reaches the end and the footer says so', (tester) async {
      final inbox = _FakeInbox(_seed(), pageSize: 3, total: 5);
      await _open(tester, repository: inbox);

      await tester.scrollUntilVisible(
        loadMore(),
        300,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('Showing 3 of 5'), findsOneWidget);

      await tester.tap(loadMore());
      await tester.pumpAndSettle();

      expect(inbox.requestedPages, [1, 2]);
      expect(loadMore(), findsNothing);

      await tester.scrollUntilVisible(
        endOfList(),
        300,
        scrollable: find.byType(Scrollable).first,
      );
      expect(endOfList(), findsOneWidget);
      expect(find.text('That is every conversation.'), findsOneWidget);
    });

    testWidgets('a search says it only covers what is loaded', (tester) async {
      await _open(
        tester,
        repository: _FakeInbox(_seed(), pageSize: 3, total: 137),
      );

      await tester.enterText(
        find.byKey(const Key('conversations_search_field')),
        'nobody at all',
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('conversations_no_matches')), findsOneWidget);
      expect(find.textContaining('loaded'), findsOneWidget);
    });
  });

  group('ConversationTile preview attribution', () {
    Future<void> pumpTile(WidgetTester tester, Conversation conversation) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(body: ConversationTile(conversation: conversation)),
        ),
      );
    }

    testWidgets('credits the persona key however its case is written', (tester) async {
      await pumpTile(
        tester,
        const Conversation(
          id: 'cnv_upper',
          kind: ConversationKind.customer,
          customerId: 'cus_1',
          lastMessagePreview: 'A draft is ready.',
          lastMessageAuthor: ConversationAuthor.agent,
          lastMessageAgentKey: 'AVA',
        ),
      );

      expect(find.text('Ava: A draft is ready.'), findsOneWidget);
    });

    testWidgets('falls back to the umbrella brand for an unknown key', (tester) async {
      await pumpTile(
        tester,
        const Conversation(
          id: 'cnv_unknown',
          kind: ConversationKind.customer,
          customerId: 'cus_1',
          lastMessagePreview: 'A draft is ready.',
          lastMessageAuthor: ConversationAuthor.agent,
          lastMessageAgentKey: 'nobody',
        ),
      );

      expect(find.text('Aveline: A draft is ready.'), findsOneWidget);
    });

    testWidgets('leaves a thread with nothing said yet honest', (tester) async {
      await pumpTile(
        tester,
        const Conversation(
          id: 'cnv_silent',
          kind: ConversationKind.customer,
          customerId: 'cus_1',
          lastMessagePreview: '',
          lastMessageAuthor: ConversationAuthor.customer,
        ),
      );

      expect(find.text('No messages yet'), findsOneWidget);
    });
  });
}
