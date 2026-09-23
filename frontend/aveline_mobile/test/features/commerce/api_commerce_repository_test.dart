import 'dart:typed_data';

import 'package:aveline_mobile/features/commerce/data/repositories/api_commerce_repository.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order_item.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ApiCommerceRepository', () {
    late Dio dio;
    late List<Map<String, dynamic>> recordedRequests;
    late ApiCommerceRepository repository;

    setUp(() {
      dio = Dio();
      recordedRequests = [];

      dio.interceptors.add(
        InterceptorsWrapper(
          onRequest: (options, handler) {
            recordedRequests.add({
              'path': options.path,
              'method': options.method,
              'data': options.data,
              'queryParameters': options.queryParameters,
              'responseType': options.responseType,
            });

            if (options.path.endsWith('/orders') && options.method == 'POST') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 201,
                  data: {
                    'id': 'ord-101',
                    'organizationId': 'org-1',
                    'customerId': 'cus-1',
                    'customerName': 'Amara Silva',
                    'orderType': 'in_store',
                    'status': 'pending_approval',
                    'subtotal': 45000.0,
                    'discount': 5000.0,
                    'total': 40000.0,
                    'totalCost': 25000.0,
                    'margin': 15000.0,
                    'createdAt': '2026-09-23T10:00:00Z',
                    'items': [
                      {
                        'id': 'line-1',
                        'itemId': 'it-1',
                        'itemName': 'Silk Scarf',
                        'quantity': 1,
                        'unitPrice': 45000.0,
                        'wholesaleCost': 25000.0,
                        'totalPrice': 45000.0,
                      }
                    ],
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/orders') && options.method == 'GET') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'items': [
                      {
                        'id': 'ord-101',
                        'organizationId': 'org-1',
                        'customerId': 'cus-1',
                        'customerName': 'Amara Silva',
                        'orderType': 'in_store',
                        'status': 'pending_approval',
                        'subtotal': 45000.0,
                        'discount': 5000.0,
                        'total': 40000.0,
                        'totalCost': 25000.0,
                        'margin': 15000.0,
                        'createdAt': '2026-09-23T10:00:00Z',
                      }
                    ],
                    'totalCount': 1,
                    'page': 1,
                    'pageSize': 20,
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/orders/ord-101') && options.method == 'GET') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'ord-101',
                    'organizationId': 'org-1',
                    'customerId': 'cus-1',
                    'customerName': 'Amara Silva',
                    'orderType': 'in_store',
                    'status': 'pending_approval',
                    'subtotal': 45000.0,
                    'discount': 5000.0,
                    'total': 40000.0,
                    'totalCost': 25000.0,
                    'margin': 15000.0,
                    'createdAt': '2026-09-23T10:00:00Z',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/approvals') && options.method == 'GET') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'items': [
                      {
                        'id': 'app-1',
                        'organizationId': 'org-1',
                        'orderId': 'ord-101',
                        'approvalType': 'discount_limit',
                        'status': 'pending',
                        'thresholdExceeded': true,
                        'reason': 'Requested discount exceeds margin threshold',
                        'createdAt': '2026-09-23T10:05:00Z',
                      }
                    ],
                    'totalCount': 1,
                    'page': 1,
                    'pageSize': 20,
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/payments') && options.method == 'POST') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 201,
                  data: {
                    'id': 'pay-1',
                    'organizationId': 'org-1',
                    'orderId': 'ord-101',
                    'amount': 40000.0,
                    'paymentType': 'full',
                    'paymentMethod': 'online',
                    'status': 'pending',
                    'paymentLink': 'https://pay.aveline.boutique/checkout/ab12cd34',
                    'createdAt': '2026-09-23T10:10:00Z',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/payments/pay-1/confirm') && options.method == 'POST') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'pay-1',
                    'organizationId': 'org-1',
                    'orderId': 'ord-101',
                    'amount': 40000.0,
                    'paymentType': 'full',
                    'paymentMethod': 'online',
                    'status': 'confirmed',
                    'paymentLink': 'https://pay.aveline.boutique/checkout/ab12cd34',
                    'createdAt': '2026-09-23T10:10:00Z',
                    'confirmedAt': '2026-09-23T10:15:00Z',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/qr/generate') && options.method == 'POST') {
              handler.resolve(
                Response<List<int>>(
                  requestOptions: options,
                  statusCode: 200,
                  data: Uint8List.fromList([137, 80, 78, 71, 13, 10, 26, 10]),
                ),
              );
              return;
            }

            handler.reject(
              DioException(
                requestOptions: options,
                type: DioExceptionType.badResponse,
                response: Response(requestOptions: options, statusCode: 404),
              ),
            );
          },
        ),
      );

      repository = ApiCommerceRepository(
        dio,
        organizationId: () => 'org-1',
      );
    });

    test('createOrder posts correctly and maps domain Order', () async {
      final order = await repository.createOrder(
        customerName: 'Amara Silva',
        orderType: 'in_store',
        items: const [
          OrderItem(
            itemId: 'it-1',
            itemName: 'Silk Scarf',
            quantity: 1,
            unitPrice: 45000.0,
            wholesaleCost: 25000.0,
          ),
        ],
        discount: 5000.0,
      );

      expect(recordedRequests.length, 1);
      final req = recordedRequests.first;
      expect(req['path'], '/api/v1/orgs/org-1/orders');
      expect(req['method'], 'POST');
      expect(order.id, 'ord-101');
      expect(order.customerName, 'Amara Silva');
      expect(order.isPendingApproval, true);
    });

    test('fetchOrders queries order list', () async {
      final orders = await repository.fetchOrders();
      expect(orders.length, 1);
      expect(orders.first.id, 'ord-101');
      expect(recordedRequests.first['path'], '/api/v1/orgs/org-1/orders');
    });

    test('fetchOrder queries single order by id', () async {
      final order = await repository.fetchOrder('ord-101');
      expect(order, isNotNull);
      expect(order!.id, 'ord-101');
    });

    test('fetchApprovals retrieves pending approvals', () async {
      final approvals = await repository.fetchApprovals();
      expect(approvals.length, 1);
      expect(approvals.first.id, 'app-1');
      expect(approvals.first.orderId, 'ord-101');
    });

    test('fetchApprovalForOrder filters approval for specific order', () async {
      final approval = await repository.fetchApprovalForOrder('ord-101');
      expect(approval, isNotNull);
      expect(approval!.orderId, 'ord-101');
    });

    test('generatePayment creates a payment request', () async {
      final payment = await repository.generatePayment(
        orderId: 'ord-101',
        amount: 40000.0,
      );

      expect(payment.id, 'pay-1');
      expect(payment.paymentLink, contains('pay.aveline.boutique'));
    });

    test('confirmPayment marks payment as confirmed', () async {
      final confirmed = await repository.confirmPayment('pay-1');
      expect(confirmed.isConfirmed, true);
      expect(confirmed.confirmedAt, isNotNull);
    });

    test('fetchPaymentQrBytes requests PNG bytes from catalog QR endpoint', () async {
      final bytes = await repository.fetchPaymentQrBytes('https://pay.aveline.boutique/checkout/ab12cd34');
      expect(bytes.isNotEmpty, true);
      expect(bytes.take(4).toList(), [137, 80, 78, 71]); // PNG magic bytes
    });
  });
}
