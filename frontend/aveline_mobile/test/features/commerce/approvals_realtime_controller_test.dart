import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/notification_provider.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/approval_entry.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/payment.dart';
import 'package:aveline_mobile/features/commerce/domain/repositories/commerce_repository.dart';
import 'package:aveline_mobile/features/commerce/presentation/controllers/approvals_realtime_controller.dart';
import 'package:flutter_test/flutter_test.dart';

class MockCommerceRepoForApprovals implements CommerceRepository {
  Order? order;
  ApprovalEntry? approval;
  Payment? payment;

  int fetchOrderCalls = 0;
  int fetchApprovalCalls = 0;

  @override
  Future<Order?> fetchOrder(String orderId) async {
    fetchOrderCalls++;
    return order;
  }

  @override
  Future<ApprovalEntry?> fetchApprovalForOrder(String orderId) async {
    fetchApprovalCalls++;
    return approval;
  }

  @override
  Future<Payment> generatePayment({
    required String orderId,
    required double amount,
    String paymentType = 'full',
    String paymentMethod = 'online',
  }) async {
    final pay = Payment(
      id: 'pay-1',
      organizationId: 'org-1',
      orderId: orderId,
      amount: amount,
      paymentType: paymentType,
      paymentMethod: paymentMethod,
      status: 'pending',
      paymentLink: 'https://pay.aveline.boutique/checkout/abc',
      createdAt: DateTime.now(),
    );
    payment = pay;
    return pay;
  }

  @override
  Future<Payment?> fetchPaymentForOrder(String orderId) async {
    return payment;
  }

  @override
  Future<Payment> confirmPayment(
    String paymentId, {
    String? transactionId,
    String? paymentMethod,
  }) async {
    final confirmed = Payment(
      id: paymentId,
      organizationId: 'org-1',
      orderId: order?.id ?? 'ord-1',
      amount: payment?.amount ?? 1000.0,
      paymentType: 'full',
      paymentMethod: 'online',
      status: 'confirmed',
      paymentLink: payment?.paymentLink,
      createdAt: DateTime.now(),
      confirmedAt: DateTime.now(),
    );
    payment = confirmed;
    return confirmed;
  }

  @override
  Future<List<int>> fetchPaymentQrBytes(String paymentLink, {int size = 300}) async {
    return [1, 2, 3, 4];
  }

  @override
  noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

void main() {
  group('ApprovalsRealtimeController', () {
    late MockCommerceRepoForApprovals repository;
    late NotificationProvider notificationProvider;
    late ApprovalsRealtimeController controller;

    setUp(() {
      repository = MockCommerceRepoForApprovals();
      notificationProvider = NotificationProvider();

      repository.order = Order(
        id: 'ord-1',
        organizationId: 'org-1',
        customerId: 'cus-1',
        customerName: 'Samadhi Wickramasinghe',
        orderType: 'in_store',
        status: 'pending_approval',
        subtotal: 50000.0,
        discount: 10000.0,
        total: 40000.0,
        totalCost: 20000.0,
        margin: 20000.0,
        createdAt: DateTime.now(),
      );

      repository.approval = ApprovalEntry(
        id: 'app-1',
        organizationId: 'org-1',
        orderId: 'ord-1',
        approvalType: 'discount',
        status: 'pending',
        thresholdExceeded: true,
        reason: '20% discount requires owner sign-off',
        createdAt: DateTime.now(),
      );

      controller = ApprovalsRealtimeController(
        repository: repository,
        notificationProvider: notificationProvider,
      );
    });

    tearDown(() {
      controller.dispose();
      notificationProvider.dispose();
    });

    test('load fetches order and approval status', () async {
      await controller.load('ord-1');

      expect(controller.order?.id, 'ord-1');
      expect(controller.approval?.id, 'app-1');
      expect(controller.approval?.isPending, true);
      expect(repository.fetchOrderCalls, 1);
      expect(repository.fetchApprovalCalls, 1);
    });

    test('notification arrival triggers automatic refresh', () async {
      await controller.load('ord-1');

      // Update mock data to simulate owner approving the order on web dashboard
      repository.order = Order(
        id: 'ord-1',
        organizationId: 'org-1',
        customerId: 'cus-1',
        customerName: 'Samadhi Wickramasinghe',
        orderType: 'in_store',
        status: 'confirmed',
        subtotal: 50000.0,
        discount: 10000.0,
        total: 40000.0,
        totalCost: 20000.0,
        margin: 20000.0,
        createdAt: DateTime.now(),
      );

      repository.approval = ApprovalEntry(
        id: 'app-1',
        organizationId: 'org-1',
        orderId: 'ord-1',
        approvalType: 'discount',
        status: 'approved',
        thresholdExceeded: true,
        reason: '20% discount requires owner sign-off',
        decisionComment: 'Approved for VIP customer',
        createdAt: DateTime.now(),
        decidedAt: DateTime.now(),
      );

      // Push notification from SignalR into NotificationProvider
      notificationProvider.push(
        NotificationPayload(
          notificationId: 'n-1',
          type: 'approval_decision',
          title: 'Order Approved',
          body: 'Discount of 10,000 LKR approved by owner.',
        ),
      );

      // Wait microtask for controller listener
      await Future<void>.delayed(const Duration(milliseconds: 20));

      expect(controller.order?.status, 'confirmed');
      expect(controller.approval?.status, 'approved');
      expect(repository.fetchOrderCalls, greaterThan(1));
    });

    test('generatePayment creates payment and fetches QR bytes', () async {
      await controller.load('ord-1');

      final payment = await controller.generatePayment();
      expect(payment.id, 'pay-1');
      expect(payment.paymentLink, isNotNull);

      final qrBytes = await controller.getPaymentQrBytes(payment.paymentLink!);
      expect(qrBytes, [1, 2, 3, 4]);
    });
  });
}
