/// The actions a content block affords, and the words they hand over.
///
/// A block is what an agent actually published — a draft (`suggestion`), a catalogue
/// piece (`piece`), a styled look (`look`) — and each one affords a different set of
/// things the associate can do with it. Which actions a block offers is a property of
/// the block, not of the bubble it happens to sit in, so the mapping lives here as
/// plain data with no Flutter in sight: every rule below can be pinned without
/// pumping a widget.
///
/// The vocabulary is deliberately narrow. The brief's "item block" is the wire's
/// `piece` and its "lookbook block" is the wire's `look`; the real names are the only
/// ones accepted, so a payload this build has not been taught gets **no** rail rather
/// than a guessed one. There is deliberately no `Share`: a block is sent to a
/// customer or forwarded to another thread, and a third path to "somewhere else"
/// would be a button with nothing behind it.
library;

import '../../../shared/utils/currency_formatter.dart';
import 'conversation.dart';
import 'thread_message.dart';

/// One thing an associate can do with a content block.
enum BlockActionId {
  copy,
  forward,
  sendToCustomer,
  regenerate,
}

/// The word on the segment, the tooltip and the accessible name.
const Map<BlockActionId, String> blockActionLabels = {
  BlockActionId.copy: 'Copy',
  BlockActionId.forward: 'Forward',
  BlockActionId.sendToCustomer: 'Send to customer',
  BlockActionId.regenerate: 'Regenerate',
};

/// Short labels for the narrow case.
///
/// A `piece` tile is around eleven rems wide and the rail shares that between its
/// segments. The accessible name always stays the full label; only the printed word
/// shortens.
const Map<BlockActionId, String> blockActionShortLabels = {
  BlockActionId.copy: 'Copy',
  BlockActionId.forward: 'Forward',
  BlockActionId.sendToCustomer: 'Send',
  BlockActionId.regenerate: 'Redo',
};

/// What the segment says while it waits on the server.
const Map<BlockActionId, String> blockActionBusyLabels = {
  BlockActionId.copy: 'Copying…',
  BlockActionId.forward: 'Forwarding…',
  BlockActionId.sendToCustomer: 'Sending…',
  BlockActionId.regenerate: 'Redoing…',
};

/// Which actions each block type offers, in the order they are drawn.
///
/// Absence is the answer: a type that is not listed gets no rail at all, and a listed
/// type gets exactly these — never a superset. `piece` carries Forward alone by
/// design, so a tile grid does not grow a Copy on every card and never a Regenerate.
const Map<String, List<BlockActionId>> _blockActions = {
  'suggestion': [
    BlockActionId.copy,
    BlockActionId.sendToCustomer,
    BlockActionId.regenerate,
  ],
  'piece': [BlockActionId.forward],
  'look': [
    BlockActionId.copy,
    BlockActionId.forward,
    BlockActionId.regenerate,
  ],
};

/// The actions a block type offers, in the order they are drawn.
///
/// An unknown type offers none, which is what keeps a block this build has not been
/// taught from growing a rail whose segments could not do anything sensible.
List<BlockActionId> actionsForBlockType(String type) =>
    _blockActions[type] ?? const [];

/// True when a block type draws a rail at all — the cheap check a renderer makes.
bool hasBlockActions(ThreadBlock block) =>
    actionsForBlockType(block.type).isNotEmpty;

/// The block's own name, or a noun for it.
///
/// Used as the toast subject ("Draft reply copied") and as the rail's accessible
/// name, so a segment's label is qualified by the card it belongs to.
String blockTitle(ThreadBlock block) {
  final name = block.name;
  if (name != null && name.trim().isNotEmpty) {
    return name.trim();
  }
  return switch (block.type) {
    'suggestion' => 'Draft reply',
    'piece' => 'Piece',
    'look' => 'Look',
    _ => 'Aveline',
  };
}

/// The block as words a person can paste or send.
///
/// Copy and Forward both hand over text, and the text has to be the **block** rather
/// than the message: the whole point of the rail is acting on one card. A `piece`
/// becomes a line an associate would actually send (`Silk Wrap Blouse · Size M · LKR
/// 18,500`), a `look` becomes its name and the styling note, and a `suggestion` is
/// already the sentence to send, so it is passed through with only the trailing
/// whitespace a model sometimes leaves trimmed.
String blockToText(ThreadBlock block) {
  switch (block.type) {
    case 'piece':
      return _pieceLine(block);
    case 'look':
      return _lookLine(block);
    case 'suggestion':
      return (block.text ?? '').trim();
    default:
      final text = (block.text ?? '').trim();
      return text.isEmpty ? blockTitle(block) : text;
  }
}

/// A piece as one line: its name, then whatever the server actually sent about it.
///
/// Nothing is invented for a missing half: a piece with no size prints no `Size`, and
/// one with no price prints no money. The currency is named (`LKR`) because the line
/// leaves the app and lands in someone else's chat, where a bare `18,500` is not
/// obviously rupees.
String _pieceLine(ThreadBlock block) {
  final parts = <String>[block.name ?? 'Piece'];
  final size = block.size;
  if (size != null && size.trim().isNotEmpty) {
    parts.add('Size ${size.trim()}');
  }
  final price = block.amount;
  if (price != null) {
    parts.add('LKR ${groupedNumber(price.round())}');
  }
  return parts.join(' · ');
}

/// A look as its name and its rationale, with neither half invented when it is missing.
///
/// An unnamed look does not print the fallback noun in front of its own note: the
/// em dash only earns its place when the server sent **both** halves.
String _lookLine(ThreadBlock block) {
  final name = block.name ?? '';
  final text = (block.text ?? '').trim();
  if (name.isNotEmpty && text.isNotEmpty) {
    return '$name — $text';
  }
  return text.isNotEmpty ? text : name;
}

/// What the surrounding thread can currently do, as the rail needs to know it.
class BlockActionEnvironment {
  const BlockActionEnvironment({
    required this.hasCustomerDestination,
    required this.hasForwardDestination,
    required this.agentBusy,
    this.pending,
  });

  /// True when the open thread is bound to a client, so "send to customer" has a
  /// destination.
  final bool hasCustomerDestination;

  /// True when there is at least one other client thread a forward could go to.
  final bool hasForwardDestination;

  /// True while the agent is already producing a reply for this thread.
  final bool agentBusy;

  /// The action on this block currently waiting on the server, if any.
  final BlockActionId? pending;
}

/// One segment of the rail, fully resolved.
class ResolvedBlockAction {
  const ResolvedBlockAction({
    required this.id,
    required this.label,
    required this.shortLabel,
    required this.enabled,
    required this.busy,
    this.reason,
  });

  final BlockActionId id;

  /// The full word, for the accessible name and the tooltip.
  final String label;

  /// The word printed on the segment, which shortens for a narrow rail.
  final String shortLabel;

  final bool enabled;

  /// True for the action that is waiting on the server right now.
  final bool busy;

  /// Why the action is unavailable. Present exactly when [enabled] is false.
  final String? reason;
}

/// Why an unavailable action is unavailable, in the app's own voice.
///
/// Every one is a sentence the associate can act on rather than a greyed-out word with
/// no explanation: a segment that does nothing must say why.
abstract final class BlockActionReasons {
  static const String pending =
      'Another action is already running on this block.';
  static const String noCustomer =
      "This thread isn't linked to a client yet.";
  static const String noForwardTarget =
      'No other client Salon to forward to yet.';
  static const String agentBusy = 'Aveline is still working on a reply.';
}

/// The rail's final state: which segments exist, which are live, and what to say when
/// one is not.
///
/// Only the actions the block type offers are ever returned, so Regenerate cannot
/// appear on a `piece` no matter what the thread can do. The rules that follow are
/// about availability within that set.
List<ResolvedBlockAction> resolveBlockActions(
  ThreadBlock block,
  BlockActionEnvironment environment,
) => [
  for (final id in actionsForBlockType(block.type)) _resolve(id, environment),
];

ResolvedBlockAction _resolve(
  BlockActionId id,
  BlockActionEnvironment environment,
) {
  final busy = environment.pending == id;
  final reason = _unavailableReason(id, environment);
  return ResolvedBlockAction(
    id: id,
    label: blockActionLabels[id]!,
    shortLabel: blockActionShortLabels[id]!,
    enabled: reason == null && !busy,
    busy: busy,
    // A busy segment states itself through its busy label; only a blocked one needs a
    // reason, and even then it is the reason rather than "busy".
    reason: reason ?? (busy ? BlockActionReasons.pending : null),
  );
}

/// The first reason an action cannot run, or `null` when it can.
String? _unavailableReason(
  BlockActionId id,
  BlockActionEnvironment environment,
) {
  // One action at a time on a block. A second would race the first over the same
  // content, and the server would be asked to send or redo the same card twice.
  if (environment.pending != null && environment.pending != id) {
    return BlockActionReasons.pending;
  }
  switch (id) {
    case BlockActionId.sendToCustomer:
      return environment.hasCustomerDestination
          ? null
          : BlockActionReasons.noCustomer;
    case BlockActionId.forward:
      return environment.hasForwardDestination
          ? null
          : BlockActionReasons.noForwardTarget;
    case BlockActionId.regenerate:
      return environment.agentBusy ? BlockActionReasons.agentBusy : null;
    case BlockActionId.copy:
      return null;
  }
}

/// A thread a forward can land in, as the picker needs to draw it.
class ForwardTarget {
  const ForwardTarget({
    required this.id,
    required this.label,
    this.lastMessageAt,
  });

  final String id;

  /// The name the inbox would print for the thread.
  final String label;

  /// When the thread last saw a word, so the list can read like the inbox behind it.
  final DateTime? lastMessageAt;
}

/// The threads a forward may choose between.
///
/// The open thread is excluded — forwarding a card into the thread it is already in
/// would only repeat it — and so is anything that cannot receive a delivery. A
/// delivery target is a thread bound to a client, or a channel thread whose client is
/// not identified yet but which carries a handle: `Conversation.isDeliveryTarget` is
/// exactly that test, which is what keeps the client-less concierge Salon out of the
/// list. The order the inbox served is kept, so the picker reads like the inbox.
List<ForwardTarget> forwardTargetsFrom(
  List<Conversation> conversations,
  String? currentConversationId,
) => [
  for (final conversation in conversations)
    if (conversation.id != currentConversationId && conversation.isDeliveryTarget)
      ForwardTarget(
        id: conversation.id,
        label: conversation.title,
        lastMessageAt: conversation.lastMessageAt,
      ),
];
