import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/bubble_tone.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/mention_text.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/message_blocks.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/whatsapp_mark.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The updated Salon's rendering rules, mirroring the web's `blocks.dom.test.tsx`:
/// product tiles as a row, entity mentions as pills, and the WhatsApp channel card.
void main() {
  Widget wrap(Widget child) => MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(child: child),
        ),
      );

  ThreadBlock piece(String name, {String? imageUrl}) => ThreadBlock('piece', {
        'name': name,
        'price': 1000,
        'size': 'M',
        'stock': 2,
        'imageUrl': ?imageUrl,
      });

  ThreadBlock look({String? name, String? imageUrl, String? text}) =>
      ThreadBlock('look', {
        'name': ?name,
        'imageUrl': ?imageUrl,
        'text': ?text,
      });

  ThreadBlock text(String value) => ThreadBlock('text', {'text': value});

  Finder grids() => find.byKey(const Key('message_tile_grid'));

  Finder pill(String kind, int start) =>
      find.byKey(ValueKey('mention_${kind}_$start'));

  /// Every mention pill in the tree.
  Finder allPills() => find.byWidgetPredicate(
        (widget) =>
            widget.key is ValueKey<String> &&
            (widget.key! as ValueKey<String>).value.startsWith('mention_'),
      );

  /// The paragraph's literal runs, with each pill standing in as a marker.
  List<String?> runs(WidgetTester tester) {
    final rich = tester.widget<RichText>(
      find
          .descendant(
            of: find.byType(MentionText),
            matching: find.byType(RichText),
          )
          .first,
    );
    var span = rich.text as TextSpan;
    // `Text.rich` wraps the span it was handed in the paragraph's own style span, so
    // the runs sit one level below the RichText's root.
    final wrapped = span.children;
    if (wrapped != null && wrapped.length == 1 && wrapped.first is TextSpan) {
      final inner = wrapped.first as TextSpan;
      if (inner.children != null) {
        span = inner;
      }
    }
    return [
      for (final child in span.children ?? const <InlineSpan>[])
        child is TextSpan ? child.text : '<pill>',
    ];
  }

  ColorScheme schemeOf(WidgetTester tester, Finder of) =>
      Theme.of(tester.element(of)).colorScheme;

  // -------------------------------------------------------------------------
  // Product tiles
  // -------------------------------------------------------------------------
  group('product tiles', () {
    testWidgets('lays consecutive pieces out in one row instead of stacking them',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [piece('One'), piece('Two'), piece('Three')],
      )));

      expect(grids(), findsOneWidget);
      expect(find.text('One'), findsOneWidget);
      expect(find.text('Two'), findsOneWidget);
      expect(find.text('Three'), findsOneWidget);

      // A row: three cards share a baseline. Stacked, their centres would differ.
      final one = tester.getCenter(find.text('One')).dy;
      final two = tester.getCenter(find.text('Two')).dy;
      final three = tester.getCenter(find.text('Three')).dy;
      expect(two, one);
      expect(three, one);
    });

    testWidgets('keeps a run of many tiles in one grid, so a long set wraps',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [
          piece('One'),
          piece('Two'),
          piece('Three'),
          piece('Four'),
          piece('Five'),
        ],
      )));

      expect(grids(), findsOneWidget);
      expect(find.text('Five'), findsOneWidget);
      // Four tracks fit the 800px test surface, so the fifth wraps to a second row.
      expect(
        tester.getCenter(find.text('Five')).dy,
        greaterThan(tester.getCenter(find.text('One')).dy),
      );
    });

    testWidgets('puts a look with its own photograph in the same row as the pieces',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [
          piece('Emerald Green Georgette Saree', imageUrl: 'https://cdn/saree.jpg'),
          look(
            name: 'Galle Sunset Soiree',
            imageUrl: 'https://cdn/look.jpg',
            text: 'Balance the green with tonal gold.',
          ),
        ],
      )));

      expect(grids(), findsOneWidget);
      expect(find.text('Emerald Green Georgette Saree'), findsOneWidget);
      expect(find.text('Balance the green with tonal gold.'), findsOneWidget);
      expect(find.byKey(const Key('message_look_tile')), findsOneWidget);
      // One row: the look's card starts where the piece's does.
      expect(
        tester.getTopLeft(find.byKey(const Key('message_look_tile'))).dy,
        tester
            .getTopLeft(
              find.byKey(
                const ValueKey('message_piece_Emerald Green Georgette Saree'),
              ),
            )
            .dy,
      );
      // The look's name labels its plate rather than sitting in the card, which is
      // what the web's `alt={block.name}` does.
      expect(
        tester
            .widgetList<Image>(find.byType(Image))
            .map((image) => image.semanticLabel),
        contains('Galle Sunset Soiree'),
      );
    });

    testWidgets("drops a look's plate when the photograph is one of the pieces' own",
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [
          piece('Emerald Green Georgette Saree', imageUrl: 'https://cdn/saree.jpg'),
          look(
            name: 'Look: Boutique Collection',
            // Elle's composer borrowed the first matched piece's photo as the look's
            // own, so the Salon showed one saree twice.
            imageUrl: 'https://cdn/saree.jpg',
            text: 'Keep the silhouette clean and let the fabric do the talking.',
          ),
        ],
      )));

      // One photograph, on the piece: the look keeps its words and loses the copy.
      expect(find.byType(Image), findsOneWidget);
      expect(
        find.text('Keep the silhouette clean and let the fabric do the talking.'),
        findsOneWidget,
      );
      expect(find.text('Look: Boutique Collection'), findsOneWidget);
    });

    testWidgets('reads a look without a photograph as its styling note, never a blank plate',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [
          look(
            name: 'Look: Boutique Collection',
            text: 'Anchor it with a muted gold blouse in a matte finish.',
          ),
        ],
      )));

      expect(grids(), findsNothing);
      expect(find.byType(Image), findsNothing);
      expect(find.byKey(const Key('message_look_note')), findsOneWidget);
      expect(find.text('Look: Boutique Collection'), findsOneWidget);
      expect(
        find.text('Anchor it with a muted gold blouse in a matte finish.'),
        findsOneWidget,
      );
    });

    testWidgets('starts a new row when prose separates two pieces, so a caption stays with its tile',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [piece('One'), text('It comes in three sizes.'), piece('Two')],
      )));

      expect(grids(), findsNWidgets(2));
      expect(
        find.descendant(of: grids().first, matching: find.text('One')),
        findsOneWidget,
      );
      expect(
        find.descendant(of: grids().first, matching: find.text('Two')),
        findsNothing,
      );
      expect(
        find.descendant(of: grids().last, matching: find.text('Two')),
        findsOneWidget,
      );
    });

    testWidgets('fits two tiles across a phone-width bubble, which 11rem tracks could not',
        (tester) async {
      await tester.pumpWidget(wrap(SizedBox(
        width: 256,
        child: MessageBlockList(
          messageId: 'm-tiles',
          blocks: [piece('One'), piece('Two'), piece('Three')],
        ),
      )));

      // Two `11rem` tracks need 360px, which a phone's bubble does not have; without the
      // shrinking track a curated set collapsed to one column — the stacking the row
      // exists to prevent.
      expect(grids(), findsOneWidget);
      expect(
        tester.getCenter(find.text('One')).dy,
        tester.getCenter(find.text('Two')).dy,
      );
      expect(
        tester.getCenter(find.text('Three')).dy,
        greaterThan(tester.getCenter(find.text('One')).dy),
      );
    });

    testWidgets('renders a piece without a photograph as a stated absence, not a broken image',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [piece('Ivory Organza')],
      )));

      expect(find.text('No photograph'), findsOneWidget);
      expect(find.byType(Image), findsNothing);
      expect(find.byKey(const Key('message_tile_no_photograph')), findsOneWidget);
    });

    testWidgets('caps a lone tile at 16rem and lets a shared row fill the width',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [piece('Only')],
      )));
      expect(tester.getSize(grids()).width, 256);

      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-tiles',
        blocks: [piece('One'), piece('Two')],
      )));
      // Not capped: two cards fill the width between them.
      expect(tester.getSize(grids()).width, greaterThan(256));
      expect(tester.getSize(find.byKey(const ValueKey('message_piece_One'))).width,
          greaterThan(256));
    });
  });

  // -------------------------------------------------------------------------
  // The customer's channel message
  // -------------------------------------------------------------------------
  group('client message block', () {
    ThreadBlock client({String? from, String? message}) => ThreadBlock(
          'client_message',
          {
            'from': ?from,
            'text': message ?? 'Do you still have the emerald green saree?',
          },
        );

    testWidgets('names the customer and the channel the message arrived on',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        blocks: [client(from: '94763475058')],
      )));

      expect(find.text('CUSTOMER'), findsOneWidget);
      expect(
        find.descendant(
          of: find.byKey(const Key('client_channel_label')),
          matching: find.text('WhatsApp'),
        ),
        findsOneWidget,
      );
      // The mark is the official glyph, drawn inside the brand-green badge.
      expect(find.byType(WhatsAppMark), findsOneWidget);
      expect(find.byKey(const Key('client_channel_mark')), findsOneWidget);
      expect(
        find.text('Do you still have the emerald green saree?'),
        findsOneWidget,
      );
    });

    testWidgets('spaces the raw WhatsApp handle the way the rest of the app spaces numbers',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        blocks: [client(from: '94763475058')],
      )));

      // The channel hands over `94763475058`; a wall of eleven digits is unreadable
      // in a thread.
      expect(find.text('+94 76 34 75 058'), findsOneWidget);
    });

    testWidgets('leaves a handle that is not Sri Lankan exactly as the channel gave it',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        blocks: [client(from: '+15551234567')],
      )));

      expect(find.text('+15551234567'), findsOneWidget);
      expect(find.textContaining('+94'), findsNothing);
    });

    testWidgets('renders without a handle rather than naming the customer "WhatsApp"',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        blocks: [client()],
      )));

      expect(find.text('CUSTOMER'), findsOneWidget);
      expect(find.textContaining('+94'), findsNothing);
      expect(
        find.text('Do you still have the emerald green saree?'),
        findsOneWidget,
      );
    });

    testWidgets("keeps the customer's line breaks, which are part of what they said",
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        blocks: [
          client(message: 'Hello,\nDo you have it in green?'),
        ],
      )));

      expect(find.text('Hello,\nDo you have it in green?'), findsOneWidget);
    });

    testWidgets("keeps the customer's words readable on the staff bubble",
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        tone: BubbleTone.own,
        blocks: [client(from: '94763475058')],
      )));

      // The staff bubble fills with `primary` and sets white ink. A card that
      // inherited it would paint white on its own white surface and say nothing.
      final words = tester.widget<Text>(
        find.text('Do you still have the emerald green saree?'),
      );
      final scheme = schemeOf(tester, find.byType(MessageBlockList));
      expect(words.style?.color, scheme.onSurface);
    });

    testWidgets("leaves the customer's own words alone, because a mention is the staff's affordance",
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-client',
        blocks: [
          client(from: '94763475058', message: 'Is @silksbyamelia yours?'),
        ],
      )));

      // A customer's at-sign is a handle they typed, not an entity any lookup read.
      expect(allPills(), findsNothing);
      expect(find.text('Is @silksbyamelia yours?'), findsOneWidget);
    });
  });

  // -------------------------------------------------------------------------
  // Entity mentions
  // -------------------------------------------------------------------------
  group('entity mentions', () {
    testWidgets('lifts a customer mention out of the prose', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        blocks: [text('@Samantha Arias — any events?')],
      )));

      expect(pill('customer', 0), findsOneWidget);
      expect(
        find.descendant(
          of: pill('customer', 0),
          matching: find.text('@Samantha Arias'),
        ),
        findsOneWidget,
      );
      // The prose around the pill is untouched.
      expect(runs(tester), ['<pill>', ' — any events?']);
    });

    testWidgets('stops the pill where the resolver stopped, leaving the prose behind it alone',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        blocks: [text('@jason smith, dropped by to browse')],
      )));

      expect(
        find.descendant(
          of: pill('customer', 0),
          matching: find.text('@jason smith'),
        ),
        findsOneWidget,
      );
      // The comma ends the name; everything after it stays the sentence it was.
      expect(runs(tester), ['<pill>', ', dropped by to browse']);
    });

    testWidgets('trims the prose a greedy capture swallowed, exactly as the resolver trims it',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        blocks: [text('@jason smith dropped by')],
      )));

      // No delimiter, so the tail is trimmed only because "by" and "dropped" are
      // stop words.
      expect(
        find.descendant(
          of: pill('customer', 0),
          matching: find.text('@jason smith'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('lifts a phone mention and sets it in figures that line up',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        blocks: [text('reach her on #0771234567 please')],
      )));

      final phone = pill('phone', 'reach her on '.length);
      expect(phone, findsOneWidget);
      expect(
        find.descendant(of: phone, matching: find.text('#0771234567')),
        findsOneWidget,
      );
      final pillText = tester.widget<Text>(
        find.descendant(of: phone, matching: find.byType(Text)),
      );
      expect(
        pillText.style?.fontFeatures,
        contains(const FontFeature.tabularFigures()),
      );
    });

    testWidgets('tints the pill for the bubble it sits in, rather than vanishing into it',
        (tester) async {
      final blocks = [text('@Samantha Arias')];

      await tester.pumpWidget(wrap(
        MessageBlockList(messageId: 'm-text', blocks: blocks),
      ));
      var scheme = schemeOf(tester, find.byType(MessageBlockList));
      var pillText = tester.widget<Text>(
        find.descendant(of: pill('customer', 0), matching: find.byType(Text)),
      );
      expect(pillText.style?.color, scheme.primary);

      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        tone: BubbleTone.own,
        blocks: blocks,
      )));
      scheme = schemeOf(tester, find.byType(MessageBlockList));
      pillText = tester.widget<Text>(
        find.descendant(of: pill('customer', 0), matching: find.byType(Text)),
      );
      expect(pillText.style?.color, scheme.onPrimary);
    });

    testWidgets('lifts mentions inside a suggestion, which is drafted text too',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        blocks: [
          ThreadBlock('suggestion', {
            'text': 'Tell @Samantha Arias the saree is back.',
          }),
        ],
      )));

      expect(allPills(), findsOneWidget);
      // `toHaveTextContent` in the web's own case: the greedy capture runs to the end
      // of the region and only trims trailing stop words, so the pill's exact text
      // here is wider than the name it starts with.
      expect(
        find.descendant(
          of: pill('customer', 'Tell '.length),
          matching: find.textContaining('@Samantha Arias'),
        ),
        findsOneWidget,
      );
    });

    testWidgets('leaves an escaped token as the text it was typed as',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-text',
        blocks: [text(r'type \@ to mention someone')],
      )));

      expect(allPills(), findsNothing);
      expect(
        tester.widget<MentionText>(find.byType(MentionText)).text,
        r'type \@ to mention someone',
      );
    });
  });

  // -------------------------------------------------------------------------
  // The rest of the block vocabulary
  // -------------------------------------------------------------------------
  group('briefing blocks', () {
    testWidgets('draws an at_a_glance table with its header and rows',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-brief',
        blocks: [
          ThreadBlock('at_a_glance', {
            'columns': ['Client', 'Last visit'],
            'rows': [
              ['Samantha Arias', '4 Aug'],
            ],
          }),
        ],
      )));

      expect(find.byKey(const Key('message_at_a_glance')), findsOneWidget);
      expect(find.text('Client'), findsOneWidget);
      expect(find.text('Last visit'), findsOneWidget);
      expect(find.text('Samantha Arias'), findsOneWidget);
      expect(find.text('4 Aug'), findsOneWidget);
    });

    testWidgets('offers a sign-off decision only when the Salon can take one',
        (tester) async {
      final block = ThreadBlock('sign_off', {
        'reason': 'A 12% discount on a repeat client.',
        'amount': 24500,
      });

      await tester.pumpWidget(wrap(
        MessageBlockList(messageId: 'm-brief', blocks: [block]),
      ));
      expect(find.text('Approval needed'), findsOneWidget);
      expect(find.text('A 12% discount on a repeat client.'), findsOneWidget);
      expect(find.text('Rs 24,500'), findsOneWidget);
      // No decision to hand anywhere, so no buttons rather than inert ones.
      expect(find.text('Approve'), findsNothing);

      bool? decision;
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-brief',
        blocks: [block],
        onSignOff: (approved) => decision = approved,
      )));
      await tester.tap(find.text('Approve'));
      expect(decision, isTrue);
      await tester.tap(find.text('Reject'));
      expect(decision, isFalse);
    });

    testWidgets('names a payment and a delivery in their own words',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-brief',
        blocks: [
          ThreadBlock('payment', {'amount': 24500, 'status': 'Pending'}),
          ThreadBlock('courier', {'status': 'Dispatched'}),
        ],
      )));

      expect(find.text('Payment'), findsOneWidget);
      expect(find.text('Rs 24,500'), findsOneWidget);
      expect(find.text('Pending'), findsOneWidget);
      expect(find.text('Delivery'), findsOneWidget);
      expect(find.text('Dispatched'), findsOneWidget);
    });

    testWidgets('reports a resolution choice and the customer it picked',
        (tester) async {
      String? selected;
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-brief',
        blocks: [
          ThreadBlock('choice', {
            'prompt': 'Which one did you mean?',
            'options': [
              {'customerId': 'c1', 'fullName': 'Samantha Arias', 'status': 'vip'},
              {'customerId': 'c2', 'fullName': 'Samantha Ranaweera'},
            ],
          }),
        ],
        onSelectCustomer: (customerId) => selected = customerId,
      )));

      expect(find.text('Which one did you mean?'), findsOneWidget);
      await tester.tap(find.text('Samantha Arias'));
      expect(selected, 'c1');
    });

    testWidgets('draws an attachment block rather than dropping it',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-brief',
        blocks: [
          ThreadBlock('attachment', {
            'attachmentId': 'a1',
            'url': '/api/v1/orgs/o1/conversations/c1/attachments/a1',
            'contentType': 'application/pdf',
            'fileName': 'price-list.pdf',
            'sizeBytes': 2048,
          }),
        ],
      )));

      expect(find.text('price-list.pdf'), findsOneWidget);
      expect(find.text('2 KB'), findsOneWidget);
    });

    testWidgets('says what an unknown block is instead of drawing an empty bubble',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm-brief',
        blocks: [
          ThreadBlock('telepathy', {'text': 'Something this build has not learned.'}),
        ],
      )));

      expect(
        find.text('Something this build has not learned.'),
        findsOneWidget,
      );
    });
  });
}
