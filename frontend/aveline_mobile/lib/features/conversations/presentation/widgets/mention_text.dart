import 'package:flutter/material.dart';

import '../../domain/mentions.dart';
import 'bubble_tone.dart';

/// A message's words with its entity mentions lifted into pills (ADR-019).
///
/// `@Samantha Arias` and `#0771234567` are how staff point the resolver at an
/// exact customer instead of letting it guess from prose. Written as plain text
/// they read as stray punctuation; as a pill they read as the entity the lookup
/// used, which is what they are.
///
/// The grammar is `domain/mentions.dart`, a mirror of the resolver's own parser,
/// so a pill covers exactly the span the resolver read — the marker plus the
/// captured name or number, and not the prose the greedy capture dropped.
///
/// **Deviation from the web.** The web styles the pill with
/// `box-decoration-clone`, so a mention longer than a line gets its own rounded
/// plate on each line fragment. Flutter cannot paint a rounded, padded background
/// per line fragment, so the pill is an atomic inline box: it never breaks across
/// lines. On a phone the bubble is narrow enough that only an unusually long
/// greedy capture could hit this, and the alternative — a rounded box that cannot
/// break at all, or a square-cornered tint that can — is a worse lie about the
/// entity than a pill that wraps to the next line whole.
class MentionText extends StatelessWidget {
  const MentionText({
    super.key,
    required this.text,
    this.tone = BubbleTone.other,
    this.style,
  });

  /// The message, as the associate or an agent wrote it.
  final String text;

  /// The bubble the paragraph sits in, which the pill tints against.
  final BubbleTone tone;

  /// The paragraph's own style. The pill inherits its size and family from it.
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final base = style ?? DefaultTextStyle.of(context).style;
    final segments = splitMentions(text);

    // A body with no mention is the common case; drawing it as a plain paragraph
    // costs nothing extra and keeps the text selectable as one run.
    if (segments.length == 1 && segments.first is MentionLiteral) {
      return Text(text, style: base);
    }

    final isOwn = tone == BubbleTone.own;
    final foreground = isOwn ? scheme.onPrimary : scheme.primary;
    final background = foreground.withValues(alpha: isOwn ? 0.15 : 0.10);
    final ring = foreground.withValues(alpha: isOwn ? 0.25 : 0.20);

    return Text.rich(
      TextSpan(
        style: base,
        children: [
          for (final segment in segments)
            switch (segment) {
              MentionLiteral(text: final literal) => TextSpan(text: literal),
              MentionEntity(:final mention) => WidgetSpan(
                alignment: PlaceholderAlignment.baseline,
                baseline: TextBaseline.alphabetic,
                child: _MentionPill(
                  mention: mention,
                  base: base,
                  foreground: foreground,
                  background: background,
                  ring: ring,
                ),
              ),
            },
        ],
      ),
    );
  }
}

/// One mention, drawn as a pill in the bubble's own ink.
class _MentionPill extends StatelessWidget {
  const _MentionPill({
    required this.mention,
    required this.base,
    required this.foreground,
    required this.background,
    required this.ring,
  });

  final Mention mention;
  final TextStyle base;
  final Color foreground;
  final Color background;
  final Color ring;

  @override
  Widget build(BuildContext context) {
    return Container(
      key: ValueKey('mention_${mention.kind.name}_${mention.start}'),
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: ring),
      ),
      child: Text(
        mention.marker + mention.value,
        // A phone reads as the digits the resolver captured, set in figures that
        // line up so two mentions of different lengths still compare.
        style: base.copyWith(
          color: foreground,
          fontWeight: FontWeight.w500,
          fontFeatures: mention.kind == MentionKind.phone
              ? const [FontFeature.tabularFigures()]
              : null,
        ),
      ),
    );
  }
}
