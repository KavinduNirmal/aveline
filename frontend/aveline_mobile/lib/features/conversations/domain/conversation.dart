/// What a conversation is, as far as the inbox is concerned.
///
/// The API carries a `kind` string (`Salon`, `Announcement`, `Digest`) and a
/// separate axis for who the thread is with. The context axis is read first (D1,
/// final): a thread bound to a client, or a channel thread whose client is not
/// identified yet, is a client thread whatever `kind` says; the general `Salon`
/// with neither is the caller's concierge thread; everything else is addressed to
/// the shop. Because the first two arms cover every thread that is not the
/// general Salon, at most one thread per caller classifies as [aveline].
enum ConversationKind {
  /// The caller's general thread with Aveline's agents. Pinned, and the only
  /// thread that reaches this arm.
  aveline,

  /// A thread with one client, including a channel thread whose client is not
  /// identified yet.
  customer,

  /// An announcement or a digest: addressed to the shop rather than a person.
  system,
}

/// An actionable state the server flagged on a conversation row.
///
/// The vocabulary is closed and the server sorts a row's markers by fixed
/// priority (`approval` → `choice` → `draft`), so the row can render the first
/// marker it is given. This is a set rather than a flag because a thread can hold
/// more than one at once.
enum ConversationMarker {
  approval('approval', 'Approval'),
  choice('choice', 'Pick the client'),
  draft('draft', 'Draft ready to copy');

  const ConversationMarker(this.apiValue, this.label);

  /// The string the API sends for this marker.
  final String apiValue;

  /// The words the row prints for this marker.
  final String label;

  /// The marker [raw] names, or `null` when it is not in the vocabulary.
  static ConversationMarker? fromJson(Object? raw) {
    if (raw is! String) {
      return null;
    }
    for (final marker in values) {
      if (marker.apiValue == raw) {
        return marker;
      }
    }
    return null;
  }
}

/// Where a conversation stands.
///
/// The row no longer keys its marker off this field: the server derives the
/// marker set ([ConversationMarker]) from the newest message and sorts it. The
/// status still records the thread's own state.
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
/// The API's list endpoint carries the identity of a thread, its last message and
/// the client's name; those arrive as optional fields so a row can be drawn from
/// either source without the screen knowing which one it is talking to.
///
/// **No read state.** No unread count is carried, on purpose: the release ships
/// no per-user read model, so a field that could never be filled was deleted
/// rather than left reporting a false `All caught up`.
class Conversation {
  const Conversation({
    required this.id,
    required this.kind,
    this.customerId,
    this.customerName,
    this.externalRef,
    this.threadId = '',
    this.status = ConversationStatus.active,
    this.lastMessageAt,
    this.lastMessagePreview,
    this.lastMessageAuthor,
    this.lastMessageBlock,
    this.lastMessageKind,
    this.lastMessageAgentKey,
    this.markers = const [],
  });

  final String id;
  final ConversationKind kind;

  /// The client the thread is with, or `null` for Aveline's thread and for
  /// anything addressed to the shop.
  final String? customerId;

  /// The client's name. Absent from the list endpoint today.
  final String? customerName;

  /// The channel reference the thread was opened from (for example a WhatsApp
  /// phone). Set on a channel-created thread whose client is not identified yet,
  /// which is what keeps it a client thread rather than the concierge.
  final String? externalRef;

  final String threadId;
  final ConversationStatus status;

  /// When the last message landed, or `null` in a thread nobody has spoken in.
  final DateTime? lastMessageAt;

  /// A one-line taste of the last message. Absent from the list endpoint today.
  final String? lastMessagePreview;

  final ConversationAuthor? lastMessageAuthor;

  /// The content-block type the preview was derived from: the row's category.
  final String? lastMessageBlock;

  /// The message kind as the wire sent it. Kept as wire truth; the row does not
  /// categorise by it, because agent output is `Note` for every persona.
  final String? lastMessageKind;

  /// The persona (`aveline`, `ava`, `elle`, `lina`) that wrote the last message,
  /// so the row can credit the persona rather than the umbrella brand.
  final String? lastMessageAgentKey;

  /// The markers the server derived for this row, in its priority order.
  final List<ConversationMarker> markers;

  /// What the row prints as the thread's name.
  static const String avelineTitle = 'Aveline';

  /// What a client thread prints before the client's name has arrived.
  static const String unnamedClientTitle = 'Client';

  bool get isAveline => kind == ConversationKind.aveline;

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
    final externalRef = _nonEmpty(json['externalRef']);

    return Conversation(
      id: json['id']?.toString() ?? '',
      kind: _classify(
        kind: kind,
        customerId: customerId,
        externalRef: externalRef,
      ),
      customerId: customerId,
      customerName: _nonEmpty(json['customerName']),
      externalRef: externalRef,
      threadId: json['threadId'] as String? ?? '',
      status: ConversationStatus.fromJson(json['status']),
      lastMessageAt: _timeFrom(json['lastMessageAt']),
      lastMessagePreview: _nonEmpty(json['lastMessagePreview']),
      lastMessageAuthor: ConversationAuthor.fromJson(json['lastMessageAuthor']),
      lastMessageBlock: _nonEmpty(json['lastMessageBlock']),
      lastMessageKind: _nonEmpty(json['lastMessageKind']),
      lastMessageAgentKey: _nonEmpty(json['lastMessageAgentKey']),
      markers: _markersFrom(json['markers']),
    );
  }

  /// The kind a thread is, from its context and the API's `kind`.
  ///
  /// The context axis is read first (D1, final): a customer-bound thread and a
  /// channel thread whose customer is not yet identified are both client threads,
  /// and `kind == 'Salon'` reaches the Aveline arm only when neither is set.
  static ConversationKind _classify({
    required String kind,
    required String? customerId,
    required String? externalRef,
  }) {
    if (customerId != null) {
      return ConversationKind.customer;
    }
    if (externalRef != null) {
      return ConversationKind.customer;
    }
    if (kind == 'Salon') {
      return ConversationKind.aveline;
    }
    return ConversationKind.system;
  }

  /// The known markers in [raw], preserving the server's order.
  static List<ConversationMarker> _markersFrom(Object? raw) {
    if (raw is! List) {
      return const [];
    }
    final markers = <ConversationMarker>[];
    for (final value in raw) {
      final marker = ConversationMarker.fromJson(value);
      if (marker != null) {
        markers.add(marker);
      }
    }
    return markers;
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

  @override
  String toString() => 'Conversation($id, ${kind.name})';
}
