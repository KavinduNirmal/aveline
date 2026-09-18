import 'package:flutter/material.dart';

import '../../../../shared/widgets/avatar_tints.dart';
import '../../../../shared/widgets/blossom.dart';

/// The circle a conversation wears.
///
/// A client is their initials on their own paper tint, the same tint the client
/// book gives them, so a face does not change shade between the two screens.
/// Aveline is the brand's blossom instead, because she is not a person and a set
/// of initials would read as one more client.
class ConversationAvatar extends StatelessWidget {
  const ConversationAvatar.client({
    super.key,
    required this.name,
    this.size = 54,
  }) : _isAveline = false;

  const ConversationAvatar.aveline({super.key, this.size = 54})
    : name = null,
      _isAveline = true;

  /// The client's name, or `null` for Aveline.
  final String? name;

  final double size;
  final bool _isAveline;

  @override
  Widget build(BuildContext context) {
    return _isAveline ? _blossom(context) : _initials(context);
  }

  Widget _blossom(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.10),
        shape: BoxShape.circle,
        border: Border.all(color: scheme.primary.withValues(alpha: 0.35)),
      ),
      child: Center(
        child: Blossom(size: size * 0.48, color: scheme.primary),
      ),
    );
  }

  Widget _initials(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final displayName = name ?? '';

    return Semantics(
      // The circle is an identity, not a name; the row prints the name beside it,
      // and the initials should not be read out as a word.
      label: 'Profile',
      excludeSemantics: true,
      child: Container(
        width: size,
        height: size,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: avatarTintFor(displayName),
          shape: BoxShape.circle,
          border: Border.all(
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
        ),
        child: Text(
          initialsForName(displayName),
          style: theme.textTheme.titleSmall?.copyWith(
            color: const Color(0xFF3B3030),
            fontWeight: FontWeight.w600,
            letterSpacing: 0.2,
          ),
        ),
      ),
    );
  }
}

/// The initials a client's circle wears: `Nadeesha Perera` is `NP`.
///
/// One name is one letter rather than a letter and a gap, and a name the API has
/// not sent yet is a question mark rather than an empty circle.
String initialsForName(String name) {
  final parts = name
      .trim()
      .split(RegExp(r'\s+'))
      .where((part) => part.isNotEmpty)
      .toList();

  if (parts.isEmpty) {
    return '?';
  }
  if (parts.length == 1) {
    return parts.first.substring(0, 1).toUpperCase();
  }
  return (parts.first.substring(0, 1) + parts.last.substring(0, 1))
      .toUpperCase();
}
