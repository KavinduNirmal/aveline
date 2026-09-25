import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/create_sourcing_ticket_screen.dart';
import 'package:aveline_mobile/features/catalog/presentation/sourcing_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _wrap(Widget child) {
  return MaterialApp(
    theme: AppTheme.light,
    home: child,
  );
}

void _useTallSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 7200);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

void main() {
  group('CreateSourcingTicketScreen', () {
    late DemoCatalogProductRepository repository;
    late SourcingController controller;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
      controller = SourcingController(repository);
    });

    tearDown(() {
      controller.dispose();
    });

    testWidgets('renders all input fields and live margin gauge', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          CreateSourcingTicketScreen(
            repository: repository,
            controller: controller,
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Bespoke Sourcing Ticket'), findsOneWidget);
      expect(find.byKey(const Key('sourcing_client_input')), findsOneWidget);
      expect(find.byKey(const Key('sourcing_desc_input')), findsOneWidget);
      expect(find.byKey(const Key('sourcing_target_price_input')), findsOneWidget);
      expect(find.byKey(const Key('sourcing_cost_input')), findsOneWidget);
      expect(find.text('ESTIMATED MARGIN GAUGE'), findsOneWidget);
      expect(find.byKey(const Key('sourcing_submit_ticket_btn')), findsOneWidget);
    });

    testWidgets('updates live margin gauge calculation dynamically as user types', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          CreateSourcingTicketScreen(
            repository: repository,
            controller: controller,
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.enterText(find.byKey(const Key('sourcing_target_price_input')), '2000');
      await tester.pump(const Duration(milliseconds: 50));
      await tester.enterText(find.byKey(const Key('sourcing_cost_input')), '1000');
      await tester.pump(const Duration(milliseconds: 50));

      expect(find.text('+100.0% profit (\$1000.00)'), findsOneWidget);
    });

    testWidgets('shows validation errors when required fields are empty', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          CreateSourcingTicketScreen(
            repository: repository,
            controller: controller,
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.tap(find.byKey(const Key('sourcing_submit_ticket_btn')));
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Please enter client name'), findsOneWidget);
      expect(find.text('Please enter item description'), findsOneWidget);
    });

    testWidgets('successfully creates ticket and closes form on valid submission', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          CreateSourcingTicketScreen(
            repository: repository,
            controller: controller,
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.enterText(find.byKey(const Key('sourcing_client_input')), 'Baroness Vivienne');
      await tester.enterText(find.byKey(const Key('sourcing_desc_input')), 'Bespoke silk chiffon ballgown with hand-pleated corset.');
      await tester.enterText(find.byKey(const Key('sourcing_target_price_input')), '3500');
      await tester.enterText(find.byKey(const Key('sourcing_cost_input')), '1500');
      await tester.pump(const Duration(milliseconds: 50));

      await tester.tap(find.byKey(const Key('sourcing_submit_ticket_btn')));
      await tester.pump(const Duration(milliseconds: 200));

      expect(controller.allTickets.any((t) => t.clientName == 'Baroness Vivienne'), isTrue);
    });
  });
}
