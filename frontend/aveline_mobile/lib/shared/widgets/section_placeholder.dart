import 'package:flutter/material.dart';

/// A "coming soon" panel for dock tabs that depend on slices that are not built
/// yet (Customers, Catalog). Mirrors the web `SectionPlaceholder` in the quiet
/// luxury style: an overline label, a serif heading, and a soft card.
class SectionPlaceholder extends StatelessWidget {
  const SectionPlaceholder({
    super.key,
    required this.title,
    this.description,
    this.icon,
  });

  final String title;
  final String? description;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return ListView(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
      children: [
        Text(
          title.toUpperCase(),
          style: theme.textTheme.labelMedium?.copyWith(
            color: scheme.onSurfaceVariant,
            letterSpacing: 1.5,
          ),
        ),
        const SizedBox(height: 8),
        Text(title, style: theme.textTheme.headlineMedium),
        const SizedBox(height: 24),
        Container(
          padding: const EdgeInsets.all(24),
          decoration: BoxDecoration(
            color: scheme.surfaceContainerLowest,
            borderRadius: BorderRadius.circular(16),
            boxShadow: [
              BoxShadow(
                color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
                blurRadius: 20,
                offset: const Offset(0, 4),
              ),
            ],
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      title,
                      style: theme.textTheme.titleLarge?.copyWith(
                        fontFamily: 'Playfair Display',
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ),
                  if (icon != null)
                    Icon(icon, color: scheme.onSurfaceVariant, size: 24),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                description ??
                    'This section will light up once the corresponding Aveline slice is built.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 16),
              Text(
                'Nothing here yet - watch this space.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
