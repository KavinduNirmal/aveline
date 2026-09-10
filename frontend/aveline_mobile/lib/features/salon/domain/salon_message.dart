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

  bool get isOwn => authorKind == 'User';

  bool get isSending => deliveryStatus == MessageDeliveryStatus.sending;

  bool get isFailed => deliveryStatus == MessageDeliveryStatus.failed;

  /// Parses a wire `MessageDto` (from the API or SignalR `ReceiveMessage`) into a
  /// [SalonMessage]. The first `text` content block becomes [text]; a `choice` block fills
  /// [choicePrompt]/[choiceOptions].
  factory SalonMessage.fromJson(Map<String, dynamic> json) {
    final blocks = json['contentBlocks'];
    var text = '';
    var choicePrompt = '';
    var choiceOptions = <SalonChoiceOption>[];
    if (blocks is List) {
      for (final block in blocks) {
        if (block is! Map) continue;
        if (block['type'] == 'text' && block['text'] is String) {
          text = block['text'] as String;
        } else if (block['type'] == 'choice') {
          choicePrompt = block['prompt'] as String? ?? '';
          final options = block['options'];
          if (options is List) {
            choiceOptions = options
                .whereType<Map>()
                .map((o) => SalonChoiceOption.fromJson(Map<String, dynamic>.from(o)))
                .toList();
          }
        }
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
    );
  }

  static const Object _unset = Object();
}
