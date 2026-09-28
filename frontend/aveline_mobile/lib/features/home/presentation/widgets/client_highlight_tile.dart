import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../domain/client_highlight.dart';

/// Geometry shared by the row's tiles so avatars, badges and names line up
/// whatever tier a client is.
abstract final class ClientTileMetrics {
  /// The visible avatar.
  static const double avatarSize = 64;

  /// The fixed box the avatar and its ring sit in. Fixed rather than intrinsic
  /// so a VIP's thicker ring grows outwards instead of pushing the name down.
  static const double footprint = 76;

  /// Distance from the box edge to the avatar's edge.
  static const double avatarInset = (footprint - avatarSize) / 2;

  /// Gap between the avatar box and the name, which the tier badge overhangs.
  static const double nameGap = 14;

  /// The tile's height: avatar box, name gap, and one line of name.
  static const double height = footprint + nameGap + 18;
}

/// Muted paper tints for avatars, picked per client so the row reads as a set of
/// distinct people rather than a column of identical circles.
const List<Color> _avatarTints = [
  Color(0xFFE6E1D8),
  Color(0xFFDCE6E1),
  Color(0xFFEDE3D6),
  Color(0xFFE2E2DE),
  Color(0xFFE9DEE2),
  Color(0xFFDEE3E8),
];

Color _tintFor(String name) {
  final seed = name.codeUnits.fold<int>(0, (sum, unit) => sum + unit);
  return _avatarTints[seed % _avatarTints.length];
}

/// The value ramp: VIP wears the brand wine, levels run dark to light so the
/// stronger tier reads as the heavier badge.
///
/// Every step has to clear 4.5:1 against the white numeral it carries, which is
/// what rules out the pale greys a ramp like this invites: the original level-1
/// tone measured 3.1:1.
Color clientTierColor(ClientTier tier, ColorScheme scheme) => switch (tier) {
      ClientTier.vip => scheme.primary,
      ClientTier.level3 => const Color(0xFF3B3030),
      ClientTier.level2 => const Color(0xFF6E6363),
      ClientTier.level1 => const Color(0xFF7A6F6F),
    };

String clientTierLabel(ClientTier tier) =>
    tier.isVip ? 'VIP' : 'LVL ${tier.level}';

/// A client's initials in a tinted circle, ringed in wine for VIPs, with a mint
/// The circular avatar, tinted from the client's own name, with the wine ring
/// a VIP wears.
class ClientAvatar extends StatelessWidget {
  const ClientAvatar({
    super.key,
    required this.client,
    this.size = ClientTileMetrics.avatarSize,
  });

  final ClientHighlight client;
  final double size;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isVip = client.tier?.isVip ?? false;

    final circle = Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: _tintFor(client.name),
      ),
      child: Text(
        client.initials,
        // Proportional to the avatar rather than a type role: the same widget
        // serves the 64dp row tile and the 44dp sheet avatar, so the initials
        // have to scale with the circle they sit in.
        style: theme.textTheme.headlineSmall?.copyWith(
          fontSize: size * 0.32,
          letterSpacing: 0.5,
          color: scheme.onSurface.withValues(alpha: 0.78),
        ),
      ),
    );

    return Container(
      padding: EdgeInsets.all(isVip ? 2 : 0),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(
          color: isVip
              ? scheme.primary
              : scheme.outlineVariant.withValues(alpha: 0.7),
          width: isVip ? 2.5 : 1,
        ),
      ),
      child: circle,
    );
  }
}

/// The value badge that rides the bottom edge of an avatar: a wide `VIP` pill,
/// or the two-line `LVL n` circle for everyone else.
class ClientTierBadge extends StatelessWidget {
  const ClientTierBadge({super.key, required this.tier});

  final ClientTier tier;

  static const double _vipHeight = 18;
  static const double _levelDiameter = 30;

  /// Badge type is sized to the badge, not to the text scale.
  ///
  /// These are graphic marks riding a 64dp avatar, so they carry their own
  /// metrics rather than a `TextTheme` role — the `labelSmall` token (12) does
  /// not fit a two-line stack inside a 30dp circle. The floor is 9: the `LVL`
  /// kicker used to sit at 6.5, which is not a size a phone renders comfortably.
  static const double _vipFontSize = 10;
  static const double _kickerFontSize = 9;
  static const double _levelFontSize = 12;

  /// How tall the badge is, so the tile can centre it on the avatar's edge.
  static double heightFor(ClientTier tier) =>
      tier.isVip ? _vipHeight : _levelDiameter;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (tier.isVip) {
      return Container(
        height: _vipHeight,
        padding: const EdgeInsets.symmetric(horizontal: 10),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: scheme.primary,
          borderRadius: BorderRadius.circular(999),
        ),
        child: Text(
          'VIP',
          style: theme.textTheme.labelSmall?.copyWith(
            color: scheme.onPrimary,
            fontSize: _vipFontSize,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.8,
            height: 1,
          ),
        ),
      );
    }

    return Container(
      width: _levelDiameter,
      height: _levelDiameter,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: clientTierColor(tier, scheme),
        border: Border.all(
          color: scheme.surfaceContainerLowest,
          width: 1.5,
        ),
      ),
      // Scaled to the circle rather than laid out inside it. The two-line stack
      // is taller than the 27dp the border leaves under the fallback font
      // metrics the test harness and CI use, and it would overflow again under a
      // large OS text scale. Scaling down keeps the badge a fixed graphic mark
      // whatever the font does.
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              'LVL',
              style: theme.textTheme.labelSmall?.copyWith(
                color: scheme.onPrimary,
                fontSize: _kickerFontSize,
                fontWeight: FontWeight.w600,
                letterSpacing: 0.4,
                height: 1.1,
              ),
            ),
            Text(
              tier.level ?? '',
              style: theme.textTheme.labelSmall?.copyWith(
                color: scheme.onPrimary,
                fontSize: _levelFontSize,
                fontWeight: FontWeight.w700,
                height: 1.1,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// A one-line value pill for places with room for text beside a name.
class ClientTierChip extends StatelessWidget {
  const ClientTierChip({super.key, required this.tier});

  final ClientTier tier;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final color = clientTierColor(tier, scheme);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        clientTierLabel(tier).toUpperCase(),
        style: theme.textTheme.labelSmall?.copyWith(
          color: color,
          fontWeight: FontWeight.w700,
          letterSpacing: 0.8,
        ),
      ),
    );
  }
}

/// One client in the direct-client-line row: circular avatar, value badge on
/// its bottom edge, and their short name underneath.
class ClientHighlightTile extends StatelessWidget {
  const ClientHighlightTile({
    super.key,
    required this.client,
    required this.onTap,
  });

  final ClientHighlight client;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final tier = client.tier;

    return Semantics(
      button: true,
      label: tier == null
          ? client.shortName
          : '${client.shortName}, ${clientTierLabel(tier)}',
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(16),
          child: SizedBox(
            width: ClientTileMetrics.footprint,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                SizedBox(
                  width: ClientTileMetrics.footprint,
                  height: ClientTileMetrics.footprint,
                  child: Stack(
                    clipBehavior: Clip.none,
                    alignment: Alignment.center,
                    children: [
                      ClientAvatar(client: client),
                      // No grade, no badge: an ungraded client is not Level 1.
                      if (tier != null)
                        Positioned(
                          // Centred on the avatar's bottom edge, so the badge
                          // straddles the circle the way a wax seal would.
                          bottom: ClientTileMetrics.avatarInset -
                              ClientTierBadge.heightFor(tier) / 2,
                          child: ClientTierBadge(tier: tier),
                        ),
                    ],
                  ),
                ),
                const SizedBox(height: ClientTileMetrics.nameGap),
                Text(
                  client.shortName,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurface,
                    fontWeight: FontWeight.w500,
                    letterSpacing: 0.1,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The walk-in slot that leads the row: a dashed circle and a wine plus.
class AddClientTile extends StatelessWidget {
  const AddClientTile({super.key, required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Semantics(
      button: true,
      label: 'Add a walk-in client',
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(16),
          child: SizedBox(
            width: ClientTileMetrics.footprint,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                SizedBox(
                  width: ClientTileMetrics.footprint,
                  height: ClientTileMetrics.footprint,
                  child: Center(
                    child: CustomPaint(
                      painter: _DashedCirclePainter(
                        color: scheme.primary.withValues(alpha: 0.55),
                      ),
                      child: SizedBox(
                        width: ClientTileMetrics.avatarSize,
                        height: ClientTileMetrics.avatarSize,
                        child: Icon(
                          Icons.add_rounded,
                          size: 26,
                          color: scheme.primary,
                        ),
                      ),
                    ),
                  ),
                ),
                const SizedBox(height: ClientTileMetrics.nameGap),
                Text(
                  'Add New',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.primary,
                    fontWeight: FontWeight.w600,
                    letterSpacing: 0.1,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A dashed circle, the conventional "there is a slot here" affordance.
class _DashedCirclePainter extends CustomPainter {
  const _DashedCirclePainter({required this.color});

  final Color color;

  static const double _strokeWidth = 1.6;
  static const int _dashes = 12;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = _strokeWidth
      ..strokeCap = StrokeCap.round;

    final radius = (size.shortestSide - _strokeWidth) / 2;
    final center = size.center(Offset.zero);
    final step = 2 * math.pi / _dashes;
    const gap = 0.22;

    for (var i = 0; i < _dashes; i++) {
      canvas.drawArc(
        Rect.fromCircle(center: center, radius: radius),
        i * step,
        step - gap,
        false,
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(_DashedCirclePainter oldDelegate) =>
      oldDelegate.color != color;
}
