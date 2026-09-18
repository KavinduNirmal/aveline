import 'package:flutter/material.dart';

import '../../domain/notification_kind.dart';

/// The icon and tint a notification wears for its kind.
///
/// The six accents are brand-adjacent rather than brand-exact. The palette in
/// `.agents/brain/DESIGN.md` names three agent states - memory, visual, commerce
/// - and the inbox has six kinds to keep apart at a glance, so four hues are
/// added. All of them stay warm and low-saturation, which is what keeps the
/// inbox reading as the same room as the rest of the app rather than as a set of
/// system alerts.
class NotificationKindVisuals {
  const NotificationKindVisuals({required this.icon, required this.tint});

  /// The mark inside the tile's enclosure.
  final IconData icon;

  /// The enclosure's ink and, at a tenth of its strength, its fill.
  final Color tint;

  /// Wine, for a client speaking.
  static const Color _wine = Color(0xFF8B2E42);

  /// Brass, for something waiting on a person.
  static const Color _brass = Color(0xFF9A6B2F);

  /// Rose, for money settled.
  static const Color _rose = Color(0xFFA84056);

  /// Muted magenta-rose, the memory hue, for a relationship cooling.
  static const Color _magenta = Color(0xFF7A2E5C);

  /// Warm taupe, for the calendar.
  static const Color _taupe = Color(0xFF6B5346);

  /// Soft gold, the visual hue, for a piece found.
  static const Color _gold = Color(0xFFB08A3E);

  static const Map<NotificationKind, NotificationKindVisuals> _byKind = {
    NotificationKind.newMessage: NotificationKindVisuals(
      icon: Icons.chat_bubble_outline_rounded,
      tint: _wine,
    ),
    NotificationKind.approvalNeeded: NotificationKindVisuals(
      icon: Icons.pending_actions_rounded,
      tint: _brass,
    ),
    NotificationKind.paymentConfirmed: NotificationKindVisuals(
      icon: Icons.receipt_long_outlined,
      tint: _rose,
    ),
    NotificationKind.vipAtRisk: NotificationKindVisuals(
      icon: Icons.workspace_premium_outlined,
      tint: _magenta,
    ),
    NotificationKind.eventReminder: NotificationKindVisuals(
      icon: Icons.event_available_outlined,
      tint: _taupe,
    ),
    NotificationKind.newMatch: NotificationKindVisuals(
      icon: Icons.auto_awesome_outlined,
      tint: _gold,
    ),
  };

  /// What a kind this build has not been taught wears.
  static const NotificationKindVisuals _fallback = NotificationKindVisuals(
    icon: Icons.notifications_none_rounded,
    tint: Color(0xFF534244),
  );

  /// The visuals for [kind], falling back to a neutral mark.
  static NotificationKindVisuals of(NotificationKind kind) =>
      _byKind[kind] ?? _fallback;
}
