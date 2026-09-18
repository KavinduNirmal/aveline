import '../../../shared/utils/date_formatter.dart';
import 'customer.dart';

/// How the boutique is allowed to stay in touch with a client.
///
/// Mirrors `CustomerConsent`'s `ConsentStatus` column. `unknown` is what the API
/// reports when a client has no consent row at all (`CustomerProfileDto.From`
/// falls back to it), which is not the same as having been asked and not
/// answered.
enum ConsentStatus {
  granted('granted', 'Granted'),
  revoked('revoked', 'Revoked'),
  pending('pending', 'Pending'),
  unknown('unknown', 'Unknown');

  const ConsentStatus(this.wireValue, this.label);

  /// What the API stores.
  final String wireValue;

  /// What a screen prints.
  final String label;

  static ConsentStatus parse(String? value) {
    for (final status in values) {
      if (status.wireValue == value) {
        return status;
      }
    }
    return ConsentStatus.unknown;
  }

  /// Whether the boutique may message this client at all.
  bool get allowsContact => this == ConsentStatus.granted;
}

/// A client's consent to be contacted.
class CustomerConsent {
  const CustomerConsent({
    required this.status,
    this.grantedAtUtc,
    this.revokedAtUtc,
  });

  /// The state of a client the boutique has never asked.
  static const CustomerConsent unknown = CustomerConsent(
    status: ConsentStatus.unknown,
  );

  final ConsentStatus status;

  /// When consent was last granted, or `null`.
  final DateTime? grantedAtUtc;

  /// When consent was last withdrawn, or `null`.
  final DateTime? revokedAtUtc;

  String get statusLabel => status.label;

  /// `Granted 3 Aug 2026`, `Revoked 12 Sep 2026`, or what to do about it.
  String get detailLabel => switch (status) {
    ConsentStatus.granted when grantedAtUtc != null =>
      'Granted ${shortDate(grantedAtUtc!)}',
    ConsentStatus.granted => 'Granted',
    ConsentStatus.revoked when revokedAtUtc != null =>
      'Revoked ${shortDate(revokedAtUtc!)}',
    ConsentStatus.revoked => 'Revoked',
    ConsentStatus.pending => 'Asked, no answer yet',
    ConsentStatus.unknown => 'Never asked',
  };
}

/// What kind of thing a memory is.
///
/// Mirrors the agent service's `MemoryCategory`. An unrecognised category is
/// kept as [fact] rather than dropped: the API is allowed to grow this
/// vocabulary, and a memory the boutique cannot label is still a memory.
enum MemoryCategory {
  preference('preference', 'Preference'),
  event('event', 'Event'),
  complaint('complaint', 'Complaint'),
  fact('fact', 'Fact'),
  sentiment('sentiment', 'Sentiment');

  const MemoryCategory(this.wireValue, this.label);

  final String wireValue;
  final String label;

  static MemoryCategory parse(String? value) {
    for (final category in values) {
      if (category.wireValue == value) {
        return category;
      }
    }
    return MemoryCategory.fact;
  }
}

/// Where a memory came from. Mirrors the agent service's `MemorySource`.
enum MemorySource {
  conversation('conversation', 'Conversation'),
  staffNote('staff_note', 'Staff note'),
  purchase('purchase', 'Purchase'),
  inferred('inferred', 'Inferred');

  const MemorySource(this.wireValue, this.label);

  final String wireValue;
  final String label;

  static MemorySource parse(String? value) {
    for (final source in values) {
      if (source.wireValue == value) {
        return source;
      }
    }
    return MemorySource.conversation;
  }
}

/// One thing Aveline remembers about a client.
///
/// Mirrors `CustomerMemoryDto`. `confidence` is the semantic-search confidence
/// the API stores, which is why it is shown: a memory the boutique typed is not
/// the same as one the agent inferred from a conversation.
class CustomerMemory {
  const CustomerMemory({
    required this.id,
    required this.content,
    required this.category,
    required this.source,
    required this.createdAtUtc,
    this.isExplicit = false,
    this.confidence = 0.5,
  });

  final String id;
  final String content;
  final MemoryCategory category;
  final MemorySource source;
  final DateTime createdAtUtc;

  /// Whether the client said it themselves, rather than the agent inferring it.
  final bool isExplicit;

  /// `0.0` to `1.0`.
  final double confidence;

  String get categoryLabel => category.label;

  String get sourceLabel => source.label;

  /// `Stated` or `Inferred`: what the associate is actually being told.
  String get originLabel => isExplicit ? 'Stated by the client' : 'Inferred';

  /// `85%`.
  String get confidenceLabel => '${(confidence * 100).round()}%';

  /// `12 Sep 2026`.
  String get createdLabel => shortDate(createdAtUtc);
}

/// One thing the boutique knows about a client's taste.
///
/// Mirrors `CustomerPreferenceDto`. The entity also carries a source and
/// timestamps; the DTO does not, so they are not modelled here.
class CustomerPreference {
  const CustomerPreference({
    required this.id,
    required this.key,
    required this.value,
    required this.isExplicit,
    required this.confidence,
  });

  final String id;

  /// What the preference is about, e.g. `Fabric`.
  final String key;

  /// What it is, e.g. `Raw silk`.
  final String value;

  /// Whether the client stated it, rather than the agent inferring it.
  final bool isExplicit;

  final double confidence;

  String get originLabel => isExplicit ? 'Stated' : 'Inferred';

  String get confidenceLabel => '${(confidence * 100).round()}%';
}

/// Why something is happening for a client.
///
/// The API column is a free string (`AddEventRequest.EventType` defaults to
/// `other`); the agent service detects the occasions it knows about, which are
/// the ones named here. Anything else reads as [other].
enum CustomerEventType {
  wedding('wedding', 'Wedding'),
  birthday('birthday', 'Birthday'),
  anniversary('anniversary', 'Anniversary'),
  party('party', 'Party'),
  office('office', 'Office function'),
  function('function', 'Function'),
  other('other', 'Occasion');

  const CustomerEventType(this.wireValue, this.label);

  final String wireValue;
  final String label;

  static CustomerEventType parse(String? value) {
    for (final type in values) {
      if (type.wireValue == value) {
        return type;
      }
    }
    return CustomerEventType.other;
  }
}

/// A date that matters to a client, and the reminder the API sends ahead of it.
///
/// Mirrors `CustomerEventDto`. `EventReminderService` reminds the floor 30 days
/// ahead, which is the horizon [isSoon] reads.
class CustomerEvent {
  const CustomerEvent({
    required this.id,
    required this.type,
    required this.dateUtc,
    this.description,
    this.isActive = true,
  });

  final String id;
  final CustomerEventType type;
  final DateTime dateUtc;
  final String? description;

  /// The API's own flag for an event that is still being tracked.
  final bool isActive;

  /// How far ahead of an event the working floor wants to know.
  static const Duration reminderHorizon = Duration(days: 30);

  String get typeLabel => type.label;

  /// `12 Dec 2026`.
  String get dateLabel => shortDate(dateUtc);

  /// `in 12 days`, `Today`, `3 days ago`.
  String countdownLabel({DateTime? now}) => relativeDay(dateUtc, now: now);

  /// Whether the occasion is still ahead. An event happening today counts: the
  /// floor has the whole day to prepare for it.
  bool isUpcoming({DateTime? now}) =>
      isActive && !isBeforeToday(dateUtc, now: now);

  /// Whether the event is inside the window the reminder job watches.
  bool isSoon({DateTime? now}) {
    final days = daysUntil(dateUtc, now: now);
    return isActive && days >= 0 && days <= reminderHorizon.inDays;
  }
}

/// Where an interaction happened. Mirrors the agent service's `ChannelType`.
enum InteractionChannel {
  whatsapp('whatsapp', 'WhatsApp'),
  instagram('instagram', 'Instagram'),
  inPerson('in_person', 'In person'),
  phone('phone', 'Phone');

  const InteractionChannel(this.wireValue, this.label);

  final String wireValue;
  final String label;

  static InteractionChannel parse(String? value) {
    for (final channel in values) {
      if (channel.wireValue == value) {
        return channel;
      }
    }
    return InteractionChannel.whatsapp;
  }
}

/// Which way an interaction went. Mirrors `RecordInteractionRequest.Direction`.
enum InteractionDirection {
  inbound('inbound', 'From the client'),
  outbound('outbound', 'From the boutique');

  const InteractionDirection(this.wireValue, this.label);

  final String wireValue;
  final String label;

  static InteractionDirection parse(String? value) {
    for (final direction in values) {
      if (direction.wireValue == value) {
        return direction;
      }
    }
    return InteractionDirection.inbound;
  }
}

/// One exchange with a client. Mirrors `CustomerInteractionDto`.
class CustomerInteraction {
  const CustomerInteraction({
    required this.id,
    required this.channel,
    required this.direction,
    required this.createdAtUtc,
    this.messageContent,
  });

  final String id;
  final InteractionChannel channel;
  final InteractionDirection direction;
  final DateTime createdAtUtc;
  final String? messageContent;

  bool get isInbound => direction == InteractionDirection.inbound;

  String get channelLabel => channel.label;

  String get directionLabel => direction.label;

  String get createdLabel => shortDate(createdAtUtc);

  /// `Today · 14:32`.
  String whenLabel({DateTime? now}) =>
      relativeDayAndTime(createdAtUtc, now: now);
}

/// Everything the concierge knows about one client.
///
/// The profile the API assembles is a single call per resource — the profile,
/// the memories, the events, the interactions and the consent are separate
/// endpoints — so this is the screen's own view of one client, gathered once.
class CustomerDetail {
  const CustomerDetail({
    required this.customer,
    this.preferences = const <CustomerPreference>[],
    this.consent = CustomerConsent.unknown,
    this.memories = const <CustomerMemory>[],
    this.events = const <CustomerEvent>[],
    this.interactions = const <CustomerInteraction>[],
  });

  /// The profile: `CustomerProfileDto`.
  final Customer customer;

  /// `CustomerProfileDto.Preferences`.
  final List<CustomerPreference> preferences;

  /// `CustomerProfileDto.ConsentStatus`, with the row's own timestamps.
  final CustomerConsent consent;

  /// `CustomerMemoryDto`, newest first.
  final List<CustomerMemory> memories;

  /// `CustomerEventDto`.
  final List<CustomerEvent> events;

  /// `CustomerInteractionDto`, newest first.
  final List<CustomerInteraction> interactions;

  bool get hasPreferences => preferences.isNotEmpty;
  bool get hasMemories => memories.isNotEmpty;
  bool get hasEvents => events.isNotEmpty;
  bool get hasInteractions => interactions.isNotEmpty;

  /// The occasions still ahead, soonest first: what an associate reads before
  /// the client walks in.
  List<CustomerEvent> upcomingEvents({DateTime? now}) {
    final upcoming = events.where((event) => event.isUpcoming(now: now)).toList()
      ..sort((a, b) => a.dateUtc.compareTo(b.dateUtc));
    return upcoming;
  }

  /// The occasions that are no longer ahead: already past, or closed by the
  /// boutique, most recent first.
  List<CustomerEvent> pastEvents({DateTime? now}) {
    final past = events
        .where((event) => !event.isUpcoming(now: now))
        .toList()
      ..sort((a, b) => b.dateUtc.compareTo(a.dateUtc));
    return past;
  }

  /// The memories the boutique stated or that the client confirmed, which are
  /// the ones worth acting on rather than the agent's guesses.
  List<CustomerMemory> get statedMemories =>
      memories.where((memory) => memory.isExplicit).toList();

  /// A copy with the parts this screen's actions change.
  ///
  /// The API updates one resource per call — a new interaction, a recomputed
  /// status, a consent change — so the screen composes the same edits here until
  /// those calls are wired.
  CustomerDetail copyWith({
    Customer? customer,
    List<CustomerPreference>? preferences,
    CustomerConsent? consent,
    List<CustomerMemory>? memories,
    List<CustomerEvent>? events,
    List<CustomerInteraction>? interactions,
  }) {
    return CustomerDetail(
      customer: customer ?? this.customer,
      preferences: preferences ?? this.preferences,
      consent: consent ?? this.consent,
      memories: memories ?? this.memories,
      events: events ?? this.events,
      interactions: interactions ?? this.interactions,
    );
  }
}
