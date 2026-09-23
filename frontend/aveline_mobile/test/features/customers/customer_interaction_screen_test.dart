import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/customers/data/demo_customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/presentation/screens/customer_interaction_screen.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_quick_actions_bar.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  void usePhoneSurface(WidgetTester tester) {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3.0;
    addTearDown(tester.view.reset);
  }

  Widget buildScreen({
    required String customerId,
    CustomerDetail? customer,
    DemoCustomerRepository? repository,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: CustomerInteractionScreen(
            customerId: customerId,
            customer: customer,
            repository: repository ?? DemoCustomerRepository(bookDelay: Duration.zero),
          ),
        ),
      ),
    );
  }

  group('CustomerInteractionScreen', () {
    const customerId = 'CUS-1011';

    testWidgets('renders client dossier, quick actions, and interaction timeline',
        (tester) async {
      usePhoneSurface(tester);
      await tester.pumpWidget(buildScreen(customerId: customerId));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      // Client name and id
      expect(find.text('Eleanor Vane'), findsWidgets);
      expect(find.text('CUS-1011'), findsOneWidget);

      // Quick action bar buttons
      final quickBar = find.byType(CustomerQuickActionsBar);
      expect(find.descendant(of: quickBar, matching: find.text('Call')), findsOneWidget);
      expect(find.descendant(of: quickBar, matching: find.text('WhatsApp')), findsOneWidget);
      expect(find.descendant(of: quickBar, matching: find.text('Email')), findsOneWidget);
      expect(find.descendant(of: quickBar, matching: find.text('Salon AI')), findsOneWidget);

      // Timeline header & FAB
      expect(find.text('Interaction Timeline'), findsOneWidget);
      expect(find.text('Log Interaction'), findsOneWidget);
    });

    testWidgets('filters timeline when channel pill is tapped',
        (tester) async {
      usePhoneSurface(tester);
      await tester.pumpWidget(buildScreen(customerId: customerId));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      // Tap on WhatsApp filter pill
      final whatsappPill = find.widgetWithText(
        InkWell,
        'WhatsApp',
      ).first;
      await tester.tap(whatsappPill);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Interaction Timeline'), findsOneWidget);
    });

    testWidgets('opens LogInteractionSheet and submits interaction',
        (tester) async {
      usePhoneSurface(tester);
      await tester.pumpWidget(buildScreen(customerId: customerId));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      // Tap the FAB to open the log interaction bottom sheet
      await tester.tap(find.text('Log Interaction'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.text('Log Interaction'), findsWidgets);
      expect(find.text('For Eleanor Vane'), findsOneWidget);

      // Enter a discussion note
      final noteField = find.widgetWithText(TextField, 'Garments inspected, sizing feedback, color preferences, or fitting requests...');
      await tester.enterText(noteField, 'Tried Royal Blue Lehenga in fitting room.');
      await tester.pump();

      // Tap submit button in bottom sheet
      final recordButton = find.text('Record In-Person Visit');
      await tester.tap(recordButton);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      // Bottom sheet closed and new note appears in timeline
      expect(find.text('Tried Royal Blue Lehenga in fitting room.'), findsOneWidget);
    });
  });
}
