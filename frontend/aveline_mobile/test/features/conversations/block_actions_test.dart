import 'package:aveline_mobile/features/conversations/domain/block_actions.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:flutter_test/flutter_test.dart';

/// The action mapping, the clipboard payload, and the availability rules.
///
/// Every rule here is plain data, so it is pinned without pumping a widget: which
/// actions a block type offers, what each hands over, and why a segment is unavailable.
void main() {
  ThreadBlock suggestion(String text) =>
      ThreadBlock('suggestion', {'type': 'suggestion', 'text': text});

  ThreadBlock piece({String? name, String? size, num? price}) => ThreadBlock(
    'piece',
    {'type': 'piece', 'name': ?name, 'size': ?size, 'price': ?price},
  );

  ThreadBlock look({String? name, String? text}) =>
      ThreadBlock('look', {'type': 'look', 'name': ?name, 'text': ?text});

  BlockActionEnvironment environment({
    bool hasCustomerDestination = true,
    bool hasForwardDestination = true,
    bool agentBusy = false,
    BlockActionId? pending,
  }) => BlockActionEnvironment(
    hasCustomerDestination: hasCustomerDestination,
    hasForwardDestination: hasForwardDestination,
    agentBusy: agentBusy,
    pending: pending,
  );

  List<BlockActionId> offered(ThreadBlock block) =>
      resolveBlockActions(block, environment()).map((action) => action.id).toList();

  // ---------------------------------------------------------------------------
  // Which actions a block type offers
  // ---------------------------------------------------------------------------
  group('actionsForBlockType', () {
    test('a suggestion is copied, sent and regenerated', () {
      expect(offered(suggestion('')), [
        BlockActionId.copy,
        BlockActionId.sendToCustomer,
        BlockActionId.regenerate,
      ]);
    });

    test('a piece carries Forward alone', () {
      // The brief names one action for an item block: a tile grid must not grow a
      // Copy on every card, and a piece is never regenerated.
      expect(actionsForBlockType('piece'), [BlockActionId.forward]);
      expect(offered(piece(name: 'A piece')), [BlockActionId.forward]);
    });

    test('a look is copied, forwarded and regenerated', () {
      expect(offered(look(name: 'A look')), [
        BlockActionId.copy,
        BlockActionId.forward,
        BlockActionId.regenerate,
      ]);
    });

    test('the vocabulary has no Share', () {
      // The brief forbids it, and a segment with nothing behind it is worse than none.
      for (final id in BlockActionId.values) {
        expect(id.name, isNot('share'));
      }
    });

    test('a type this build has not been taught offers nothing', () {
      expect(actionsForBlockType('telepathy'), isEmpty);
      expect(actionsForBlockType('text'), isEmpty);
      expect(actionsForBlockType('sign_off'), isEmpty);
      expect(offered(ThreadBlock('telepathy', const {})), isEmpty);
    });

    test('never offers an action the type does not have', () {
      // Regenerate on a piece is the case the brief calls out by name.
      expect(offered(piece(name: 'A piece')), isNot(contains(BlockActionId.regenerate)));
      expect(offered(piece(name: 'A piece')), isNot(contains(BlockActionId.copy)));
      expect(offered(suggestion('')), isNot(contains(BlockActionId.forward)));
    });
  });

  // ---------------------------------------------------------------------------
  // The clipboard payload
  // ---------------------------------------------------------------------------
  group('blockToText', () {
    test('a suggestion hands over its own sentence', () {
      expect(blockToText(suggestion('  Tell her it is back.  ')), 'Tell her it is back.');
    });

    test('a piece is one line with the parts the server sent', () {
      expect(
        blockToText(piece(name: 'Silk Wrap Blouse', size: 'M', price: 18500)),
        'Silk Wrap Blouse · Size M · LKR 18,500',
      );
    });

    test('a piece invents no half the server did not send', () {
      expect(blockToText(piece(name: 'Silk Wrap Blouse', price: 18500)),
          'Silk Wrap Blouse · LKR 18,500');
      expect(blockToText(piece(name: 'Silk Wrap Blouse', size: 'M')),
          'Silk Wrap Blouse · Size M');
      expect(blockToText(piece(name: 'Silk Wrap Blouse')), 'Silk Wrap Blouse');
    });

    test('a look is its name and its note when it has both', () {
      expect(
        blockToText(look(name: 'Galle Sunset', text: 'Balance the green with gold.')),
        'Galle Sunset — Balance the green with gold.',
      );
    });

    test('a look with only one half prints only that half', () {
      expect(blockToText(look(name: 'Galle Sunset')), 'Galle Sunset');
      expect(
        blockToText(look(text: 'Balance the green with gold.')),
        'Balance the green with gold.',
      );
    });
  });

  group('blockTitle', () {
    test('prefers the block name, then a noun for its type', () {
      expect(blockTitle(piece(name: 'Silk Wrap Blouse')), 'Silk Wrap Blouse');
      expect(blockTitle(suggestion('')), 'Draft reply');
      expect(blockTitle(piece()), 'Piece');
      expect(blockTitle(look()), 'Look');
    });
  });

  // ---------------------------------------------------------------------------
  // Availability
  // ---------------------------------------------------------------------------
  group('resolveBlockActions', () {
    test('every action is live when the thread can do all of them', () {
      final actions = resolveBlockActions(look(name: 'A look'), environment());
      expect(actions.every((action) => action.enabled), isTrue);
      expect(actions.every((action) => action.reason == null), isTrue);
      expect(actions.every((action) => !action.busy), isTrue);
    });

    test('send to customer says why when the thread has no client', () {
      final actions = resolveBlockActions(
        suggestion(''),
        environment(hasCustomerDestination: false),
      );
      final send =
          actions.firstWhere((action) => action.id == BlockActionId.sendToCustomer);
      expect(send.enabled, isFalse);
      expect(send.reason, BlockActionReasons.noCustomer);
    });

    test('forward says why when there is nowhere to forward to', () {
      final actions = resolveBlockActions(
        piece(name: 'A piece'),
        environment(hasForwardDestination: false),
      );
      expect(actions.single.enabled, isFalse);
      expect(actions.single.reason, BlockActionReasons.noForwardTarget);
    });

    test('regenerate says why while the agent is still working', () {
      final actions = resolveBlockActions(
        suggestion(''),
        environment(agentBusy: true),
      );
      final regenerate =
          actions.firstWhere((action) => action.id == BlockActionId.regenerate);
      expect(regenerate.enabled, isFalse);
      expect(regenerate.reason, BlockActionReasons.agentBusy);
    });

    test('one action in flight disables the others on the same block', () {
      final actions = resolveBlockActions(
        look(name: 'A look'),
        environment(pending: BlockActionId.copy),
      );
      final copy = actions.firstWhere((action) => action.id == BlockActionId.copy);
      final forward = actions.firstWhere((action) => action.id == BlockActionId.forward);
      // The running one states itself as busy; the rest say why they are blocked.
      expect(copy.busy, isTrue);
      expect(copy.enabled, isFalse);
      expect(forward.enabled, isFalse);
      expect(forward.reason, BlockActionReasons.pending);
    });
  });

  // ---------------------------------------------------------------------------
  // Forward destinations
  // ---------------------------------------------------------------------------
  group('forwardTargetsFrom', () {
    const nadeesha = Conversation(
      id: 'cnv_nadeesha',
      kind: ConversationKind.customer,
      customerId: 'cus_204',
      customerName: 'Nadeesha Perera',
    );
    const menaka = Conversation(
      id: 'cnv_menaka',
      kind: ConversationKind.customer,
      customerId: 'cus_311',
      customerName: 'Menaka Rathnayake',
    );

    /// The concierge Salon: no client and no channel handle.
    const salon = Conversation(
      id: 'cnv_salon',
      kind: ConversationKind.aveline,
      customerName: 'Aveline',
    );

    /// A channel thread whose client is not identified yet, which is still a target.
    const channel = Conversation(
      id: 'cnv_channel',
      kind: ConversationKind.customer,
      externalRef: '94763475058',
    );

    test('excludes the thread the block is already in', () {
      final targets = forwardTargetsFrom(
        [nadeesha, menaka, salon, channel],
        nadeesha.id,
      );
      expect(targets.map((target) => target.id), [
        menaka.id,
        channel.id,
      ]);
    });

    test('never offers the client-less concierge Salon', () {
      final targets = forwardTargetsFrom([salon, nadeesha], null);
      expect(targets.map((target) => target.id), [nadeesha.id]);
      expect(
        targets.any((target) => target.label == 'Aveline'),
        isFalse,
      );
    });

    test('offers a channel thread whose client is not identified yet', () {
      final targets = forwardTargetsFrom([channel], nadeesha.id);
      expect(targets.single.id, channel.id);
      // Its name is the handle it was opened from, as the inbox prints it.
      expect(targets.single.label, '94763475058');
    });

    test('never offers a shop-wide notice', () {
      const notice = Conversation(id: 'cnv_notice', kind: ConversationKind.system);
      expect(forwardTargetsFrom([notice], null), isEmpty);
    });
  });
}
