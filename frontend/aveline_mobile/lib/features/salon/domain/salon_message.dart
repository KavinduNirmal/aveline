import '../../conversations/domain/thread_message.dart';

/// Delivery status of an optimistic (not-yet-confirmed) staff message.
enum MessageDeliveryStatus {
  /// The message is in-flight to the backend.
  sending,

  /// The backend rejected the message.
  failed,
}

/// A selectable customer in a resolution `choice` content block (Issue #161).
class SalonChoiceOption {
  const SalonChoiceOption({
    required this.customerId,
    this.fullName,
    this.status,
  });

  final String customerId;
  final String? fullName;
  final String? status;

  factory SalonChoiceOption.fromJson(Map<String, dynamic> json) => SalonChoiceOption(
        customerId: json['customerId'] as String? ?? '',
        fullName: json['fullName'] as String?,
        status: json['status'] as String?,
      );

  /// The wire shape, so a choice the build already holds can be re-shaped into the
  /// block the renderer draws.
  Map<String, dynamic> toJson() => {
        'customerId': customerId,
        if (fullName != null) 'fullName': fullName,
        if (status != null) 'status': status,
      };
}

/// A single message in the Salon thread.
///
/// Mirrors the web `MessageDto` shape (author kind, agent key, kind, content) plus UI-only
/// fields: [deliveryStatus] marks an optimistic staff message, and [thoughtSeconds] records
/// how long Aveline "thought" before producing an agent message.
class SalonMessage {
  const SalonMessage({
    required this.id,
    required this.authorKind,
    this.agentKey,
    required this.text,
    required this.createdAt,
    this.deliveryStatus,
    this.thoughtSeconds,
    this.streamIn = false,
    this.choicePrompt = '',
    this.choiceOptions = const [],
    this.blocks = const [],
    this.contentHash,
    this.status,
  });

  final String id;

  /// `User` for staff-authored messages (right-aligned), `Agent` for Aveline
  /// and the specialists (left-aligned).
  final String authorKind;

  /// The persona key (`aveline`, `ava`, `elle`, `lina`) when [authorKind] is
  /// `Agent`.
  final String? agentKey;

  final String text;
  final DateTime createdAt;

  /// Non-null while an optimistic staff message is sending or has failed.
  final MessageDeliveryStatus? deliveryStatus;

  /// Seconds Aveline "thought" before this agent message was produced.
  final double? thoughtSeconds;

  /// True for agent messages that arrived live and should type out word by word.
  final bool streamIn;

  /// Heading of a resolution `choice` content block, when present (empty otherwise).
  final String choicePrompt;

  /// Candidate customers to pick from a resolution `choice` block, when present.
  final List<SalonChoiceOption> choiceOptions;

  /// Binds a sign-off decision to the exact content the approver was shown. The API
  /// rejects a decision whose hash does not match, so it travels with the message
  /// rather than being re-derived at the point of approval.
  final String? contentHash;

  /// The message's status on the wire (`AwaitingSignOff`, `Published`, ...).
  final String? status;

  /// A reply staged by an agent and waiting on the associate to release it.
  ///
  /// Only a staged message may be decided: an approved or dismissed one has already
  /// been answered, and the API would reject a second decision.
  bool get needsSignOff => status == 'AwaitingSignOff';

  /// The ordered content blocks the renderer draws.
  ///
  /// The Salon is a block renderer, not a text field: a run of `piece` blocks becomes
  /// a row of tiles, a `client_message` becomes the channel card, and only then does
  /// an agent's answer read the way the web reads it.
  final List<ThreadBlock> blocks;

  /// The blocks to draw, whichever way the message was built.
  ///
  /// A message parsed from the wire carries [blocks] and uses them as they are. A
  /// message this device composed — an optimistic send, or a preview in a test —
  /// carries only words or a resolution, so the block it would have arrived as is
  /// shaped here. That keeps one rendering path rather than two that can drift.
  List<ThreadBlock> get contentBlocks {
    if (blocks.isNotEmpty) {
      return blocks;
    }
    if (choiceOptions.isNotEmpty) {
      return [
        ThreadBlock('choice', {
          'prompt': choicePrompt,
          'options': [for (final option in choiceOptions) option.toJson()],
        }),
      ];
    }
    if (text.isNotEmpty) {
      return [
        ThreadBlock('text', {'text': text}),
      ];
    }
    return const [];
  }

  /// True while a live agent message should type out word by word instead of
  /// drawing its rich blocks.
  ///
  /// Streaming collapses the content to its first `text` block, so it is only safe
  /// for a message whose content is purely text. A rich answer — a brief plus tiles
  /// plus a suggestion — has to render its blocks at once, or its cards would be
  /// hidden until a manual reload.
  bool get streamsAsText =>
      streamIn &&
      contentBlocks.isNotEmpty &&
      contentBlocks.every((block) => block.type == 'text');

  bool get isOwn => authorKind == 'User';

  bool get isSending => deliveryStatus == MessageDeliveryStatus.sending;

  bool get isFailed => deliveryStatus == MessageDeliveryStatus.failed;

  /// Parses a wire `MessageDto` (from the API or SignalR `ReceiveMessage`) into a
  /// [SalonMessage]. The first `text` content block becomes [text]; a `choice` block fills
  /// [choicePrompt]/[choiceOptions].
  factory SalonMessage.fromJson(Map<String, dynamic> json) {
    final blocks = ThreadBlock.listFrom(json['contentBlocks']);
    var text = '';
    var choicePrompt = '';
    var choiceOptions = <SalonChoiceOption>[];
    for (final block in blocks) {
      if (block.type == 'text' && block.text != null) {
        // The last `text` block wins, which is what this model has always done and
        // is what the composer's own words read as.
        text = block.text!;
      } else if (block.type == 'choice') {
        choicePrompt = block.prompt ?? '';
        choiceOptions = [
          for (final option in block.options)
            SalonChoiceOption.fromJson(option),
        ];
      }
    }
    return SalonMessage(
      id: json['id'] as String? ?? '',
      authorKind: json['authorKind'] as String? ?? 'Agent',
      agentKey: json['agentKey'] as String?,
      text: text,
      createdAt:
          DateTime.tryParse(json['createdAt'] as String? ?? '')?.toLocal() ??
              DateTime.now(),
      choicePrompt: choicePrompt,
      choiceOptions: choiceOptions,
      blocks: blocks,
      contentHash: json['contentHash'] as String?,
      status: json['status'] as String?,
    );
  }

  SalonMessage copyWith({
    String? id,
    String? authorKind,
    String? agentKey,
    String? text,
    DateTime? createdAt,
    Object? deliveryStatus = _unset,
    Object? thoughtSeconds = _unset,
    bool? streamIn,
    String? choicePrompt,
    List<SalonChoiceOption>? choiceOptions,
    List<ThreadBlock>? blocks,
    String? contentHash,
    String? status,
  }) {
    return SalonMessage(
      id: id ?? this.id,
      authorKind: authorKind ?? this.authorKind,
      agentKey: agentKey ?? this.agentKey,
      text: text ?? this.text,
      createdAt: createdAt ?? this.createdAt,
      deliveryStatus: identical(deliveryStatus, _unset)
          ? this.deliveryStatus
          : deliveryStatus as MessageDeliveryStatus?,
      thoughtSeconds: identical(thoughtSeconds, _unset)
          ? this.thoughtSeconds
          : thoughtSeconds as double?,
      streamIn: streamIn ?? this.streamIn,
      choicePrompt: choicePrompt ?? this.choicePrompt,
      choiceOptions: choiceOptions ?? this.choiceOptions,
      blocks: blocks ?? this.blocks,
      contentHash: contentHash ?? this.contentHash,
      status: status ?? this.status,
    );
  }

  static const Object _unset = Object();
}
