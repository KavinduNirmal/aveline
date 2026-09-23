import '../../domain/entities/payment.dart';

class GeneratePaymentRequestDto {
  const GeneratePaymentRequestDto({
    required this.orderId,
    required this.amount,
    this.paymentType = 'full',
    this.paymentMethod = 'online',
  });

  final String orderId;
  final double amount;
  final String paymentType;
  final String paymentMethod;

  Map<String, dynamic> toJson() {
    return {
      'orderId': orderId,
      'amount': amount,
      'paymentType': paymentType,
      'paymentMethod': paymentMethod,
    };
  }
}

class ConfirmPaymentDto {
  const ConfirmPaymentDto({
    this.gatewayTransactionId,
    this.notes,
  });

  final String? gatewayTransactionId;
  final String? notes;

  Map<String, dynamic> toJson() {
    return {
      if (gatewayTransactionId != null) 'gatewayTransactionId': gatewayTransactionId,
      if (notes != null) 'notes': notes,
    };
  }
}

class PaymentResponseDto {
  const PaymentResponseDto({
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

  factory PaymentResponseDto.fromJson(Map<String, dynamic> json) {
    return PaymentResponseDto(
      id: (json['id'] ?? '') as String,
      organizationId: (json['organizationId'] ?? '') as String,
      orderId: (json['orderId'] ?? '') as String,
      amount: (json['amount'] as num?)?.toDouble() ?? 0.0,
      paymentType: (json['paymentType'] ?? 'full') as String,
      paymentMethod: (json['paymentMethod'] ?? 'online') as String,
      status: (json['status'] ?? 'pending') as String,
      gatewayTransactionId: json['gatewayTransactionId'] as String?,
      paymentLink: json['paymentLink'] as String?,
      createdAt: json['createdAt'] != null
          ? DateTime.tryParse(json['createdAt'] as String) ?? DateTime.now()
          : DateTime.now(),
      confirmedAt: json['confirmedAt'] != null
          ? DateTime.tryParse(json['confirmedAt'] as String)
          : null,
      expiresAt: json['expiresAt'] != null
          ? DateTime.tryParse(json['expiresAt'] as String)
          : null,
    );
  }

  final String id;
  final String organizationId;
  final String orderId;
  final double amount;
  final String paymentType;
  final String paymentMethod;
  final String status;
  final String? gatewayTransactionId;
  final String? paymentLink;
  final DateTime createdAt;
  final DateTime? confirmedAt;
  final DateTime? expiresAt;

  Payment toDomain() {
    return Payment(
      id: id,
      organizationId: organizationId,
      orderId: orderId,
      amount: amount,
      paymentType: paymentType,
      paymentMethod: paymentMethod,
      status: status,
      gatewayTransactionId: gatewayTransactionId,
      paymentLink: paymentLink,
      createdAt: createdAt,
      confirmedAt: confirmedAt,
      expiresAt: expiresAt,
    );
  }
}
