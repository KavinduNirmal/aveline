import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/shared/widgets/brand_section_title.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The widget test font draws every glyph as a one-em square, so it is far wider
/// than Playfair Display at the same size. These tests therefore choose the
/// surface width to put the label clearly on one side of the line or the other,
/// rather than relying on the real typeface's metrics: what is under test is
/// whether the fade is applied only when the name does not fit.
Future<void> _pumpTitle(
  WidgetTester tester, {
  required String boutiqueName,
  required double width,
  String section = 'Customers',
}) async {
  tester.view.physicalSize = Size(width, 200);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(tester.view.reset);

  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: Scaffold(
            body: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: BrandSectionTitle(
                boutiqueName: boutiqueName,
                section: section,
                titleKey: const Key('title'),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

/// The `ShaderMask` that carries the trailing fade, if there is one.
Finder get _fade => find.ancestor(
  of: find.byKey(const Key('title')),
  matching: find.byType(ShaderMask),
);

void main() {
  group('BrandSectionTitle', () {
    testWidgets('prints the boutique and the section', (tester) async {
      await _pumpTitle(tester, boutiqueName: 'Ceylon Atelier', width: 1200);

      final title = tester.widget<Text>(find.byKey(const Key('title')));
      expect(title.data, 'Ceylon Atelier - Customers');
      expect(title.maxLines, 1);
      expect(title.softWrap, isFalse);
      expect(title.style?.fontSize, AppTheme.textTheme.displayMedium?.fontSize);
    });

    testWidgets('leaves a name that fits the line fully legible', (
      tester,
    ) async {
      await _pumpTitle(tester, boutiqueName: 'Ceylon Atelier', width: 1200);

      // The band covers a fixed share of the line, so masking a name that fits
      // would dim its last letters. `{boutique} - Customers` reaches further
      // along the line than `{boutique} - Catalog`, which is how the regression
      // showed up: the shop's own name read as a ghost of itself.
      expect(_fade, findsNothing);
    });

    testWidgets('fades a name that does not fit the line', (tester) async {
      await _pumpTitle(
        tester,
        boutiqueName: 'The Colombo Heritage Atelier and Silk House',
        width: 300,
      );

      expect(_fade, findsOneWidget);
    });
  });
}
