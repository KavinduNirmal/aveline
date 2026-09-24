import 'dart:ui' show Tristate;

import 'package:aveline_mobile/features/conversations/domain/block_actions.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/block_action_rail.dart';
import 'package:aveline_mobile/features/conversations/presentation/widgets/message_blocks.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The rail's drawing rules: it is the card's footer, not a row of chips.
void main() {
  BlockActionBridge bridge({
    bool hasCustomerDestination = true,
    bool hasForwardDestination = true,
    bool agentBusy = false,
    BlockActionId? Function(String messageId, int blockIndex)? pending,
    void Function(ThreadBlock, String, int, BlockActionId)? onAction,
  }) => BlockActionBridge(
    hasCustomerDestination: hasCustomerDestination,
    hasForwardDestination: hasForwardDestination,
    agentBusy: agentBusy,
    pendingAction: pending ?? (_, _) => null,
    onAction: onAction ?? (_, _, _, _) {},
  );

  Widget wrap(Widget child) => MaterialApp(
    home: Scaffold(body: SingleChildScrollView(child: child)),
  );

  ThreadBlock suggestion(String text) =>
      ThreadBlock('suggestion', {'type': 'suggestion', 'text': text});

  ThreadBlock piece() =>
      ThreadBlock('piece', {'type': 'piece', 'name': 'Silk Slip', 'price': 24000});

  // ---------------------------------------------------------------------------
  // Fused to the card
  // ---------------------------------------------------------------------------
  group('fused to the card', () {
    testWidgets('the rail is drawn inside the card it closes', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(),
      )));

      // The card clips both its body and its rail, so the card's radius shapes the
      // rail's bottom corners rather than the segments rounding themselves.
      final card = tester.widget<Container>(
        find.byKey(const Key('message_suggestion')),
      );
      expect(card.clipBehavior, Clip.antiAlias);
      expect(
        find.descendant(
          of: find.byKey(const Key('message_suggestion')),
          matching: find.byKey(const ValueKey('block_action_rail_m1_0')),
        ),
        findsOneWidget,
      );
    });

    testWidgets('a piece tile carries its rail inside its own clipped box', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [piece()],
        bridge: bridge(),
      )));

      final card = tester.widget<Container>(
        find.byKey(const ValueKey('message_piece_Silk Slip')),
      );
      expect(card.clipBehavior, Clip.antiAlias);
      expect(
        find.descendant(
          of: find.byKey(const ValueKey('message_piece_Silk Slip')),
          matching: find.byKey(const ValueKey('block_action_rail_m1_0')),
        ),
        findsOneWidget,
      );
    });

    testWidgets('draws no rail at all when the renderer has no thread behind it',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
      )));

      expect(find.byKey(const ValueKey('block_action_rail_m1_0')), findsNothing);
    });
  });

  // ---------------------------------------------------------------------------
  // Which segments exist
  // ---------------------------------------------------------------------------
  group('segments', () {
    testWidgets('a suggestion draws Copy, Send to customer and Regenerate, in order',
        (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(),
      )));

      expect(find.byKey(const ValueKey('block_action_copy_m1_0')), findsOneWidget);
      expect(
        find.byKey(const ValueKey('block_action_sendToCustomer_m1_0')),
        findsOneWidget,
      );
      expect(
        find.byKey(const ValueKey('block_action_regenerate_m1_0')),
        findsOneWidget,
      );
      expect(find.byKey(const ValueKey('block_action_forward_m1_0')), findsNothing);

      final copy = tester.getTopLeft(
        find.byKey(const ValueKey('block_action_copy_m1_0')),
      );
      final send = tester.getTopLeft(
        find.byKey(const ValueKey('block_action_sendToCustomer_m1_0')),
      );
      expect(send.dx, greaterThan(copy.dx));
    });

    testWidgets('a piece never draws Regenerate or Copy', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [piece()],
        bridge: bridge(),
      )));

      expect(find.byKey(const ValueKey('block_action_forward_m1_0')), findsOneWidget);
      expect(find.byKey(const ValueKey('block_action_regenerate_m1_0')), findsNothing);
      expect(find.byKey(const ValueKey('block_action_copy_m1_0')), findsNothing);
    });
  });

  // ---------------------------------------------------------------------------
  // Tooltips, compact mode and disabled reasons
  // ---------------------------------------------------------------------------
  group('accessibility', () {
    testWidgets('every segment wears a tooltip', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(),
      )));

      expect(find.byTooltip('Copy'), findsOneWidget);
      expect(find.byTooltip('Send to customer'), findsOneWidget);
      expect(find.byTooltip('Regenerate'), findsOneWidget);
    });

    testWidgets('a narrow tile drops the printed word but keeps the name and tooltip',
        (tester) async {
      final handle = tester.ensureSemantics();
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [piece()],
        bridge: bridge(),
      )));

      // The glyph carries the segment; the word is not printed.
      expect(find.text('Forward'), findsNothing);
      expect(find.byTooltip('Forward'), findsOneWidget);
      // The accessible name stays whole, so a screen reader hears the action.
      expect(find.bySemanticsLabel('Forward'), findsOneWidget);
      handle.dispose();
    });

    testWidgets('a disabled segment explains why, in its tooltip', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(hasCustomerDestination: false),
      )));

      expect(find.byTooltip(BlockActionReasons.noCustomer), findsOneWidget);
    });

    testWidgets('a disabled segment stays focusable with an unavailable state',
        (tester) async {
      final handle = tester.ensureSemantics();
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(hasCustomerDestination: false),
      )));

      final node = tester.getSemantics(
        find.bySemanticsLabel('Send to customer'),
      );
      // Reachable rather than removed from the tab order: the reason is the point.
      expect(node.label, 'Send to customer');
      expect(node.flagsCollection.isEnabled, Tristate.isFalse);
      handle.dispose();
    });

    testWidgets('pressing a disabled segment says the reason out loud', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(hasCustomerDestination: false),
      )));

      await tester.tap(find.byKey(const ValueKey('block_action_sendToCustomer_m1_0')));
      await tester.pumpAndSettle();

      expect(find.text(BlockActionReasons.noCustomer), findsOneWidget);
    });

    testWidgets('a busy segment states itself and blocks its neighbours', (tester) async {
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(
          pending: (messageId, blockIndex) => BlockActionId.regenerate,
        ),
      )));

      expect(find.byTooltip('Redoing…'), findsOneWidget);
      // The rest of the block says why it cannot run.
      expect(find.byTooltip(BlockActionReasons.pending), findsNWidgets(2));
    });
  });

  // ---------------------------------------------------------------------------
  // Handing the action over
  // ---------------------------------------------------------------------------
  group('actions', () {
    testWidgets('a live segment hands its block, message and position over',
        (tester) async {
      ThreadBlock? acted;
      String? actedMessage;
      int? actedIndex;
      BlockActionId? actedAction;

      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [suggestion('Tell her it is back.')],
        bridge: bridge(
          onAction: (block, messageId, blockIndex, action) {
            acted = block;
            actedMessage = messageId;
            actedIndex = blockIndex;
            actedAction = action;
          },
        ),
      )));

      await tester.tap(find.byKey(const ValueKey('block_action_copy_m1_0')));
      await tester.pump();

      expect(acted?.type, 'suggestion');
      expect(actedMessage, 'm1');
      expect(actedIndex, 0);
      expect(actedAction, BlockActionId.copy);
    });

    testWidgets('two blocks of one message are addressed apart', (tester) async {
      // The indices are what keep one piece's pending state off another's rail.
      final seen = <int>[];
      await tester.pumpWidget(wrap(MessageBlockList(
        messageId: 'm1',
        blocks: [
          ThreadBlock('piece', {'type': 'piece', 'name': 'One'}),
          ThreadBlock('piece', {'type': 'piece', 'name': 'Two'}),
        ],
        blockIndices: const [3, 4],
        bridge: bridge(
          onAction: (_, _, blockIndex, _) => seen.add(blockIndex),
        ),
      )));

      await tester.tap(find.byKey(const ValueKey('block_action_forward_m1_3')));
      await tester.tap(find.byKey(const ValueKey('block_action_forward_m1_4')));
      await tester.pump();

      expect(seen, [3, 4]);
    });
  });
}
