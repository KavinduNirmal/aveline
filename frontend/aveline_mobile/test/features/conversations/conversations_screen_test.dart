import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/conversations/data/conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/data/demo_conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/presentation/screens/conversations_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The inbox the screen is driven against.
///
/// The demo repository rather than a stub: this test is about the screen's
/// wiring, and the seed is what gives it a pinned Salon, six client threads of
/// differing recency, read and unread rows, a thread waiting on a signature and
/// a notice.
///
/// Measured from the wall clock, unlike the repository's own test: the ages on
/// screen are printed against `DateTime.now()`, so pinning only the seed's clock
/// would make every row read "Just now".
DemoConversationRepository _inbox({Duration latency = Duration.zero}) =>
    DemoConversationRepository(clock: DateTime.now, latency: latency);

/// Fails the first read once, then serves the real inbox.
class _FlakyInbox implements ConversationRepository {
  _FlakyInbox(this._inner);

  final DemoConversationRepository _inner;
  int failures = 1;

  @override
  Future<List<Conversation>> fetchConversations() async {
    if (failures > 0) {
      failures--;
      throw Exception('The inbox is unavailable.');
    }
    return _inner.fetchConversations();
  }
}

class _EmptyInbox implements ConversationRepository {
  @override
  Future<List<Conversation>> fetchConversations() async => const [];
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
Finder _unread(String id) => find.byKey(Key('conversation_unread_$id'));

Future<void> _open(
  WidgetTester tester, {
  ConversationRepository? repository,
  VoidCallback? onOpenAveline,
  void Function(Conversation)? onOpenConversation,
}) async {
  await tester.pumpWidget(
    _wrap(
      repository ?? _inbox(),
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
      expect(
        find.textContaining('Aveline: The 12% goodwill discount'),
        findsOneWidget,
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

    testWidgets('counts the unread messages on the thread that has them', (tester) async {
      await _open(tester);

      expect(_unread('cnv_nadeesha'), findsOneWidget);
      expect(find.text('2'), findsOneWidget);
      expect(_unread('cnv_chathurika'), findsNothing);
    });

    testWidgets('flags the thread that is waiting on a signature', (tester) async {
      await _open(tester);

      expect(
        find.byKey(const Key('conversation_signoff_cnv_menaka')),
        findsOneWidget,
      );
      expect(find.text('Approval'), findsOneWidget);
    });

    testWidgets('states the unread count for the whole inbox', (tester) async {
      await _open(tester);

      final summary = tester.widget<Text>(
        find.byKey(const Key('conversations_unread_summary')),
      );
      expect(summary.data, '4 unread');
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
        _wrap(_inbox(latency: const Duration(milliseconds: 200))),
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
      expect(find.text('All caught up'), findsOneWidget);
      expect(_avelineRow(), findsNothing);
    });

    testWidgets('a failed read offers a retry that works', (tester) async {
      await tester.pumpWidget(_wrap(_FlakyInbox(_inbox())));
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
}
