/// What a conversation is, as far as the inbox is concerned.
///
/// The API carries a `kind` string whose enum only names `Salon`, `Announcement`
/// and `Digest` today: the backend models the one thread staff share with
/// Aveline's agents and has not yet modelled a thread per client. So the kind is
/// read as an intent rather than copied: a `Salon` is Aveline's, anything bound
/// to a client is a client's, and the rest is addressed to the shop. That rule
/// keeps working when the backend adds a kind for client threads.
enum ConversationKind {
  /// The unified thread staff share with Aveline's agents: the Salon. Pinned.
  aveline,

  /// A thread with one client.
  customer,

  /// An announcement or a digest: addressed to the shop rather than a person.
  system,
}

/// Where a conversation stands.
///
/// [awaitingSignOff] is the one the inbox acts on: a thread paused for a
/// human-in-the-loop decision is the only kind of conversation that is waiting on
/// the associate rather than the other way round.
enum ConversationStatus {
  active('Active'),
  awaitingSignOff('AwaitingSignOff'),
  resolved('Resolved'),
  archived('Archived'),

  /// A state this build has not been taught. Rendered as having no marker.
  unknown('');

  const ConversationStatus(this.apiValue);

  /// The `status` the API sends for this state.
  final String apiValue;

  static ConversationStatus fromJson(Object? raw) {
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

/// Who said the last word, which is what the row's preview is prefixed with.
enum ConversationAuthor {
  customer('Customer'),
  staff('Staff'),
  agent('Agent'),
  system('System');

  const ConversationAuthor(this.apiValue);

  final String apiValue;

  /// The author [raw] names, or `null` when the API did not say.
  static ConversationAuthor? fromJson(Object? raw) {
    if (raw is! String) {
      return null;
    }
    for (final author in values) {
      if (author.apiValue == raw) {
        return author;
      }
    }
    return null;
  }
}

/// One thread in the boutique's message inbox, mirroring the API's
/// `ConversationDto` and the richer row the inbox needs.
///
/// The API's list endpoint carries the identity of a thread but not its last
/// message, its unread count or the client's name; those arrive as optional
/// fields so a row can be drawn from either source without the screen knowing
/// which one it is talking to.
class Conversation {
  const Conversation({
    required this.id,
    required this.kind,
    this.customerId,
    this.customerName,
    this.threadId = '',
    this.status = ConversationStatus.active,
    this.lastMessageAt,
    this.lastMessagePreview,
    this.lastMessageAuthor,
    this.unreadCount = 0,
  });

  final String id;
  final ConversationKind kind;

  /// The client the thread is with, or `null` for Aveline's thread and for
  /// anything addressed to the shop.
  final String? customerId;

  /// The client's name. Absent from the list endpoint today.
  final String? customerName;

  final String threadId;
  final ConversationStatus status;

  /// When the last message landed, or `null` in a thread nobody has spoken in.
  final DateTime? lastMessageAt;

  /// A one-line taste of the last message. Absent from the list endpoint today.
  final String? lastMessagePreview;

  final ConversationAuthor? lastMessageAuthor;

  final int unreadCount;

  /// What the row prints as the thread's name.
  static const String avelineTitle = 'Aveline';

  /// What a client thread prints before the client's name has arrived.
  static const String unnamedClientTitle = 'Client';

  bool get isAveline => kind == ConversationKind.aveline;

  bool get isUnread => unreadCount > 0;

  /// Whether the thread is waiting on the associate for a decision.
  bool get needsSignOff => status == ConversationStatus.awaitingSignOff;

  /// The thread's name: the concierge's, the client's, or a stand-in.
  String get title => switch (kind) {
    ConversationKind.aveline => avelineTitle,
    ConversationKind.customer =>
      customerName == null || customerName!.isEmpty
          ? unnamedClientTitle
          : customerName!,
    ConversationKind.system => 'Announcement',
  };

  factory Conversation.fromJson(Map<String, dynamic> json) {
    final kind = json['kind'] as String? ?? '';
    final rawCustomerId = json['customerId']?.toString();
    final customerId = rawCustomerId == null || rawCustomerId.isEmpty
        ? null
        : rawCustomerId;

    return Conversation(
      id: json['id']?.toString() ?? '',
      kind: _classify(kind: kind, customerId: customerId),
      customerId: customerId,
      customerName: _nonEmpty(json['customerName']),
      threadId: json['threadId'] as String? ?? '',
      status: ConversationStatus.fromJson(json['status']),
      lastMessageAt: _timeFrom(json['lastMessageAt']),
      lastMessagePreview: _nonEmpty(json['lastMessagePreview']),
      lastMessageAuthor: ConversationAuthor.fromJson(json['lastMessageAuthor']),
      unreadCount: _countFrom(json['unreadCount']),
    );
  }

  /// The kind a thread is, from the API's `kind` and the client it names.
  static ConversationKind _classify({
    required String kind,
    required String? customerId,
  }) {
    if (kind == 'Salon') {
      return ConversationKind.aveline;
    }
    if (customerId != null) {
      return ConversationKind.customer;
    }
    return ConversationKind.system;
  }

  /// A non-empty string, or `null`.
  static String? _nonEmpty(Object? raw) {
    if (raw is! String || raw.isEmpty) {
      return null;
    }
    return raw;
  }

  /// An ISO-8601 timestamp, or `null` when it is absent or unreadable.
  static DateTime? _timeFrom(Object? raw) {
    if (raw is! String) {
      return null;
    }
    return DateTime.tryParse(raw);
  }

  /// A count that cannot be negative: a negative unread count is not a thing the
  /// badge should try to print.
  static int _countFrom(Object? raw) {
    final count = (raw as num?)?.toInt() ?? 0;
    return count < 0 ? 0 : count;
  }

  @override
  String toString() => 'Conversation($id, ${kind.name}, unread: $unreadCount)';
}
