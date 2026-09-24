import 'package:aveline_mobile/features/commerce/data/models/approval_dto.dart';
import 'package:aveline_mobile/features/commerce/data/models/order_dto.dart';
import 'package:aveline_mobile/features/commerce/data/models/payment_dto.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order_item.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Commerce DTOs', () {
    test('OrderItemDto serialization and domain mapping', () {
      const item = OrderItem(
        itemId: 'item-1',
        itemName: 'Kanchipuram Silk',
        quantity: 2,
        unitPrice: 50000.0,
        wholesaleCost: 30000.0,
      );

      expect(item.totalPrice, 100000.0);
      expect(item.margin, 40000.0);
      expect(item.marginPercent, 40.0);

      final dto = OrderItemDto.fromDomain(item);
      final json = dto.toJson();
      expect(json['itemId'], 'item-1');
      expect(json['quantity'], 2);
      expect(json['unitPrice'], 50000.0);

      final parsed = OrderItemDto.fromJson(json).toDomain();
      expect(parsed.itemId, 'item-1');
      expect(parsed.totalPrice, 100000.0);
    });

    test('CreateOrderDto to JSON', () {
      final dto = CreateOrderDto(
        customerId: 'cus-1',
        customerName: 'Ananya Sharma',
        orderType: 'whatsapp',
        items: const [
          OrderItemDto(
            itemId: 'item-1',
            itemName: 'Linen Saree',
            quantity: 1,
            unitPrice: 25000.0,
            wholesaleCost: 15000.0,
            totalPrice: 25000.0,
          ),
        ],
        discount: 2500.0,
        notes: 'Requested express fitting',
      );

      final json = dto.toJson();
      expect(json['customerName'], 'Ananya Sharma');
      expect(json['orderType'], 'whatsapp');
      expect((json['items'] as List).length, 1);
      expect(json['discount'], 2500.0);
      expect(json['notes'], 'Requested express fitting');
    });

    test('OrderResponseDto fromJson and domain mapping', () {
      final json = {
        'id': 'ord-101',
        'organizationId': 'org-1',
        'customerId': 'cus-1',
        'customerName': 'Dilshan Perera',
        'orderType': 'in_store',
        'status': 'pending_approval',
        'subtotal': 60000.0,
        'discount': 10000.0,
        'total': 50000.0,
        'totalCost': 30000.0,
        'margin': 20000.0,
        'createdAt': '2026-09-23T10:00:00Z',
        'items': [
          {
            'id': 'line-1',
            'itemId': 'it-1',
            'itemName': 'Raw Silk Kurta',
            'quantity': 2,
            'unitPrice': 30000.0,
            'wholesaleCost': 15000.0,
            'totalPrice': 60000.0,
          }
        ]
      };

      final order = OrderResponseDto.fromJson(json).toDomain();
      expect(order.id, 'ord-101');
      expect(order.isPendingApproval, true);
      expect(order.items.length, 1);
      expect(order.marginPercentage, 40.0);
    });

    test('ApprovalQueueResponseDto fromJson and domain mapping', () {
      final json = {
        'id': 'app-1',
        'organizationId': 'org-1',
        'orderId': 'ord-101',
        'approvalType': 'discount_limit',
        'status': 'pending',
        'thresholdExceeded': true,
        'reason': 'Requested discount exceeds 15% margin threshold',
        'createdAt': '2026-09-23T10:05:00Z',
      };

      final approval = ApprovalQueueResponseDto.fromJson(json).toDomain();
      expect(approval.id, 'app-1');
      expect(approval.isPending, true);
      expect(approval.thresholdExceeded, true);
      expect(approval.reason, contains('margin threshold'));
    });

    test('PaymentResponseDto fromJson and domain mapping', () {
      final json = {
        'id': 'pay-1',
        'organizationId': 'org-1',
        'orderId': 'ord-101',
        'amount': 50000.0,
        'paymentType': 'full',
        'paymentMethod': 'online',
        'status': 'pending',
        'paymentLink': 'https://pay.aveline.boutique/checkout/ab12cd34',
        'createdAt': '2026-09-23T10:10:00Z',
      };

      final payment = PaymentResponseDto.fromJson(json).toDomain();
      expect(payment.id, 'pay-1');
      expect(payment.isPending, true);
      expect(payment.paymentLink, contains('pay.aveline.boutique'));
    });
  });
}
