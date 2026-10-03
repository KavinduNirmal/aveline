import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/shared/widgets/section_placeholder.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// A golden on a **static** screen, on purpose.
///
/// `SectionPlaceholder` is the empty state a dock tab shows before its slice is
/// built. It carries no clock, no greeting, no network data and no animation, so
/// its pixels are the same on every run and on every machine. The alternative
/// candidate, `HomeScreen`, greets by time of day and would drift.
///
/// Fonts: the theme asks Google Fonts for Playfair Display and Hanken Grotesk.
/// `flutter test` cannot fetch them, and the device-file-system cache is skipped
/// under the test binding, so every golden renders in the test binding's own
/// default font. That is exactly what makes this image stable between this
/// machine and CI, and it is why the golden must be regenerated with
/// `flutter test --update-goldens` on the same Flutter channel CI uses.
void main() {
  testWidgets('SectionPlaceholder empty state renders pixel-for-pixel', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        debugShowCheckedModeBanner: false,
        home: const Center(
          child: RepaintBoundary(
            // A fixed-size boundary: the captured image is 390x420 logical
            // pixels whatever the test surface or device pixel ratio is.
            key: Key('golden'),
            child: SizedBox(
              width: 390,
              height: 420,
              child: Material(
                color: Color(0xFFFFF8F7),
                child: SectionPlaceholder(
                  title: 'Conversations',
                  description: 'Client threads and the Salon arrive here next.',
                  icon: Icons.forum_outlined,
                ),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pump();

    await expectLater(
      find.byKey(const Key('golden')),
      matchesGoldenFile('goldens/section_placeholder.png'),
    );
  });
}
