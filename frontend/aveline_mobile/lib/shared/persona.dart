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

/// Commerce's wine-rose (same as primary).
const Color _linaAccent = Color(0xFF8B2E42);

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
