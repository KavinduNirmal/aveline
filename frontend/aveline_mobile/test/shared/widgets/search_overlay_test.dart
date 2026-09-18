import 'package:aveline_mobile/shared/widgets/search_overlay.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SearchOverlay', () {
    testWidgets('renders search text field and hint message', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: SearchOverlay(),
          ),
        ),
      );

      expect(find.byType(TextField), findsOneWidget);
      expect(find.text('Search Aveline...'), findsOneWidget);
      expect(find.textContaining('Search across catalog'), findsOneWidget);
    });

    testWidgets('entering text updates the search field and fires onQueryChanged', (tester) async {
      String? lastQuery;

      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SearchOverlay(
              onQueryChanged: (query) => lastQuery = query,
            ),
          ),
        ),
      );

      await tester.enterText(find.byType(TextField), 'cashmere coat');
      await tester.pump();

      expect(find.text('cashmere coat'), findsOneWidget);
      expect(lastQuery, 'cashmere coat');
    });

    testWidgets('tapping close button calls onClose callback', (tester) async {
      var closed = false;

      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SearchOverlay(
              onClose: () => closed = true,
            ),
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('search_overlay_close_button')));
      await tester.pump();

      expect(closed, isTrue);
    });
  });
}
