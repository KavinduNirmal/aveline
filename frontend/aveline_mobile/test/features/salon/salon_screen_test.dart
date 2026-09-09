import 'package:aveline_mobile/features/salon/presentation/screens/salon_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders the seeded thread and composer', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: SalonScreen()),
    );

    expect(find.text('The Salon'), findsOneWidget);
    expect(find.text('Message Aveline...'), findsOneWidget);
    // Seeded agent greeting is present.
    expect(find.textContaining('your boutique concierge'), findsOneWidget);
  });

  testWidgets('composer appends a staff note to the thread', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: SalonScreen()),
    );

    await tester.enterText(
      find.byType(TextField),
      'Please draft a note for Mrs. Perera.',
    );
    await tester.tap(find.byTooltip('Send message'));
    await tester.pump();

    expect(
      find.text('Please draft a note for Mrs. Perera.'),
      findsOneWidget,
    );
  });
}
