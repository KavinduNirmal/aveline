import 'package:flutter/material.dart';

import '../../domain/sourcing_status.dart';
import '../sourcing_controller.dart';

/// Horizontally scrollable stage filter capsules for the sourcing pipeline.
class SourcingStageChips extends StatelessWidget {
  const SourcingStageChips({
    super.key,
    required this.controller,
  });

  final SourcingController controller;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isDark = theme.brightness == Brightness.dark;

    final stages = [null, ...SourcingStatus.values.where((s) => s != SourcingStatus.archived)];

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (final stage in stages) ...[
            _StageChip(
              stage: stage,
              isSelected: controller.selectedStage == stage,
              count: stage == null ? controller.totalActiveCount : controller.countForStage(stage),
              onTap: () => controller.setStageFilter(stage),
              scheme: scheme,
              isDark: isDark,
            ),
            const SizedBox(width: 8),
          ],
        ],
      ),
    );
  }
}

class _StageChip extends StatelessWidget {
  const _StageChip({
    required this.stage,
    required this.isSelected,
    required this.count,
    required this.onTap,
    required this.scheme,
    required this.isDark,
  });

  final SourcingStatus? stage;
  final bool isSelected;
  final int count;
  final VoidCallback onTap;
  final ColorScheme scheme;
  final bool isDark;

  @override
  Widget build(BuildContext context) {
    final label = stage == null ? 'All Stages' : stage!.shortLabel;
    final badgeColor = stage?.badgeColor ?? scheme.primary;

    return InkWell(
      key: Key('sourcing_stage_chip_${stage?.name ?? "all"}'),
      onTap: onTap,
      borderRadius: BorderRadius.circular(20),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 200),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
        decoration: BoxDecoration(
          color: isSelected
              ? scheme.primary
              : (stage != null ? stage!.badgeBackgroundColor(isDark).withValues(alpha: 0.6) : scheme.surfaceContainerHighest.withValues(alpha: 0.5)),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(
            color: isSelected ? scheme.primary : scheme.outlineVariant.withValues(alpha: 0.6),
            width: isSelected ? 1.5 : 1,
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (stage != null && !isSelected) ...[
              Container(
                width: 7,
                height: 7,
                decoration: BoxDecoration(
                  color: badgeColor,
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 6),
            ],
            Text(
              label,
              style: TextStyle(
                fontSize: 12,
                fontWeight: isSelected ? FontWeight.w600 : FontWeight.w500,
                color: isSelected ? scheme.onPrimary : scheme.onSurface,
              ),
            ),
            const SizedBox(width: 6),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
              decoration: BoxDecoration(
                color: isSelected
                    ? scheme.onPrimary.withValues(alpha: 0.2)
                    : scheme.surfaceContainerHighest,
                borderRadius: BorderRadius.circular(10),
              ),
              child: Text(
                count.toString(),
                style: TextStyle(
                  fontSize: 10,
                  fontWeight: FontWeight.bold,
                  fontFamily: 'RobotoMono',
                  color: isSelected ? scheme.onPrimary : scheme.onSurfaceVariant,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
