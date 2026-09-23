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

/// What a message is, which is what a renderer switches on.
///
/// The kind is coarser than the content blocks: every persona message is
/// published as `Note` whatever category its blocks hold, so the thread draws the
/// blocks and uses the kind only for the three cases that are genuinely
/// kind-shaped — a client message, a SignOff, and the plain note.
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

/// One typed content block from the API's `contentBlocks` array.
///
/// The block vocabulary is Aveline's briefing model (`docs/architecture/inbox.md`
/// §5): `text`, `piece`, `look`, `at_a_glance`, `sign_off`, `payment`, `courier`,
/// `suggestion`, `client_message`, `choice`, plus the thread's own `attachment`.
/// The block's type is the category, so the renderer switches on that rather than
/// on the message's kind.
///
/// Fields are read through typed getters that tolerate an absent or badly-typed
/// value, because a payload this build has not been taught must render as a
/// one-line summary rather than throw.
class ThreadBlock {
  const ThreadBlock(this.type, this.data);

  final String type;
  final Map<String, dynamic> data;

  /// The blocks in [raw], in the order the server sent them.
  static List<ThreadBlock> listFrom(Object? raw) {
    if (raw is! List) {
      return const [];
    }
    final blocks = <ThreadBlock>[];
    for (final item in raw) {
      if (item is Map) {
        final block = ThreadBlock(
          item['type']?.toString() ?? '',
          Map<String, dynamic>.from(item),
        );
        blocks.add(block);
      }
    }
    return blocks;
  }

  String? get text => _string(data['text']);

  /// The channel handle a `client_message` came from.
  String? get from => _string(data['from']);

  String? get name => _string(data['name']);

  String? get status => _string(data['status']);

  String? get carrier => _string(data['carrier']);

  String? get reason => _string(data['reason']);

  String? get prompt => _string(data['prompt']);

  /// The `payment`/`sign_off` amount, or a `piece`'s price.
  num? get amount => _number('amount') ?? _number('price');

  /// The `at_a_glance` header row.
  List<String> get columns {
    final raw = data['columns'];
    if (raw is! List) {
      return const [];
    }
    return [for (final value in raw) value?.toString() ?? ''];
  }

  /// The `at_a_glance` body rows.
  List<List<String>> get rows {
    final raw = data['rows'];
    if (raw is! List) {
      return const [];
    }
    return [
      for (final row in raw)
        if (row is List) [for (final value in row) value?.toString() ?? ''],
    ];
  }

  /// The `choice` options, each a map with `customerId` and a display name.
  List<Map<String, dynamic>> get options {
    final raw = data['options'];
    if (raw is! List) {
      return const [];
    }
    return [
      for (final option in raw)
        if (option is Map) Map<String, dynamic>.from(option),
    ];
  }

  /// An `attachment` block's id.
  String? get attachmentId => _string(data['attachmentId']);

  /// An `attachment` block's authenticated URL. Read for a CDN-backed store; the local provider
  /// still needs the bytes fetched through the shared client.
  String? get url => _string(data['url']);

  String? get contentType => _string(data['contentType']);

  String? get fileName => _string(data['fileName']);

  int? get sizeBytes => _number('sizeBytes')?.toInt();

  int? get width => _number('width')?.toInt();

  int? get height => _number('height')?.toInt();

  /// Whether an `attachment` block is an image rather than a document.
  bool get isImage => (contentType ?? '').startsWith('image/');

  static String? _string(Object? raw) {
    if (raw is! String || raw.isEmpty) {
      return null;
    }
    return raw;
  }

  num? _number(String key) {
    final raw = data[key];
    return raw is num ? raw : null;
  }

  @override
  String toString() => 'ThreadBlock($type)';
}

/// One message in a thread with a client.
///
/// Mirrors the API's `MessageDto`. A plain message is a `text` block, and a
/// client's forwarded words are a `client_message` block whose own field is
/// `text`; the domain promotes both to [text]. The richer block kinds carry the
/// product's briefing cards and are rendered from [blocks], not from [kind]
/// alone, because an agent message is published as `Note` whatever category its
/// blocks hold.
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
    this.blocks = const [],
    this.clientMessageFrom,
    this.clientMessageId,
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

  /// The ordered content blocks the renderer draws.
  final List<ThreadBlock> blocks;

  /// The channel handle a `client_message` came from, when it carried one.
  final String? clientMessageFrom;

  /// The idempotency key one composed message carries across its send and every
  /// retry. A server message echoes the key it was stored under, which is what
  /// lets a realtime echo reconcile the optimistic row.
  final String? clientMessageId;

  /// The client's own message.
  bool get isFromClient => author == MessageAuthor.client;

  /// The associate's own message, which is the side the thread draws on the right.
  ///
  /// This, not [isFromClient], is the side test. The wire's `AuthorKind` has no
  /// `Client` member — a client's forwarded content arrives authored by `System` —
  /// so `!isFromClient` is true of every agent reply too, and testing it puts the
  /// associate and every persona on the same side of the thread.
  bool get isFromStaff => author == MessageAuthor.staff;

  /// Whether an agent persona wrote this, which is what credits the bubble to a
  /// persona name rather than to the shop.
  bool get isFromAgent => author == MessageAuthor.agent;

  /// The boutique's side of the conversation.
  bool get isFromBoutique => author != MessageAuthor.client && author != MessageAuthor.system;

  /// A note the associate wrote into the record that reached no customer channel.
  ///
  /// The backend marks an internal note `Published`: it is visible in the
  /// conversation and went nowhere. A client's forwarded message is published too,
  /// and so is an approved `SignOff` — which is a decision that was made, not a
  /// note — so both are excluded. An agent's published reply is not a note either:
  /// the thread draws it as the reply it is, credited to its persona.
  bool get isInternalNote =>
      isFromStaff && kind != MessageKind.signOff && status == MessageStatus.published;

  /// A reply staged by an agent and waiting on the associate to release it.
  bool get needsSignOff => status == MessageStatus.awaitingSignOff;

  /// A SignOff the associate released.
  bool get isApprovedSignOff =>
      kind == MessageKind.signOff && status == MessageStatus.published;

  /// A SignOff the associate dropped.
  bool get isDismissedSignOff =>
      kind == MessageKind.signOff && status == MessageStatus.cancelled;

  bool get isSending => deliveryStatus == MessageDeliveryStatus.sending;

  bool get isFailed => deliveryStatus == MessageDeliveryStatus.failed;

  /// Whether the client's device has it.
  bool get isDelivered =>
      status == MessageStatus.delivered || status == MessageStatus.read;

  /// Whether the client has opened it.
  bool get isRead => status == MessageStatus.read;

  /// The `sign_off` block, when this message carries one.
  ThreadBlock? get signOffBlock {
    for (final block in blocks) {
      if (block.type == 'sign_off') {
        return block;
      }
    }
    return null;
  }

  /// The blocks the bubble draws below its own words.
  ///
  /// The three the bubble already accounts for are left out: the `text` and
  /// `client_message` blocks *are* the bubble's words, and `sign_off` is drawn by
  /// the overline and the decision row rather than as a card.
  List<ThreadBlock> get bodyBlocks => [
    for (final block in blocks)
      if (block.type != 'text' &&
          block.type != 'client_message' &&
          block.type != 'sign_off')
        block,
  ];

  factory ThreadMessage.fromJson(Map<String, dynamic> json) {
    final kind = MessageKind.fromJson(json['kind']);
    final blocks = ThreadBlock.listFrom(json['contentBlocks']);
    return ThreadMessage(
      id: json['id']?.toString() ?? '',
      // A forwarded client message is authored by `System` on the wire and by the
      // client in the conversation.
      author: kind == MessageKind.clientMessage
          ? MessageAuthor.client
          : MessageAuthor.fromJson(json['authorKind']),
      kind: kind,
      text: _textFrom(blocks),
      createdAt:
          DateTime.tryParse(json['createdAt'] as String? ?? '') ??
          DateTime.now().toUtc(),
      agentKey: json['agentKey'] as String?,
      status: MessageStatus.fromJson(json['status']),
      contentHash: _nonEmpty(json['contentHash']),
      deliveryStatus: null,
      replyToMessageId: _nonEmpty(json['replyToMessageId']),
      blocks: blocks,
      clientMessageFrom: _fromOf(blocks),
      clientMessageId: _nonEmpty(json['clientMessageId']),
    );
  }

  /// A copy with the state this device owns changed.
  ///
  /// Only the fields a device may revise travel: the delivery state it is
  /// tracking, the status of a message a decision just changed, and the blocks of
  /// one the server has replaced.
  ThreadMessage copyWith({
    MessageStatus? status,
    MessageDeliveryStatus? deliveryStatus,
    bool clearDeliveryStatus = false,
    List<ThreadBlock>? blocks,
    String? text,
  }) {
    return ThreadMessage(
      id: id,
      author: author,
      kind: kind,
      text: text ?? this.text,
      createdAt: createdAt,
      agentKey: agentKey,
      status: status ?? this.status,
      contentHash: contentHash,
      deliveryStatus: clearDeliveryStatus
          ? null
          : (deliveryStatus ?? this.deliveryStatus),
      replyToMessageId: replyToMessageId,
      blocks: blocks ?? this.blocks,
      clientMessageFrom: clientMessageFrom,
      clientMessageId: clientMessageId,
    );
  }

  /// The message's own words: the `client_message` block's text, else the first
  /// `text` block. A payload with neither reads as empty rather than throwing.
  static String _textFrom(List<ThreadBlock> blocks) {
    for (final block in blocks) {
      if (block.type == 'client_message' && block.text != null) {
        return block.text!;
      }
    }
    for (final block in blocks) {
      if (block.type == 'text' && block.text != null) {
        return block.text!;
      }
    }
    return '';
  }

  /// The channel handle of the message's `client_message` block, when it has one.
  static String? _fromOf(List<ThreadBlock> blocks) {
    for (final block in blocks) {
      if (block.type == 'client_message') {
        return block.from;
      }
    }
    return null;
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
