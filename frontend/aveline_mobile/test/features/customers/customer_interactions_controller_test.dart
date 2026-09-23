import 'package:aveline_mobile/features/customers/data/demo_customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/presentation/customer_interactions_controller.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('CustomerInteractionsController', () {
    late DemoCustomerRepository repository;
    late CustomerInteractionsController controller;
    const testCustomerId = 'CUS-1011';

    setUp(() {
      repository = DemoCustomerRepository(bookDelay: Duration.zero);
      controller = CustomerInteractionsController(
        repository,
        customerId: testCustomerId,
      );
    });

    test('initial state is unpopulated', () {
      expect(controller.detail, isNull);
      expect(controller.customer, isNull);
      expect(controller.allInteractions, isEmpty);
      expect(controller.isLoading, isFalse);
    });

    test('load populates detail and interactions', () async {
      await controller.load();

      expect(controller.detail, isNotNull);
      expect(controller.customer, isNotNull);
      expect(controller.allInteractions, isNotEmpty);
      expect(controller.errorMessage, isNull);
    });

    test('filtering by channel narrows filteredInteractions', () async {
      await controller.load();
      final totalCount = controller.allInteractions.length;

      controller.setChannel(InteractionChannel.whatsapp);
      expect(controller.channel, InteractionChannel.whatsapp);
      for (final interaction in controller.filteredInteractions) {
        expect(interaction.channel, InteractionChannel.whatsapp);
      }

      // Toggling same channel clears filter
      controller.setChannel(InteractionChannel.whatsapp);
      expect(controller.channel, isNull);
      expect(controller.filteredInteractions.length, totalCount);
    });

    test('recordInteraction prepends new interaction and updates metrics', () async {
      await controller.load();
      final initialCount = controller.allInteractions.length;
      final initialVisits = controller.customer!.visitCount;
      final initialSpent = controller.customer!.totalSpent;

      final request = RecordInteractionRequest(
        occurredAtUtc: DateTime.now().toUtc(),
        channel: InteractionChannel.inPerson,
        direction: InteractionDirection.inbound,
        note: 'Purchased bespoke clutch',
        purchaseTotal: 15000,
        tags: const ['Accessory'],
      );

      final created = await controller.recordInteraction(request);

      expect(created, isNotNull);
      expect(controller.allInteractions.length, initialCount + 1);
      expect(controller.allInteractions.first.messageContent, 'Purchased bespoke clutch');
      expect(controller.customer!.visitCount, initialVisits + 1);
      expect(controller.customer!.totalSpent, initialSpent + 15000);
    });
  });
}
