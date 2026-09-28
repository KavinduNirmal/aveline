import 'package:aveline_mobile/core/notifications/notification_provider.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/approval_entry.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order_item.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/payment.dart';
import 'package:aveline_mobile/features/commerce/domain/repositories/commerce_repository.dart';
import 'package:aveline_mobile/features/commerce/presentation/screens/order_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

class FakeCommerceRepoForDetail implements CommerceRepository {
  Order? order;
  ApprovalEntry? approval;
  Payment? payment;

  @override
  Future<Order?> fetchOrder(String orderId) async => order;

  @override
  Future<ApprovalEntry?> fetchApprovalForOrder(String orderId) async => approval;

  @override
  Future<Payment?> fetchPaymentForOrder(String orderId) async => payment;

  @override
  Future<Payment> generatePayment({
    required String orderId,
    required double amount,
    String paymentType = 'full',
    String paymentMethod = 'online',
  }) async {
    final pay = Payment(
      id: 'pay-123',
      organizationId: 'org-1',
      orderId: orderId,
      amount: amount,
      paymentType: paymentType,
      paymentMethod: paymentMethod,
      status: 'pending',
      paymentLink: 'https://pay.aveline.boutique/checkout/sample123',
      createdAt: DateTime.now(),
    );
    payment = pay;
    return pay;
  }

  @override
  Future<List<int>> fetchPaymentQrBytes(String paymentLink, {int size = 300}) async {
    return const [
      137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1,
      0, 0, 0, 1, 8, 6, 0, 0, 0, 31, 21, 196, 137, 0, 0, 0, 10, 73, 68, 65, 84,
      120, 156, 99, 96, 0, 0, 0, 2, 0, 1, 226, 33, 188, 51, 0, 0, 0, 0, 73, 69,
      78, 68, 174, 66, 96, 130
    ];
  }

  @override
  noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

void main() {
  testWidgets('OrderDetailScreen renders pending approval and payment actions', (tester) async {
    final repo = FakeCommerceRepoForDetail();
    repo.order = Order(
      id: 'ord-99999999',
      organizationId: 'org-1',
      customerId: 'cus-1',
      customerName: 'Hansika Rodrigo',
      orderType: 'whatsapp',
      status: 'confirmed',
      subtotal: 50000.0,
      discount: 5000.0,
      total: 45000.0,
      totalCost: 20000.0,
      margin: 25000.0,
      createdAt: DateTime.now(),
      items: const [
        OrderItem(
          itemId: 'it-1',
          itemName: 'Georgette Anarkali',
          quantity: 1,
          unitPrice: 50000.0,
          wholesaleCost: 20000.0,
        ),
      ],
    );

    final notificationProvider = NotificationProvider();

    await tester.pumpWidget(
      MultiProvider(
        providers: [
          ChangeNotifierProvider<NotificationProvider>.value(value: notificationProvider),
        ],
        child: MaterialApp(
          home: OrderDetailScreen(
            orderId: 'ord-99999999',
            repository: repo,
          ),
        ),
      ),
    );

    await tester.pumpAndSettle();

    expect(find.text('Hansika Rodrigo'), findsOneWidget);
    expect(find.text('WhatsApp'), findsOneWidget);
    expect(find.text('Georgette Anarkali'), findsOneWidget);
    expect(find.text('LKR 45000'), findsWidgets);

    // Verify Payment action button is visible
    final paymentBtn = find.text('Customer Payment QR & Link');
    expect(paymentBtn, findsOneWidget);

    // Tap to open payment modal
    await tester.tap(paymentBtn);
    await tester.pumpAndSettle();

    // Verify QR modal opens with payment details
    expect(find.text('Customer Payment QR'), findsOneWidget);
    expect(find.text('Copy Link'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'WhatsApp'), findsOneWidget);
    expect(find.text('Mark Paid (Cash / Card Terminal)'), findsOneWidget);
  });
}
