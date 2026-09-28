class Payment {
  const Payment({
    required this.id,
    required this.organizationId,
    required this.orderId,
    required this.amount,
    required this.paymentType,
    required this.paymentMethod,
    required this.status,
    this.gatewayTransactionId,
    this.paymentLink,
    required this.createdAt,
    this.confirmedAt,
    this.expiresAt,
  });

  final String id;
  final String organizationId;
  final String orderId;
  final double amount;
  final String paymentType; // full, deposit, balance
  final String paymentMethod; // online, card, cash, bank_transfer
  final String status; // pending, confirmed, failed, refunded
  final String? gatewayTransactionId;
  final String? paymentLink;
  final DateTime createdAt;
  final DateTime? confirmedAt;
  final DateTime? expiresAt;

  bool get isPending => status.toLowerCase() == 'pending';
  bool get isConfirmed => status.toLowerCase() == 'confirmed';
}
