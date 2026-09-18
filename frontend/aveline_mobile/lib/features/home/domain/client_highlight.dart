/// The client's value to the boutique, strongest first.
///
/// Mirrors the loyalty tier the API recomputes from spend and visits (see
/// `CustomerConciergeEndpoints`' status route). VIPs wear the wine ring and pill;
/// everyone else wears their level, the darker the badge the stronger the tier.
enum ClientTier {
  vip(isVip: true, level: null),
  level3(isVip: false, level: '3'),
  level2(isVip: false, level: '2'),
  level1(isVip: false, level: '1');

  const ClientTier({required this.isVip, required this.level});

  /// VIP clients are marked with the brand wine rather than a level.
  final bool isVip;

  /// The digit inside the `LVL` badge, or `null` for [ClientTier.vip].
  final String? level;
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
    this.hasNewActivity = false,
  });

  final String id;

  final String name;

  final ClientTier tier;

  /// Why they are in this row, in the associate's words. Shown in the
  /// all-clients sheet, where there is room for a line of context.
  final String activity;

  /// Whether something has arrived that the associate has not looked at yet.
  final bool hasNewActivity;

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
