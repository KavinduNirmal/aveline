import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../domain/entities/approval_entry.dart';
import '../../domain/entities/order.dart';
import '../../domain/entities/order_item.dart';
import '../../domain/entities/payment.dart';
import '../../domain/repositories/commerce_repository.dart';
import '../models/approval_dto.dart';
import '../models/order_dto.dart';
import '../models/payment_dto.dart';

class ApiCommerceRepository implements CommerceRepository {
  ApiCommerceRepository(
    this._dio, {
    required this.organizationId,
  });

  final Dio _dio;
  final String? Function() organizationId;

  String _requireOrgId() {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      throw StateError('Cannot perform commerce operation without an active organization context.');
    }
    return orgId;
  }

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
    final orgId = _requireOrgId();
    final effectiveCustomerId = customerId ?? '00000000-0000-0000-0000-000000000001';

    final dto = CreateOrderDto(
      customerId: effectiveCustomerId,
      customerName: customerName,
      orderType: orderType,
      items: items.map(OrderItemDto.fromDomain).toList(),
      discount: discount,
      customerTier: customerTier,
      notes: notes,
    );

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/orders',
      data: dto.toJson(),
    );

    final data = response.data ?? const {};
    return OrderResponseDto.fromJson(data).toDomain();
  }

  @override
  Future<List<Order>> fetchOrders({
    String? status,
    int page = 1,
    int pageSize = 20,
  }) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) return const [];

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/orders',
      queryParameters: {
        if (status != null && status != 'all') 'status': status,
        'page': page,
        'pageSize': pageSize,
      },
    );

    final data = response.data ?? const {};
    final rawList = (data['items'] as List<dynamic>?) ?? const [];
    return rawList
        .map((i) => OrderResponseDto.fromJson(i as Map<String, dynamic>).toDomain())
        .toList();
  }

  @override
  Future<Order?> fetchOrder(String orderId) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) return null;

    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/orders/$orderId',
      );
      final data = response.data ?? const {};
      return OrderResponseDto.fromJson(data).toDomain();
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  @override
  Future<List<ApprovalEntry>> fetchApprovals({
    String? status,
    int page = 1,
    int pageSize = 20,
  }) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) return const [];

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/approvals',
      queryParameters: {
        if (status != null && status != 'all') 'status': status,
        'page': page,
        'pageSize': pageSize,
      },
    );

    final data = response.data ?? const {};
    final rawList = (data['items'] as List<dynamic>?) ?? const [];
    return rawList
        .map((i) => ApprovalQueueResponseDto.fromJson(i as Map<String, dynamic>).toDomain())
        .toList();
  }

  @override
  Future<ApprovalEntry?> fetchApprovalForOrder(String orderId) async {
    final approvals = await fetchApprovals();
    final matching = approvals.where((a) => a.orderId == orderId).toList();
    if (matching.isNotEmpty) {
      return matching.first;
    }
    return null;
  }

  @override
  Future<Payment> generatePayment({
    required String orderId,
    required double amount,
    String paymentType = 'full',
    String paymentMethod = 'online',
  }) async {
    final orgId = _requireOrgId();

    final dto = GeneratePaymentRequestDto(
      orderId: orderId,
      amount: amount,
      paymentType: paymentType,
      paymentMethod: paymentMethod,
    );

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/payments',
      data: dto.toJson(),
    );

    final data = response.data ?? const {};
    return PaymentResponseDto.fromJson(data).toDomain();
  }

  @override
  Future<Payment?> fetchPaymentForOrder(String orderId) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) return null;

    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/payments/order/$orderId',
      );
      final data = response.data ?? const {};
      return PaymentResponseDto.fromJson(data).toDomain();
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  @override
  Future<Payment> confirmPayment(
    String paymentId, {
    String? transactionId,
    String? notes,
  }) async {
    final orgId = _requireOrgId();

    final dto = ConfirmPaymentDto(
      gatewayTransactionId: transactionId,
      notes: notes,
    );

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/payments/$paymentId/confirm',
      data: dto.toJson(),
    );

    final data = response.data ?? const {};
    return PaymentResponseDto.fromJson(data).toDomain();
  }

  @override
  Future<List<int>> fetchPaymentQrBytes(String paymentLink, {int size = 300}) async {
    final orgId = _requireOrgId();

    final response = await _dio.post<List<int>>(
      '/api/v1/orgs/$orgId/catalog/qr/generate',
      data: {
        'payload': paymentLink,
        'format': 'png',
        'size': size,
        'eccLevel': 'M',
        'quietZone': 2,
      },
      options: Options(
        responseType: ResponseType.bytes,
      ),
    );

    return response.data ?? Uint8List(0);
  }
}
