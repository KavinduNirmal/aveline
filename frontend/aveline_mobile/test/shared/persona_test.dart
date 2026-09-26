import 'package:aveline_mobile/shared/persona.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Lina's accent is her own colour, not the theme seed.
///
/// She used to share Aveline's wine-rose (`0xFF8B2E42`), which is also `AppTheme.seedColor` and
/// the colour the docs chrome and the pricing CTA are built on, so her avatar rendered as the
/// app's own accent rather than hers. This pins the separation in both directions: her accent is
/// the lilac, and nobody else's moved.
void main() {
  group('personaForAgent', () {
    test('gives Lina her own lilac', () {
      final lina = personaForAgent('lina');
      expect(lina.name, 'Lina');
      expect(lina.role, 'Commerce');
      expect(lina.accent, const Color(0xFF6B5A9E));
    });

    test('no longer gives Lina the theme seed', () {
      expect(personaForAgent('lina').accent, isNot(const Color(0xFF8B2E42)));
    });

    test('leaves every other agent accent untouched', () {
      expect(personaForAgent('aveline').accent, const Color(0xFF8B2E42));
      expect(personaForAgent('ava').accent, const Color(0xFFB0566B));
      expect(personaForAgent('elle').accent, const Color(0xFFB08D57));
    });

    test('gives all four agents distinct accents', () {
      final accents = {
        for (final key in ['aveline', 'ava', 'elle', 'lina'])
          personaForAgent(key).accent,
      };
      expect(accents.length, 4);
    });

    test('defaults to Aveline for an unknown or null key', () {
      expect(personaForAgent('nobody').name, 'Aveline');
      expect(personaForAgent(null).name, 'Aveline');
    });
  });

  group('personaForAuthor', () {
    test('returns the persona for an Agent author', () {
      expect(personaForAuthor('Agent', 'lina')?.name, 'Lina');
    });

    test('returns null for a non-agent author', () {
      expect(personaForAuthor('User', 'lina'), isNull);
      expect(personaForAuthor('System', null), isNull);
    });
  });
}
