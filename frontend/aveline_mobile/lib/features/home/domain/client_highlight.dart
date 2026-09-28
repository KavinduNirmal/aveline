/// The client's value to the boutique, strongest first.
///
/// Mirrors the grade the server stores on `Customer.Level`
/// (`vip | level3 | level2 | level1`). VIPs wear the wine ring and pill;
/// everyone else wears their level, the darker the badge the stronger the tier.
enum ClientTier {
  vip(isVip: true, level: null, wireValue: 'vip'),
  level3(isVip: false, level: '3', wireValue: 'level3'),
  level2(isVip: false, level: '2', wireValue: 'level2'),
  level1(isVip: false, level: '1', wireValue: 'level1');

  const ClientTier({
    required this.isVip,
    required this.level,
    required this.wireValue,
  });

  /// VIP clients are marked with the brand wine rather than a level.
  final bool isVip;

  /// The digit inside the `LVL` badge, or `null` for [ClientTier.vip].
  final String? level;

  /// The wire value the API stores.
  final String wireValue;

  /// The tier a wire value names, or `null` when it is absent or unknown.
  ///
  /// Nullable on purpose: the server stores no grade until the boutique sets
  /// one, and a default would invent a grade for every client.
  static ClientTier? fromWire(String? value) {
    for (final tier in values) {
      if (tier.wireValue == value) {
        return tier;
      }
    }
    return null;
  }
}

/// A client worth surfacing on Home because something is happening with them.
///
/// This is Home's own view of a client. The customers slice owns the full
/// customer entity and maps it into this highlight; Home deliberately does not
/// import that feature's domain layer (see `features/customers/README.md`).
class ClientHighlight {
  const ClientHighlight({
    required this.id,
    required this.name,
    required this.tier,
    required this.activity,
  });

  /// The profile route id. A real customer's GUID, or a local id inside a test.
  final String id;

  final String name;

  /// The grade the boutique works with, or `null` when nobody has graded this
  /// client. The tile hides the badge rather than inventing one.
  final ClientTier? tier;

  /// Why they are in this row, in the associate's words. Shown in the
  /// all-clients sheet, where there is room for a line of context.
  final String activity;

  /// `Eleanor Vane` reads as `Eleanor V.` under a circular avatar.
  String get shortName {
    final parts = _parts;
    if (parts.length < 2) return name.trim();
    return '${parts.first} ${parts.last[0].toUpperCase()}.';
  }

  /// The two letters drawn inside the avatar.
  String get initials {
    final parts = _parts;
    if (parts.isEmpty) return '?';
    if (parts.length == 1) return parts.first[0].toUpperCase();
    return '${parts.first[0]}${parts.last[0]}'.toUpperCase();
  }

  List<String> get _parts => name
      .trim()
      .split(RegExp(r'\s+'))
      .where((part) => part.isNotEmpty)
      .toList();
}
