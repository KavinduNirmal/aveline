import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../../domain/social_provider.dart';

/// The Google mark, exactly as the web's `AuthBits.tsx` draws it.
///
/// The web renders the four-colour "G" rather than the single-colour
/// `simple-icons` path the dashboard's brand marks use, so the same four paths
/// are kept here: the two platforms show the same mark on the same screen
/// instead of two imitations of it.
const String _googleGlyph = r'''
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path fill="#EA4335" d="M12 5.4c1.5 0 2.9.5 4 1.5l3-3C17.1 1.8 14.7 1 12 1 7.7 1 4 3.5 2.2 7.2l3.5 2.7C6.7 7.2 9.2 5.4 12 5.4Z"/><path fill="#4285F4" d="M21.6 12.2c0-.8-.1-1.5-.2-2.2H12v4.4h5.4c-.2 1.2-.9 2.2-1.9 2.9l3.4 2.6c2-1.9 3.1-4.7 3.1-7.7Z"/><path fill="#FBBC05" d="M5.7 14.3c-.3-.9-.4-1.9-.4-2.3s.2-1.4.4-2.3L2.2 7.2C1.4 8.7 1 10.3 1 12s.4 3.3 1.2 4.8l3.5-2.5Z"/><path fill="#34A853" d="M12 23c2.7 0 5.1-.9 6.9-2.4l-3.4-2.6c-.9.6-2.1 1-3.5 1-2.8 0-5.3-1.8-6.1-4.3l-3.5 2.5C4 19.9 7.7 23 12 23Z"/></svg>
''';

/// The Facebook mark, exactly as the web's `AuthBits.tsx` draws it.
const String _facebookGlyph = r'''
<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path fill="#1877F2" d="M24 12a12 12 0 1 0-13.9 11.9v-8.4h-3V12h3V9.4c0-3 1.8-4.7 4.6-4.7 1.3 0 2.7.2 2.7.2v3h-1.5c-1.5 0-2 .9-2 1.9V12h3.3l-.5 3.5h-2.8v8.4A12 12 0 0 0 24 12Z"/></svg>
''';

/// The marks this app draws itself, by the provider's brand key.
const Map<String, String> _glyphs = {
  'google': _googleGlyph,
  'facebook': _facebookGlyph,
};

/// The brand mark for [provider], drawn at [size] logical pixels.
///
/// The provider is named in words on the button beside this, so the mark is
/// decorative and carries no semantics of its own.
///
/// A provider this app has no mark for - the instance can enable any of Clerk's
/// - falls back to its initial rather than to the `logoUrl` the instance serves,
/// so a sign-in screen never waits on a third-party image to finish painting, and
/// a widget test never depends on the network.
class SocialProviderMark extends StatelessWidget {
  /// Construct a [SocialProviderMark].
  const SocialProviderMark({
    super.key,
    required this.provider,
    this.size = 18,
  });

  /// The provider whose mark to draw.
  final SocialProvider provider;

  /// The square the mark is drawn in.
  final double size;

  @override
  Widget build(BuildContext context) {
    final glyph = _glyphs[provider.brand];
    if (glyph != null) {
      return SvgPicture.string(
        glyph,
        width: size,
        height: size,
        excludeFromSemantics: true,
      );
    }

    final initial = provider.name.trim().isEmpty
        ? '?'
        : provider.name.trim().characters.first.toUpperCase();
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: Theme.of(context).colorScheme.primaryContainer,
      ),
      child: Text(
        initial,
        style: TextStyle(
          fontSize: size * 0.55,
          height: 1,
          fontWeight: FontWeight.w600,
          color: Theme.of(context).colorScheme.onPrimaryContainer,
        ),
      ),
    );
  }
}
