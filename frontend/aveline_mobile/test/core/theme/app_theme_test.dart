import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Whether [family] belongs to one of the two faces the brand pairs.
///
/// `google_fonts` appends the weight variant to the family name, so the styles
/// read `DMSans_600` / `PlayfairDisplay_500` rather than `DM Sans`. A slot that
/// fell through to Material's defaults would come back as plain `Roboto`.
bool _isBrandFace(String? family) =>
    family != null &&
    (family.startsWith('DMSans') || family.startsWith('PlayfairDisplay'));

void main() {
  group('AppTheme type scale', () {
    testWidgets('defines every slot, so none falls back to Roboto',
        (tester) async {
      late TextTheme theme;
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Builder(
            builder: (context) {
              theme = Theme.of(context).textTheme;
              return const Scaffold();
            },
          ),
        ),
      );

      final slots = <String, TextStyle?>{
        'displayLarge': theme.displayLarge,
        'displayMedium': theme.displayMedium,
        'displaySmall': theme.displaySmall,
        'headlineLarge': theme.headlineLarge,
        'headlineMedium': theme.headlineMedium,
        'headlineSmall': theme.headlineSmall,
        'titleLarge': theme.titleLarge,
        'titleMedium': theme.titleMedium,
        'titleSmall': theme.titleSmall,
        'bodyLarge': theme.bodyLarge,
        'bodyMedium': theme.bodyMedium,
        'bodySmall': theme.bodySmall,
        'labelLarge': theme.labelLarge,
        'labelMedium': theme.labelMedium,
        'labelSmall': theme.labelSmall,
      };

      for (final entry in slots.entries) {
        final style = entry.value;
        if (style == null) {
          fail('${entry.key} is undefined in AppTheme.textTheme');
        }
        // `ThemeData` merges this theme over `Typography.material2021`, whose
        // styles name Roboto explicitly. A slot left null therefore renders its
        // widget in Roboto, which is how the filled-button label ("Sign Off")
        // silently left the brand face.
        expect(
          _isBrandFace(style.fontFamily),
          isTrue,
          reason: '${entry.key} resolved to ${style.fontFamily}, not a brand face',
        );
      }
    });

    testWidgets('gives a filled button a brand label', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
            body: FilledButton(onPressed: () {}, child: const Text('Sign Off')),
          ),
        ),
      );

      // `ButtonStyleButton` resolves its label from `textTheme.labelLarge` and
      // publishes the result as the ambient `DefaultTextStyle`.
      final style =
          DefaultTextStyle.of(tester.element(find.text('Sign Off'))).style;

      expect(
        _isBrandFace(style.fontFamily),
        isTrue,
        reason: 'the button label resolved to ${style.fontFamily}',
      );
      expect(style.fontFamily, isNot('Roboto'));
      expect(style.fontSize, AppTheme.textTheme.labelLarge?.fontSize);
    });
  });
}
