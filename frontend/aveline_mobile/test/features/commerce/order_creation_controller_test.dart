import 'package:aveline_mobile/features/commerce/domain/entities/order.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order_item.dart';
import 'package:aveline_mobile/features/commerce/domain/repositories/commerce_repository.dart';
import 'package:aveline_mobile/features/commerce/presentation/controllers/order_creation_controller.dart';
import 'package:flutter_test/flutter_test.dart';

class MockCommerceRepository implements CommerceRepository {
  Order? createdOrder;
  Map<String, dynamic>? lastCallParams;

  @override
  Future<Order> createOrder({
    required String customerName,
    required String orderType,
    required List<OrderItem> items,
    String? customerId,
    double? discount,
    String? customerTier,
    String? notes,
  }) async {
    lastCallParams = {
      'customerName': customerName,
      'orderType': orderType,
      'items': items,
      'customerId': customerId,
      'discount': discount,
      'customerTier': customerTier,
      'notes': notes,
    };

    final subtotal = items.fold(0.0, (sum, i) => sum + i.totalPrice);
    final disc = discount ?? 0.0;
    final total = subtotal - disc;
    final totalCost = items.fold(0.0, (sum, i) => sum + (i.wholesaleCost * i.quantity));

    final order = Order(
      id: 'ord-created-1',
      organizationId: 'org-1',
      customerId: customerId ?? 'cus-default',
      customerName: customerName,
      orderType: orderType,
      status: disc > 5000 ? 'pending_approval' : 'confirmed',
      subtotal: subtotal,
      discount: disc,
      total: total,
      totalCost: totalCost,
      margin: total - totalCost,
      createdAt: DateTime.now(),
      items: items,
    );
    createdOrder = order;
    return order;
  }

  @override
  noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

void main() {
  group('OrderCreationController', () {
    late MockCommerceRepository repository;
    late OrderCreationController controller;

    setUp(() {
      repository = MockCommerceRepository();
      controller = OrderCreationController(repository: repository);
    });

    test('initial state is clean and in_store by default', () {
      expect(controller.orderType, 'in_store');
      expect(controller.items, isEmpty);
      expect(controller.subtotal, 0.0);
      expect(controller.total, 0.0);
      expect(controller.canSubmit, false);
    });

    test('adding and updating items computes subtotal and margin accurately', () {
      controller.setCustomerName('Chamari Atapattu');

      controller.addItem(
        const OrderItem(
          itemId: 'item-1',
          itemName: 'Chanderi Saree',
          quantity: 1,
          unitPrice: 30000.0,
          wholesaleCost: 18000.0,
        ),
      );

      expect(controller.items.length, 1);
      expect(controller.subtotal, 30000.0);
      expect(controller.totalCost, 18000.0);
      expect(controller.total, 30000.0);
      expect(controller.margin, 12000.0);
      expect(controller.marginPercent, 40.0);
      expect(controller.canSubmit, true);

      // Adding the same item increments quantity
      controller.addItem(
        const OrderItem(
          itemId: 'item-1',
          itemName: 'Chanderi Saree',
          quantity: 1,
          unitPrice: 30000.0,
          wholesaleCost: 18000.0,
        ),
      );
      expect(controller.items.length, 1);
      expect(controller.items.first.quantity, 2);
      expect(controller.subtotal, 60000.0);

      // Decrementing quantity
      controller.updateQuantity('item-1', 1);
      expect(controller.items.first.quantity, 1);
      expect(controller.subtotal, 30000.0);
    });

    test('high discount triggers threshold warning', () {
      controller.setCustomerName('Chamari Atapattu');
      controller.addItem(
        const OrderItem(
          itemId: 'item-1',
          itemName: 'Chanderi Saree',
          quantity: 1,
          unitPrice: 30000.0,
          wholesaleCost: 18000.0,
        ),
      );

      expect(controller.triggersApprovalWarning, false);

      // Apply 30% discount
      controller.setDiscount(9000.0);
      expect(controller.total, 21000.0);
      expect(controller.triggersApprovalWarning, true);
    });

    test('submitOrder creates order and resets or holds result', () async {
      controller.setOrderType('whatsapp');
      controller.setCustomerName('Niluka Fernando');
      controller.addItem(
        const OrderItem(
          itemId: 'item-2',
          itemName: 'Silk Dupatta',
          quantity: 2,
          unitPrice: 15000.0,
          wholesaleCost: 8000.0,
        ),
      );
      controller.setDiscount(6000.0);

      final order = await controller.submitOrder();

      expect(order.customerName, 'Niluka Fernando');
      expect(order.orderType, 'whatsapp');
      expect(order.status, 'pending_approval');
      expect(repository.lastCallParams?['orderType'], 'whatsapp');
      expect(controller.isSubmitting, false);
    });
  });
}
