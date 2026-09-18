/// Who a message in a client thread came from, as the thread draws it.
///
/// Note that this is not quite the wire's `authorKind`. A boutique's client is
/// external and never a sender on the wire: their inbound WhatsApp or Instagram
/// content is forwarded as a `ClientMessage` card authored by `System`. The
/// content is still the client's, and a thread that drew it as a system notice
/// would be lying about the conversation, so [ThreadMessage.fromJson] promotes it.
enum MessageAuthor {
  client('Client'),
  staff('User'),
  agent('Agent'),
  system('System');

  const MessageAuthor(this.apiValue);

  final String apiValue;

  static MessageAuthor fromJson(Object? raw) {
    if (raw is! String) {
      return system;
    }
    for (final author in values) {
      if (author.apiValue == raw) {
        return author;
      }
    }
    return system;
  }
}

/// What a message is, which is what a renderer would switch on.
///
/// Only `clientMessage` and `note` change how the thread draws anything today.
/// The rest are the quiet-luxury block kinds the backend already emits - a look,
/// a piece, a payment link - and a thread renders them from their text block
/// until each gets the card it deserves.
enum MessageKind {
  note('Note'),
  clientMessage('ClientMessage'),
  look('Look'),
  piece('Piece'),
  atAGlance('AtAGlance'),
  signOff('SignOff'),
  payment('Payment'),
  courier('Courier'),
  suggestion('Suggestion'),

  /// A kind this build has not been taught. Drawn as plain text.
  unknown('');

  const MessageKind(this.apiValue);

  final String apiValue;

  static MessageKind fromJson(Object? raw) {
    if (raw is! String) {
      return unknown;
    }
    for (final kind in values) {
      if (kind != unknown && kind.apiValue == raw) {
        return kind;
      }
    }
    return unknown;
  }
}

/// Where a message stands in its life.
///
/// The two that change how the thread reads are [published], which means the
/// client never saw it, and [awaitingSignOff], which means it is staged and
/// waiting on the associate.
enum MessageStatus {
  draft('Draft'),
  awaitingSignOff('AwaitingSignOff'),
  published('Published'),
  sent('Sent'),
  delivered('Delivered'),
  read('Read'),
  failed('Failed'),
  cancelled('Cancelled'),

  /// A state this build has not been taught.
  unknown('');

  const MessageStatus(this.apiValue);

  final String apiValue;

  static MessageStatus fromJson(Object? raw) {
    if (raw is! String) {
      return unknown;
    }
    for (final status in values) {
      if (status != unknown && status.apiValue == raw) {
        return status;
      }
    }
    return unknown;
  }
}

/// The state of a message this device is still trying to send.
///
/// Server messages have no such state: this is the gap between the send button
/// and the API answering, which the thread has to draw because the associate is
/// owed an answer about whether their words went anywhere.
enum MessageDeliveryStatus { sending, failed }

/// One message in a thread with a client.
///
/// Mirrors the API's `MessageDto`, with the first `text` content block promoted
/// to [text] the way the Salon's model does. The richer block kinds are carried
/// by [kind] and rendered from [text] for now.
class ThreadMessage {
  const ThreadMessage({
    required this.id,
    required this.author,
    required this.text,
    required this.createdAt,
    this.kind = MessageKind.note,
    this.agentKey,
    this.status = MessageStatus.published,
    this.contentHash,
    this.deliveryStatus,
    this.replyToMessageId,
  });

  final String id;
  final MessageAuthor author;
  final MessageKind kind;
  final String text;
  final DateTime createdAt;

  /// The persona key when [author] is [MessageAuthor.agent].
  final String? agentKey;

  final MessageStatus status;

  /// Binds a sign-off decision to the exact content the approver was shown. The
  /// API rejects a decision whose hash does not match, so it travels with the
  /// message rather than being re-derived at the point of approval.
  final String? contentHash;

  /// Non-null only for a message this device is still sending.
  final MessageDeliveryStatus? deliveryStatus;

  final String? replyToMessageId;

  /// The client's own message.
  bool get isFromClient => author == MessageAuthor.client;

  /// The boutique's side of the conversation, which is drawn on the right.
  bool get isFromBoutique => author != MessageAuthor.client && author != MessageAuthor.system;

  /// A message the client never saw.
  ///
  /// The backend marks an internal note `Published`: it is visible in the
  /// conversation and went nowhere. A client's forwarded message is published too,
  /// which is why the author is checked as well.
  bool get isInternalNote => !isFromClient && status == MessageStatus.published;

  /// A reply staged by an agent and waiting on the associate to release it.
  bool get needsSignOff => status == MessageStatus.awaitingSignOff;

  bool get isSending => deliveryStatus == MessageDeliveryStatus.sending;

  bool get isFailed => deliveryStatus == MessageDeliveryStatus.failed;

  /// Whether the client's device has it.
  bool get isDelivered =>
      status == MessageStatus.delivered || status == MessageStatus.read;

  /// Whether the client has opened it.
  bool get isRead => status == MessageStatus.read;

  factory ThreadMessage.fromJson(Map<String, dynamic> json) {
    final kind = MessageKind.fromJson(json['kind']);
    return ThreadMessage(
      id: json['id']?.toString() ?? '',
      // A forwarded client message is authored by `System` on the wire and by the
      // client in the conversation.
      author: kind == MessageKind.clientMessage
          ? MessageAuthor.client
          : MessageAuthor.fromJson(json['authorKind']),
      kind: kind,
      text: _textFrom(json['contentBlocks']),
      createdAt:
          DateTime.tryParse(json['createdAt'] as String? ?? '') ??
          DateTime.now().toUtc(),
      agentKey: json['agentKey'] as String?,
      status: MessageStatus.fromJson(json['status']),
      contentHash: _nonEmpty(json['contentHash']),
      deliveryStatus: null,
      replyToMessageId: _nonEmpty(json['replyToMessageId']),
    );
  }

  /// A copy with the delivery state or the status changed.
  ///
  /// Only the fields the device owns are copyable: the delivery state it is
  /// tracking, and the status of a draft it has just decided.
  ThreadMessage copyWith({
    MessageStatus? status,
    MessageDeliveryStatus? deliveryStatus,
    bool clearDeliveryStatus = false,
  }) {
    return ThreadMessage(
      id: id,
      author: author,
      kind: kind,
      text: text,
      createdAt: createdAt,
      agentKey: agentKey,
      status: status ?? this.status,
      contentHash: contentHash,
      deliveryStatus: clearDeliveryStatus
          ? null
          : (deliveryStatus ?? this.deliveryStatus),
      replyToMessageId: replyToMessageId,
    );
  }

  /// The first `text` block, which is the whole message for anything but a rich
  /// card. A payload with no text block reads as empty rather than throwing.
  static String _textFrom(Object? raw) {
    if (raw is! List) {
      return '';
    }
    for (final block in raw) {
      if (block is Map && block['type'] == 'text' && block['text'] is String) {
        return block['text'] as String;
      }
    }
    return '';
  }

  static String? _nonEmpty(Object? raw) {
    if (raw is! String || raw.isEmpty) {
      return null;
    }
    return raw;
  }

  @override
  String toString() => 'ThreadMessage($id, ${author.name}, ${status.name})';
}
