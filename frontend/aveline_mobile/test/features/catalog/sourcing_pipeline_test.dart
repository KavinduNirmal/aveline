import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_status.dart';
import 'package:aveline_mobile/features/catalog/presentation/sourcing_controller.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/archived_tickets_sheet.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/sourcing_pipeline_view.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/sourcing_stage_chips.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/sourcing_ticket_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _wrap(Widget child) {
  return MaterialApp(
    theme: AppTheme.light,
    home: Scaffold(body: child),
  );
}

void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

void main() {
  group('SourcingPipelineView', () {
    late DemoCatalogProductRepository repository;
    late SourcingController controller;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
      controller = SourcingController(repository);
    });

    tearDown(() {
      controller.dispose();
    });

    testWidgets('loads data and renders summary KPIs and ticket cards', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('ACTIVE TICKETS'), findsOneWidget);
      expect(find.text('ATELIER VOLUME'), findsOneWidget);
      expect(find.text('AVG MARGIN'), findsOneWidget);
      expect(find.byType(SourcingStageChips), findsOneWidget);
      expect(find.byType(SourcingTicketCard), findsWidgets);
    });

    testWidgets('filters tickets by search query in search field', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Mrs. Radhika Merchant'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('sourcing_search_field')), 'Radhika');
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Mrs. Radhika Merchant'), findsOneWidget);
      expect(find.text('Devraj Rajput'), findsNothing);
    });

    testWidgets('filters tickets when stage chip is tapped', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      final chipFinder = find.byKey(const Key('sourcing_stage_chip_quoted'));
      await tester.ensureVisible(chipFinder);
      await tester.pumpAndSettle();
      await tester.tap(chipFinder);
      await tester.pump(const Duration(milliseconds: 100));

      expect(controller.selectedStage, SourcingStatus.quoted);
      for (final ticket in controller.activeTickets) {
        expect(ticket.status, SourcingStatus.quoted);
      }
    });

    testWidgets('folds and unfolds card on chevron tap', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();
      final firstTicket = controller.activeTickets.first;

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('FINANCIALS & PROFITABILITY'), findsWidgets);

      await tester.tap(find.byKey(Key('sourcing_toggle_fold_${firstTicket.id}')));
      await tester.pump(const Duration(milliseconds: 100));

      expect(controller.isCollapsed(firstTicket.id), isTrue);
    });

    testWidgets('toggles fold all for active cards', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.tap(find.byKey(const Key('sourcing_fold_all_btn')));
      await tester.pump(const Duration(milliseconds: 100));

      for (final ticket in controller.activeTickets) {
        expect(controller.isCollapsed(ticket.id), isTrue);
      }

      await tester.tap(find.byKey(const Key('sourcing_fold_all_btn')));
      await tester.pump(const Duration(milliseconds: 100));

      for (final ticket in controller.activeTickets) {
        expect(controller.isCollapsed(ticket.id), isFalse);
      }
    });

    testWidgets('archives ticket and allows undoing via SnackBar action', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();
      final targetTicket = controller.activeTickets.first;
      final initialActiveCount = controller.activeTickets.length;

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.tap(find.byKey(Key('sourcing_archive_btn_${targetTicket.id}')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 200));

      expect(controller.activeTickets.length, initialActiveCount - 1);
      expect(find.text('Archived "${targetTicket.clientName}" commission'), findsOneWidget);
      expect(find.text('Undo'), findsOneWidget);

      await tester.tap(find.text('Undo'), warnIfMissed: false);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 200));

      expect(controller.activeTickets.length, initialActiveCount);
      expect(controller.activeTickets.any((t) => t.id == targetTicket.id), isTrue);
    });

    testWidgets('opens archived sheet when archived drawer button is tapped', (tester) async {
      _usePhoneSurface(tester);
      await controller.loadData();

      await tester.pumpWidget(
        _wrap(
          SourcingPipelineView(
            controller: controller,
            onCreateTicket: () {},
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.tap(find.byKey(const Key('sourcing_archived_btn')));
      await tester.pump(const Duration(milliseconds: 300));

      expect(find.byType(ArchivedTicketsSheet), findsOneWidget);
      expect(find.text('Archived Commissions'), findsOneWidget);
    });
  });
}
