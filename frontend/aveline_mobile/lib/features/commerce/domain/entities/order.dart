import 'order_item.dart';

class Order {
  const Order({
    required this.id,
    required this.organizationId,
    required this.customerId,
    required this.customerName,
    required this.orderType,
    required this.status,
    required this.subtotal,
    required this.discount,
    required this.total,
    required this.totalCost,
    required this.margin,
    this.createdBy,
    required this.createdAt,
    this.updatedAt,
    this.items = const [],
  });

  final String id;
  final String organizationId;
  final String customerId;
  final String customerName;
  final String orderType; // in_store, whatsapp, instagram, sourcing
  final String status; // pending_approval, confirmed, processing, delivered, cancelled
  final double subtotal;
  final double discount;
  final double total;
  final double totalCost;
  final double margin;
  final String? createdBy;
  final DateTime createdAt;
  final DateTime? updatedAt;
  final List<OrderItem> items;

  bool get isPendingApproval => status.toLowerCase() == 'pending_approval';
  bool get isConfirmed => status.toLowerCase() == 'confirmed';
  bool get isCancelled => status.toLowerCase() == 'cancelled';
  bool get isProcessing => status.toLowerCase() == 'processing';
  bool get isDelivered => status.toLowerCase() == 'delivered';

  /// The server's `margin` is a **ratio** in `[-1, 1]`, not a currency amount:
  /// `OrderService` computes `(total - totalCost) / total` (rounded to 4 dp).
  double get marginPercentage => margin * 100;

  /// The margin in currency: what is left of [total] after [totalCost].
  double get marginAmount => total - totalCost;
}
