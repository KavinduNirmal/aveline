import '../../../shared/utils/currency_formatter.dart';
import '../../../shared/utils/date_formatter.dart';
import 'customer_level.dart';

/// Where a client sits in the boutique's relationship with them.
///
/// Mirrors `CustomerProfileDto.Status`, which `CustomerLoyaltyService` derives
/// from spend, visits and recency. Distinct from [CustomerLevel]: the status is
/// the API's lifecycle, while the level is the grade the boutique works with, so
/// a client carries both.
enum CustomerStatus {
  /// The API's `new`. Declared as [fresh] because `new` is a reserved word in
  /// Dart; [wireValue] is what the API sends and stores.
  fresh('new', 'New'),
  returning('returning', 'Returning'),
  vip('vip', 'VIP'),
  dormant('dormant', 'Dormant');

  const CustomerStatus(this.wireValue, this.label);

  /// What the API sends.
  final String wireValue;

  /// What a screen prints.
  final String label;

  static CustomerStatus parse(String? value) {
    for (final status in values) {
      if (status.wireValue == value) {
        return status;
      }
    }
    return CustomerStatus.fresh;
  }

  /// The tier the API recomputes from a client's spend, visits and recency.
  ///
  /// This is `CustomerLoyaltyService.RecommendStatus`, thresholds and all:
  /// dormant once a client has been away for 90 days, VIP on more than 50,000
  /// spent across five visits, returning on more than 10,000 spent or two
  /// visits, and new otherwise. [now] is injectable so the rule can be tested
  /// without the clock moving under it.
  static CustomerStatus recommend({
    required double totalSpent,
    required int visitCount,
    DateTime? lastVisitAtUtc,
    DateTime? now,
  }) {
    const double returningSpendThreshold = 10000;
    const double vipSpendThreshold = 50000;
    const int returningMinVisits = 2;
    const int vipMinVisits = 5;
    const Duration dormancyAfter = Duration(days: 90);

    final last = lastVisitAtUtc;
    if (last != null &&
        (now ?? DateTime.now()).difference(last) >= dormancyAfter) {
      return CustomerStatus.dormant;
    }
    if (totalSpent > vipSpendThreshold && visitCount >= vipMinVisits) {
      return CustomerStatus.vip;
    }
    if (totalSpent > returningSpendThreshold ||
        visitCount >= returningMinVisits) {
      return CustomerStatus.returning;
    }
    return CustomerStatus.fresh;
  }
}

/// One client in the boutique's book.
///
/// Mirrors `CustomerProfileDto`. Two fields are not on the DTO yet: [nickname]
/// (the API carries preferences as key/value pairs, so a nickname is stored as a
/// preference today) and [level] (the API derives a status, not a grade). Both
/// are promoted here so the book can read them like any other field.
class Customer {
  const Customer({
    required this.id,
    required this.organizationId,
    required this.phoneNumber,
    required this.level,
    required this.status,
    this.fullName,
    this.nickname,
    this.email,
    this.totalSpent = 0,
    this.visitCount = 0,
    this.lastVisitAtUtc,
    this.createdAtUtc,
    this.tags = const <String>{},
  });

  /// `CustomerProfileDto.CustomerId`.
  final String id;

  /// `CustomerProfileDto` is tenant-scoped; the book is always one shop's.
  final String organizationId;

  /// How the boutique reaches the client. Identifies them per organization.
  final String phoneNumber;

  /// The grade the boutique works with, shown on the row's badge.
  final CustomerLevel level;

  /// The API's lifecycle status for this client.
  final CustomerStatus status;

  /// The client's full name, when the boutique has it.
  final String? fullName;

  /// What the floor calls them, when that is not their full name.
  final String? nickname;

  final String? email;

  final double totalSpent;
  final int visitCount;
  final DateTime? lastVisitAtUtc;
  final DateTime? createdAtUtc;

  /// The boutique's own tags for this client.
  final Set<String> tags;

  /// What the row prints as their name: the full name when the boutique has one,
  /// else the nickname, else the number they are reached on.
  String get displayName =>
      _present(fullName) ?? _present(nickname) ?? phoneNumber;

  /// Whether [displayName] is a nickname rather than a full name, so a screen can
  /// mark it as one.
  bool get isKnownByNickname =>
      _present(fullName) == null && _present(nickname) != null;

  /// Whether the boutique knows a nickname *alongside* a full name, which is the
  /// only case where saying what the counter calls them adds anything: when the
  /// nickname is already the display name, the row has said it.
  bool get hasNicknameAlias =>
      _present(fullName) != null && _present(nickname) != null;

  /// The two letters drawn in the row's circle, e.g. `EV`.
  ///
  /// Taken from one name rather than from both: a client with a full name and a
  /// nickname has one identity, and mixing the two would print `EE` for
  /// `Eleanor Vane (Ellie)`. A client the boutique has no name for wears `#`
  /// rather than the first character of their phone number.
  String get initials {
    final name = _present(fullName) ?? _present(nickname);
    if (name == null) {
      return '#';
    }

    final parts = name.split(RegExp(r'\s+')).where((p) => p.isNotEmpty).toList();
    if (parts.isEmpty) {
      return '#';
    }
    if (parts.length == 1) {
      return parts.first[0].toUpperCase();
    }
    return '${parts.first[0]}${parts.last[0]}'.toUpperCase();
  }

  /// The letter this client files under, for the alphabet index.
  ///
  /// `#` collects the clients the boutique has no name for: they file under
  /// their number, which is where a phone's contact list puts them too.
  String get sectionLetter {
    final name = displayName.trim();
    if (name.isEmpty) {
      return '#';
    }
    final first = name[0].toUpperCase();
    return RegExp(r'^[A-Z]$').hasMatch(first) ? first : '#';
  }

  /// The id the row prints under the name.
  ///
  /// The API keys customers by GUID, which is longer than a row can read, so a
  /// long id is shortened to its leading characters — still enough to read out
  /// to another system. The demo book's ids are already short and pass through.
  String get idLabel {
    const int maxLength = 12;
    if (id.length <= maxLength) {
      return id;
    }
    return '${id.substring(0, 8)}…';
  }

  /// Everything the book's search field matches against.
  String get searchHaystack => <String>[
    fullName ?? '',
    nickname ?? '',
    phoneNumber,
    email ?? '',
    id,
    for (final tag in tags) tag,
  ].join(' ').toLowerCase();

  /// `Rs 184,000`: what the client has spent with the boutique, ever.
  String get totalSpentLabel => rupees(totalSpent);

  /// `9 visits`, or `1 visit`.
  String get visitCountLabel => visitCount == 1 ? '1 visit' : '$visitCount visits';

  /// `Last visit 9 Sep 2026`, or the fact that they have not been in.
  String get lastVisitLabel => lastVisitAtUtc == null
      ? 'Never visited'
      : 'Last visit ${shortDate(lastVisitAtUtc!)}';

  /// When the client was filed, or `null` when the API did not say.
  String? get clientSinceLabel => createdAtUtc == null
      ? null
      : 'Client since ${shortDate(createdAtUtc!)}';

  /// The client's email, or `Not recorded` when the boutique has none.
  String get emailLabel => _present(email) ?? 'Not recorded';

  /// A copy with the fields this screen's actions change.
  Customer copyWith({
    CustomerLevel? level,
    CustomerStatus? status,
    int? visitCount,
    DateTime? lastVisitAtUtc,
  }) {
    return Customer(
      id: id,
      organizationId: organizationId,
      phoneNumber: phoneNumber,
      level: level ?? this.level,
      status: status ?? this.status,
      fullName: fullName,
      nickname: nickname,
      email: email,
      totalSpent: totalSpent,
      visitCount: visitCount ?? this.visitCount,
      lastVisitAtUtc: lastVisitAtUtc ?? this.lastVisitAtUtc,
      createdAtUtc: createdAtUtc,
      tags: tags,
    );
  }
}

/// The value with its surrounding space removed, or `null` when there was
/// nothing there to begin with.
String? _present(String? value) {
  final trimmed = value?.trim();
  return (trimmed == null || trimmed.isEmpty) ? null : trimmed;
}
