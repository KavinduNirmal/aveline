import 'package:aveline_mobile/features/conversations/domain/thread_message.dart' as wire;
import 'package:aveline_mobile/features/salon/domain/salon_message.dart';
import 'package:aveline_mobile/features/salon/presentation/widgets/message_bubble.dart';
import 'package:aveline_mobile/features/salon/presentation/widgets/typewriter_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  SalonMessage agentMessage({double? thoughtSeconds}) => SalonMessage(
        id: 'm1',
        authorKind: 'Agent',
        agentKey: 'aveline',
        text: 'I have looked into this.',
        createdAt: DateTime(2026, 9, 9, 10, 0),
        thoughtSeconds: thoughtSeconds,
      );

  SalonMessage userMessage({MessageDeliveryStatus? status}) => SalonMessage(
        id: 'm2',
        authorKind: 'User',
        text: 'Hello',
        createdAt: DateTime(2026, 9, 9, 10, 0),
        deliveryStatus: status,
      );

  Widget wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

  testWidgets('shows a Sending… status for an optimistic staff message',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: userMessage(status: MessageDeliveryStatus.sending))));
    expect(find.text('Sending…'), findsOneWidget);
  });

  testWidgets('shows a failed status for a failed message', (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: userMessage(status: MessageDeliveryStatus.failed))));
    expect(find.text('Failed to send'), findsOneWidget);
  });

  testWidgets('shows the timestamp once confirmed', (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: userMessage())));
    expect(find.text('10:00'), findsOneWidget);
    expect(find.text('Sending…'), findsNothing);
  });

  testWidgets('shows a Thought for Xs caption on an agent message',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: agentMessage(thoughtSeconds: 0.84))));
    expect(find.text('Thought for 0.84s'), findsOneWidget);
  });

  testWidgets('agent message without thoughtSeconds shows no caption',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: agentMessage())));
    expect(find.textContaining('Thought for'), findsNothing);
  });

  SalonMessage choiceMessage() => SalonMessage(
        id: 'm4',
        authorKind: 'Agent',
        agentKey: 'aveline',
        text: '',
        choicePrompt: 'Which one did you mean?',
        choiceOptions: const [
          SalonChoiceOption(
            customerId: 'c1',
            fullName: 'Samantha Arias',
            status: 'vip',
          ),
          SalonChoiceOption(customerId: 'c2', fullName: 'Samantha Ranaweera'),
        ],
        createdAt: DateTime(2026, 9, 9, 10, 0),
      );

  testWidgets('renders choice options for an ambiguous resolution',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: choiceMessage())));
    expect(find.text('Which one did you mean?'), findsOneWidget);
    expect(find.text('Samantha Arias'), findsOneWidget);
    expect(find.text('Samantha Ranaweera'), findsOneWidget);
  });

  testWidgets('tapping a choice option reports the selected customer',
      (tester) async {
    String? selected;
    await tester.pumpWidget(wrap(
      MessageBubble(
        message: choiceMessage(),
        onSelectCustomer: (customerId) => selected = customerId,
      ),
    ));
    await tester.tap(find.text('Samantha Arias'));
    expect(selected, 'c1');
  });

  // ---------------------------------------------------------------------------
  // The updated Salon: tiles, the channel card, and what streams
  // ---------------------------------------------------------------------------
  group('content blocks', () {
    wire.ThreadBlock piece(String name, {String? imageUrl}) => wire.ThreadBlock('piece', {
          'name': name,
          'price': 1000,
          'imageUrl': ?imageUrl,
        });

    wire.ThreadBlock look({String? name, String? imageUrl, String? text}) =>
        wire.ThreadBlock('look', {
          'name': ?name,
          'imageUrl': ?imageUrl,
          'text': ?text,
        });

    wire.ThreadBlock textBlock(String value) => wire.ThreadBlock('text', {'text': value});

    SalonMessage agentWith(List<wire.ThreadBlock> blocks, {bool streamIn = false}) =>
        SalonMessage(
          id: 'm5',
          authorKind: 'Agent',
          agentKey: 'elle',
          text: '',
          createdAt: DateTime(2026, 9, 9, 10, 0),
          streamIn: streamIn,
          blocks: blocks,
        );

    /// The width the bubble settled on. The test surface is 800 wide, so the web's
    /// 78% cap resolves to 624.
    double bubbleWidth(WidgetTester tester) =>
        tester.getSize(find.byKey(const ValueKey('salon_bubble_m5'))).width;

    testWidgets('gives a message that carries a tile row the definite bubble width',
        (tester) async {
      await tester.pumpWidget(wrap(
        MessageBubble(message: agentWith([piece('One'), piece('Two')])),
      ));

      // The row counts its columns against this width; a shrink-to-fit bubble would
      // resolve to a single column and stack the pieces again.
      expect(bubbleWidth(tester), moreOrLessEquals(624, epsilon: 0.5));
      expect(find.byKey(const Key('message_tile_grid')), findsOneWidget);
    });

    testWidgets('leaves a lone tile shrink-to-fit, so the card is capped rather than the bubble',
        (tester) async {
      await tester.pumpWidget(wrap(
        MessageBubble(message: agentWith([piece('Only')])),
      ));

      expect(bubbleWidth(tester), lessThan(624));
      expect(
        tester.getSize(find.byKey(const Key('message_tile_grid'))).width,
        256,
      );
    });

    testWidgets('leaves a text-only bubble shrink-to-fit', (tester) async {
      await tester.pumpWidget(wrap(
        MessageBubble(message: agentWith([textBlock('Three pieces match.')])),
      ));

      expect(bubbleWidth(tester), lessThan(624));
    });

    testWidgets('does not widen the bubble for a look whose photograph was borrowed',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBubble(
        message: agentWith([
          piece('One', imageUrl: 'https://cdn/one.jpg'),
          look(
            name: 'Look',
            imageUrl: 'https://cdn/one.jpg',
            text: 'Wear it with gold.',
          ),
        ]),
      )));

      // The look is a note after normalisation, so there is no row to size against.
      expect(bubbleWidth(tester), lessThan(624));
      expect(find.byKey(const Key('message_look_note')), findsOneWidget);
    });

    testWidgets('renders a customer message as the channel card', (tester) async {
      await tester.pumpWidget(wrap(MessageBubble(
        message: agentWith([
          wire.ThreadBlock('client_message', {
            'from': '94763475058',
            'text': 'Do you still have the emerald green saree?',
          }),
        ]),
      )));

      expect(find.byKey(const Key('client_channel_surface')), findsOneWidget);
      expect(find.text('+94 76 34 75 058'), findsOneWidget);
      expect(
        find.text('Do you still have the emerald green saree?'),
        findsOneWidget,
      );
    });

    SalonMessage staged({String status = 'AwaitingSignOff'}) => SalonMessage(
          id: 'm6',
          authorKind: 'Agent',
          agentKey: 'lina',
          text: '',
          createdAt: DateTime(2026, 9, 9, 10, 0),
          contentHash: 'hash-1',
          status: status,
          blocks: [
            wire.ThreadBlock('sign_off', {
              'reason': 'A 12% loyalty discount on a repeat client.',
              'amount': 24500,
            }),
          ],
        );

    testWidgets('offers the decision on a staged reply, with the message it decides',
        (tester) async {
      SalonMessage? decided;
      bool? approval;

      await tester.pumpWidget(wrap(MessageBubble(
        message: staged(),
        onSignOff: (message, approved) {
          decided = message;
          approval = approved;
        },
      )));

      expect(find.text('Approval needed'), findsOneWidget);
      await tester.tap(find.text('Approve'));

      // The whole message travels, not just the boolean: the API binds the decision to
      // the content hash the associate was shown, and only the message carries it.
      expect(decided?.id, 'm6');
      expect(decided?.contentHash, 'hash-1');
      expect(approval, isTrue);
    });

    testWidgets('offers no decision on a reply that has already been answered',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBubble(
        message: staged(status: 'Published'),
        onSignOff: (_, _) {},
      )));

      // The block still reads as the approval it was, but a second decision is not on
      // offer — the API would refuse it.
      expect(find.text('Approval needed'), findsOneWidget);
      expect(find.text('Approve'), findsNothing);
      expect(find.text('Reject'), findsNothing);
    });

    testWidgets('types out a purely textual reply', (tester) async {
      await tester.pumpWidget(wrap(MessageBubble(
        message: SalonMessage(
          id: 'm5',
          authorKind: 'Agent',
          agentKey: 'aveline',
          text: 'I have looked into this.',
          createdAt: DateTime(2026, 9, 9, 10, 0),
          streamIn: true,
        ),
      )));

      expect(find.byType(TypewriterText), findsOneWidget);
    });

    testWidgets('draws a rich reply at once, because streaming would hide its cards',
        (tester) async {
      await tester.pumpWidget(wrap(
        MessageBubble(
          message: agentWith([piece('One'), piece('Two')], streamIn: true),
        ),
      ));

      // Streaming collapses content to the first `text` block, so a rich answer has
      // to render its blocks or its tiles would not appear until a reload.
      expect(find.byType(TypewriterText), findsNothing);
      expect(find.byKey(const Key('message_tile_grid')), findsOneWidget);
    });
  });
}
