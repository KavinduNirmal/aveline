import 'package:flutter/material.dart';

import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/filter_pill.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../domain/catalog_filters.dart';

/// The catalog's search filter options.
///
/// Opened from the field row on the Catalog screen and returns the chosen
/// [CatalogFilters] when "Show results" is pressed, so the catalog owns the
/// applied value and this screen only ever edits a draft.
class CatalogFilterScreen extends StatefulWidget {
  const CatalogFilterScreen({super.key, this.initial});

  /// The filters already applied, so reopening the screen shows them chosen.
  final CatalogFilters? initial;

  @override
  State<CatalogFilterScreen> createState() => _CatalogFilterScreenState();
}

class _CatalogFilterScreenState extends State<CatalogFilterScreen> {
  late CatalogFilters _draft = widget.initial ?? const CatalogFilters.none();

  void _toggle(CatalogFilterGroup group, String value) {
    setState(() => _draft = _draft.toggle(group, value));
  }

  void _reset() {
    setState(() => _draft = const CatalogFilters.none());
  }

  void _apply() {
    Navigator.of(context).pop(_draft);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          key: const Key('catalog_filter_back'),
          icon: const Icon(Icons.arrow_back_rounded),
          color: scheme.onSurfaceVariant,
          tooltip: 'Back',
          onPressed: () => Navigator.of(context).maybePop(),
        ),
        actions: [
          TextButton(
            key: const Key('catalog_filter_reset'),
            onPressed: _draft.isEmpty ? null : _reset,
            child: const Text('Reset'),
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: Stack(
        children: [
          const Positioned.fill(child: BrandBackdrop()),
          ListView(
            // Bottom padding clears the pinned action below.
            padding: const EdgeInsets.fromLTRB(20, 24, 20, 120),
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              const SectionOverline('Catalog'),
              const SizedBox(height: 8),
              Text('Filter options', style: theme.textTheme.headlineMedium),
              const SizedBox(height: 8),
              Text(
                'Narrow the catalog by availability, category, fabric, size, '
                'or price.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 28),
              for (final group in CatalogFilterGroup.values) ...[
                _FilterGroupSection(
                  group: group,
                  draft: _draft,
                  onToggled: (value) => _toggle(group, value),
                ),
                const SizedBox(height: 28),
              ],
            ],
          ),
        ],
      ),
      // Pinned rather than scrolled to, so applying never needs a hunt.
      bottomNavigationBar: DecoratedBox(
        decoration: BoxDecoration(
          color: scheme.surface,
          border: Border(
            top: BorderSide(
              color: scheme.outlineVariant.withValues(alpha: 0.5),
            ),
          ),
        ),
        child: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 12),
            child: SizedBox(
              width: double.infinity,
              child: FilledButton(
                key: const Key('catalog_filter_apply'),
                onPressed: _apply,
                child: Text(
                  _draft.isEmpty
                      ? 'Show all pieces'
                      : 'Show results (${_draft.activeCount})',
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// One labelled group of options.
class _FilterGroupSection extends StatelessWidget {
  const _FilterGroupSection({
    required this.group,
    required this.draft,
    required this.onToggled,
  });

  final CatalogFilterGroup group;
  final CatalogFilters draft;
  final ValueChanged<String> onToggled;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionOverline(group.label),
        const SizedBox(height: 12),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final option in group.options)
              FilterPill(
                label: option,
                selected: draft.isSelected(group, option),
                onTap: () => onToggled(option),
              ),
          ],
        ),
      ],
    );
  }
}
