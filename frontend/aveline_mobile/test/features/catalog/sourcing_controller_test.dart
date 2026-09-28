import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_status.dart';
import 'package:aveline_mobile/features/catalog/presentation/sourcing_controller.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SourcingController', () {
    late DemoCatalogProductRepository repository;
    late SourcingController controller;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
      controller = SourcingController(repository);
    });

    test('initializes and loads active and archived tickets with suppliers', () async {
      expect(controller.allTickets, isEmpty);
      expect(controller.isLoading, isFalse);

      await controller.loadData();

      expect(controller.allTickets, isNotEmpty);
      expect(controller.suppliers, isNotEmpty);
      expect(controller.totalActiveCount, greaterThan(0));
      expect(controller.activeTickets, isNotEmpty);
      expect(controller.archivedTickets, hasLength(1));
      expect(controller.estimatedAtelierVolume, greaterThan(0));
      expect(controller.averageMarginPercent, greaterThan(0));
    });

    test('filters active tickets by pipeline stage', () async {
      await controller.loadData();

      controller.setStageFilter(SourcingStatus.pending);
      expect(controller.selectedStage, SourcingStatus.pending);
      expect(controller.activeTickets.every((t) => t.status == SourcingStatus.pending), isTrue);

      controller.setStageFilter(SourcingStatus.quoted);
      expect(controller.selectedStage, SourcingStatus.quoted);
      expect(controller.activeTickets.every((t) => t.status == SourcingStatus.quoted), isTrue);

      controller.setStageFilter(null); // All
      expect(controller.selectedStage, isNull);
      expect(controller.activeTickets.length, controller.totalActiveCount);
    });

    test('filters tickets by text search query', () async {
      await controller.loadData();

      controller.setSearchQuery('Radhika');
      expect(controller.activeTickets, hasLength(1));
      expect(controller.activeTickets.first.clientName, 'Mrs. Radhika Merchant');

      controller.setSearchQuery('Sherwani');
      expect(controller.activeTickets, hasLength(1));
      expect(controller.activeTickets.first.category, 'Sherwanis');
    });

    test('manages card folding state', () async {
      await controller.loadData();
      final id = controller.activeTickets.first.id;

      expect(controller.isCollapsed(id), isFalse);

      controller.toggleCollapse(id);
      expect(controller.isCollapsed(id), isTrue);

      controller.toggleCollapse(id);
      expect(controller.isCollapsed(id), isFalse);

      final ids = controller.activeTickets.map((t) => t.id).toList();
      controller.setCollapsedFor(ids, true);
      expect(controller.activeTickets.every((t) => controller.isCollapsed(t.id)), isTrue);
    });

    test('updates ticket status and advances stage', () async {
      await controller.loadData();
      final ticket = controller.activeTickets.firstWhere((t) => t.status == SourcingStatus.pending);

      final success = await controller.updateTicketStatus(ticket.id, SourcingStatus.approved);
      expect(success, isTrue);

      final updated = controller.allTickets.firstWhere((t) => t.id == ticket.id);
      expect(updated.status, SourcingStatus.approved);
    });

    test('archives ticket and restores via undo', () async {
      await controller.loadData();
      final ticket = controller.activeTickets.first;
      final initialActiveCount = controller.totalActiveCount;

      final previousStatus = await controller.archiveTicket(ticket);
      expect(previousStatus, isNotNull);
      expect(controller.totalActiveCount, initialActiveCount - 1);
      expect(controller.archivedTickets.any((t) => t.id == ticket.id), isTrue);

      // Restore
      final restored = await controller.restoreTicket(ticket, targetStage: previousStatus!);
      expect(restored, isTrue);
      expect(controller.totalActiveCount, initialActiveCount);
      expect(controller.activeTickets.any((t) => t.id == ticket.id), isTrue);
    });

    test('creates new sourcing ticket and adds to active list', () async {
      await controller.loadData();
      final initialCount = controller.totalActiveCount;

      const payload = CreateSourcingRequestPayload(
        clientName: 'Princess Gayatri',
        category: 'Lehengas',
        color: 'Old Rose',
        description: 'Bespoke handloom zardozi lehenga',
        supplierId: 'sup-1',
        estimatedCost: 1200.0,
        targetPrice: 2800.0,
      );

      final created = await controller.createTicket(payload);
      expect(created, isNotNull);
      expect(created!.clientName, 'Princess Gayatri');
      expect(controller.totalActiveCount, initialCount + 1);
      expect(controller.activeTickets.first.id, created.id);
    });
  });
}
