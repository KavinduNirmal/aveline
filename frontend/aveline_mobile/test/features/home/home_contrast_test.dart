import 'dart:math' as math;

import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/client_highlight_tile.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/focus_task_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// WCAG relative luminance.
double _luminance(Color color) {
  double linear(double channel) => channel <= 0.03928
      ? channel / 12.92
      : math.pow((channel + 0.055) / 1.055, 2.4).toDouble();

  return 0.2126 * linear(color.r) +
      0.7152 * linear(color.g) +
      0.0722 * linear(color.b);
}

/// WCAG 2.2 contrast ratio. SC 1.4.3 asks for 4.5:1 at these text sizes.
double _contrast(Color a, Color b) {
  final la = _luminance(a);
  final lb = _luminance(b);
  return (math.max(la, lb) + 0.05) / (math.min(la, lb) + 0.05);
}

void main() {
  final scheme = AppTheme.colorScheme;

  group('tier badge contrast', () {
    test('every tier clears 4.5:1 against the numeral it carries', () {
      for (final tier in ClientTier.values) {
        final ratio = _contrast(clientTierColor(tier, scheme), scheme.onPrimary);
        expect(
          ratio,
          greaterThanOrEqualTo(4.5),
          reason: '$tier measured ${ratio.toStringAsFixed(2)}:1',
        );
      }
    });

    test('the ramp still runs dark to light from level 3 down to level 1', () {
      double luminanceOf(ClientTier tier) =>
          _luminance(clientTierColor(tier, scheme));

      expect(
        luminanceOf(ClientTier.level3),
        lessThan(luminanceOf(ClientTier.level2)),
      );
      expect(
        luminanceOf(ClientTier.level2),
        lessThan(luminanceOf(ClientTier.level1)),
      );
    });
  });

  testWidgets('the wardrobe chip clears 4.5:1 on its own fill', (tester) async {
    const task = FocusTask(
      id: 'w1',
      domain: FocusDomain.wardrobe,
      title: 'Steam the ivory silk',
      detail: 'It hangs in the back room for tomorrow.',
      timeLabel: '9:00 AM',
      actionLabel: 'Sign Off',
      doneMessage: 'Wardrobe task cleared.',
    );

    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          body: FocusTaskCard(
            task: task,
            onAction: () {},
          ),
        ),
      ),
    );

    final label = tester.widget<Text>(find.text('WARDROBE'));
    // The chip's own fill: the nearest decorated ancestor of the label.
    final fill = tester
        .widgetList<Container>(
          find.ancestor(
            of: find.text('WARDROBE'),
            matching: find.byType(Container),
          ),
        )
        .map((container) => container.decoration)
        .whereType<BoxDecoration>()
        .map((decoration) => decoration.color)
        .whereType<Color>()
        .first;

    final ratio = _contrast(label.style!.color!, fill);
    expect(
      ratio,
      greaterThanOrEqualTo(4.5),
      reason: 'wardrobe chip measured ${ratio.toStringAsFixed(2)}:1',
    );
  });
}
