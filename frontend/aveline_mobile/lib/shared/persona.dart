import 'package:flutter/material.dart';

/// Display metadata for an Aveline agent persona, mirroring the web
/// `persona.ts`. Accent colors follow `.agents/brain/DESIGN.md`.
class Persona {
  const Persona({
    required this.name,
    required this.accent,
    required this.role,
  });

  final String name;
  final Color accent;
  final String role;
}

/// Aveline's primary wine-rose.
const Color _avelineAccent = Color(0xFF8B2E42);

/// Memory's muted magenta-rose.
const Color _avaAccent = Color(0xFFB0566B);

/// Visual's soft gold/brass.
const Color _elleAccent = Color(0xFFB08D57);

/// Commerce's lilac.
///
/// Lina used to share Aveline's wine-rose (`0xFF8B2E42`), which is also the theme seed and the
/// colour the docs chrome and the pricing CTA are built on — so her avatar was indistinguishable
/// from the app's own accent. She now has her own lilac, matching `--aveline-lilac` in the web
/// palette. It is deliberately deeper than the decorative lavender (`0xFF8E7CC3`) because her
/// name label and avatar glyphs sit on this colour at 11px, where lavender is only ~3.6:1 against
/// white; this clears WCAG AA at ~5.9:1.
const Color _linaAccent = Color(0xFF6B5A9E);

const Map<String, Persona> _personas = {
  'aveline': Persona(name: 'Aveline', accent: _avelineAccent, role: 'Your concierge'),
  'ava': Persona(name: 'Ava', accent: _avaAccent, role: 'Memory'),
  'elle': Persona(name: 'Elle', accent: _elleAccent, role: 'Visual sourcing'),
  'lina': Persona(name: 'Lina', accent: _linaAccent, role: 'Commerce'),
};

/// Resolves a persona for an agent key, defaulting to Aveline for unknown keys.
Persona personaForAgent(String? agentKey) {
  if (agentKey != null && _personas.containsKey(agentKey)) {
    return _personas[agentKey]!;
  }
  return _personas['aveline']!;
}

/// Resolves a persona for a message author kind + agent key, or `null` when the
/// author is not an agent.
Persona? personaForAuthor(String authorKind, String? agentKey) {
  if (authorKind == 'Agent') {
    return personaForAgent(agentKey);
  }
  return null;
}
