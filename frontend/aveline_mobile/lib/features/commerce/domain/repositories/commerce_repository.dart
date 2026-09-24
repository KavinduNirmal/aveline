import '../entities/approval_entry.dart';
import '../entities/order.dart';
import '../entities/order_item.dart';
import '../entities/payment.dart';

abstract class CommerceRepository {
  Future<Order> createOrder({
    required String customerName,
    required String orderType,
    required List<OrderItem> items,
    String? customerId,
    double? discount,
    String? customerTier,
    String? notes,
  });

  Future<List<Order>> fetchOrders({
    String? status,
    int page = 1,
    int pageSize = 20,
  });

  Future<Order?> fetchOrder(String orderId);

  Future<List<ApprovalEntry>> fetchApprovals({
    String? status,
    int page = 1,
    int pageSize = 20,
  });

  Future<ApprovalEntry?> fetchApprovalForOrder(String orderId);

  Future<Payment> generatePayment({
    required String orderId,
    required double amount,
    String paymentType = 'full',
    String paymentMethod = 'online',
  });

  Future<Payment?> fetchPaymentForOrder(String orderId);

  /// Confirms a payment. `paymentMethod` is the API's own field (the counter confirmation sends
  /// `cash`); a `null` method leaves the payment's generation-time method in place.
  Future<Payment> confirmPayment(
    String paymentId, {
    String? transactionId,
    String? paymentMethod,
  });

  Future<List<int>> fetchPaymentQrBytes(String paymentLink, {int size = 300});
}
