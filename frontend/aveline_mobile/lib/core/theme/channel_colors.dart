import 'package:flutter/material.dart';

/// The WhatsApp channel tokens, mirroring the `--whatsapp*` custom properties in
/// the web's `src/index.css`.
///
/// `whatsapp` is the brand green and is only ever a mark or a tint; [whatsappDeep]
/// is the ink that carries the channel's name, because white on the brand green
/// fails contrast at label sizes; [whatsappCanvas] is the field the customer's
/// words sit on, the way a chat app paints its own background.
///
/// A `ColorScheme` has no slot for a channel, and the app themes itself through
/// [ThemeData], so the pair is resolved from the brightness the way the web's
/// `:root` / `.dark` blocks are. Both themes are carried so the Salon is already
/// correct on the day the app gains a dark mode.
@immutable
class ChannelColors {
  const ChannelColors({
    required this.whatsapp,
    required this.whatsappForeground,
    required this.whatsappDeep,
    required this.whatsappCanvas,
  });

  /// The brand green: a mark, a tint, a border — never a field for text.
  final Color whatsapp;

  /// What sits on [whatsapp] at mark sizes, where the green is dark enough.
  final Color whatsappForeground;

  /// The ink that names the channel, and the only accessible green at label sizes.
  final Color whatsappDeep;

  /// The field the customer's own words sit on.
  final Color whatsappCanvas;

  /// The light-theme values, as the web defines them on `:root`.
  static const ChannelColors light = ChannelColors(
    whatsapp: Color(0xFF25D366),
    whatsappForeground: Color(0xFFFFFFFF),
    whatsappDeep: Color(0xFF0B5D4F),
    whatsappCanvas: Color(0xFFEFF8F2),
  );

  /// The dark-theme values, as the web defines them on `.dark`.
  static const ChannelColors dark = ChannelColors(
    whatsapp: Color(0xFF25D366),
    whatsappForeground: Color(0xFFFFFFFF),
    whatsappDeep: Color(0xFF8FE3B0),
    whatsappCanvas: Color(0xFF1B2A24),
  );

  /// The tokens for the theme in scope.
  static ChannelColors of(BuildContext context) =>
      Theme.of(context).brightness == Brightness.dark ? dark : light;
}
