import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/focus_task_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const _task = FocusTask(
  id: 't1',
  domain: FocusDomain.commerce,
  title: 'Review the handover note for the evening',
  detail: 'Nadia flagged two pieces that need the safe before close.',
  timeLabel: '5:45 PM',
  actionLabel: 'Sign Off',
  doneMessage: 'Handover reviewed.',
);

Widget _wrap(double prominence) => MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Center(
          child: FocusTaskCard(
            task: _task,
            onAction: () {},
            prominence: prominence,
          ),
        ),
      ),
    );

/// The card's own surface: the outermost `Container` it builds.
BoxDecoration _surface(WidgetTester tester) {
  final container = tester
      .widgetList<Container>(
        find.descendant(
          of: find.byType(FocusTaskCard),
          matching: find.byType(Container),
        ),
      )
      .first;

  return container.decoration! as BoxDecoration;
}

Color _borderColor(BoxDecoration decoration) =>
    (decoration.border! as Border).top.color;

void main() {
  final scheme = AppTheme.colorScheme;

  testWidgets('the docket in hand is the lightest surface, without a border',
      (tester) async {
    await tester.pumpWidget(_wrap(1));

    final decoration = _surface(tester);

    expect(decoration.color, scheme.surfaceContainerLowest);
    expect(_borderColor(decoration).a, 0);
  });

  testWidgets('the layer behind is tinted and outlined, so the pile is visible',
      (tester) async {
    await tester.pumpWidget(_wrap(0));

    final decoration = _surface(tester);

    // The original ghost fill was `surfaceContainerLow`, which is within a hair
    // of the page colour: the layer measured 1.00:1 against the background and
    // read as a stray arc rather than as a card.
    expect(decoration.color, scheme.surfaceContainerHigh);
    expect(decoration.color, isNot(scheme.surfaceContainerLow));

    // And it has to be visibly separated from the page it sits on.
    expect(decoration.color, isNot(scheme.surface));

    // A real border, not a 45%-alpha hairline.
    expect(_borderColor(decoration).a, closeTo(0.5, 0.01));
    expect(_borderColor(decoration).withValues(alpha: 1), scheme.outline);
  });

  testWidgets('the layer behind keeps its copy invisible', (tester) async {
    await tester.pumpWidget(_wrap(0));

    final opacity = tester.widget<Opacity>(
      find.descendant(
        of: find.byType(FocusTaskCard),
        matching: find.byType(Opacity),
      ),
    );

    expect(opacity.opacity, 0);
  });
}
