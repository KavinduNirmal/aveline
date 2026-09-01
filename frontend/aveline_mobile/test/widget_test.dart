import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('AppTheme.light builds a MaterialApp scaffold', (tester) async {
    await tester.pumpWidget(
      MaterialApp(theme: AppTheme.light, home: const Scaffold()),
    );

    expect(find.byType(MaterialApp), findsOneWidget);
    expect(
      Theme.of(tester.element(find.byType(Scaffold))).colorScheme.surface,
      AppTheme.light.colorScheme.surface,
    );
  });
}
