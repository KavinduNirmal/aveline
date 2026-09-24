import 'dart:async';

import 'package:flutter/foundation.dart';

import '../../../../core/notifications/notification_provider.dart';
import '../../domain/entities/approval_entry.dart';
import '../../domain/entities/order.dart';
import '../../domain/entities/payment.dart';
import '../../domain/repositories/commerce_repository.dart';

class ApprovalsRealtimeController extends ChangeNotifier {
  ApprovalsRealtimeController({
    required this.repository,
    this.notificationProvider,
  }) {
    notificationProvider?.addListener(_onNotificationReceived);
  }

  final CommerceRepository repository;
  final NotificationProvider? notificationProvider;

  String? _orderId;
  Order? _order;
  ApprovalEntry? _approval;
  Payment? _payment;
  bool _isLoading = false;
  String? _errorMessage;

  String? get orderId => _orderId;
  Order? get order => _order;
  ApprovalEntry? get approval => _approval;
  Payment? get payment => _payment;
  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;

  bool get isRealtimeActive => notificationProvider != null;

  void _onNotificationReceived() {
    if (_orderId == null) return;
    final last = notificationProvider?.latest;
    if (last == null) return;

    // Refresh if notification mentions approval or matches order
    final text = '${last.title} ${last.body} ${last.type}'.toLowerCase();
    if (text.contains('approval') ||
        text.contains('order') ||
        text.contains(_orderId!.toLowerCase()) ||
        text.contains('decision') ||
        text.contains('confirmed')) {
      unawaited(refresh());
    }
  }

  Future<void> load(String orderId) async {
    _orderId = orderId;
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    await _fetchData();

    _isLoading = false;
    notifyListeners();
  }

  Future<void> refresh() async {
    if (_orderId == null) return;
    await _fetchData();
    notifyListeners();
  }

  Future<void> _fetchData() async {
    if (_orderId == null) return;
    try {
      final results = await Future.wait([
        repository.fetchOrder(_orderId!),
        repository.fetchApprovalForOrder(_orderId!),
        repository.fetchPaymentForOrder(_orderId!),
      ]);

      _order = results[0] as Order?;
      _approval = results[1] as ApprovalEntry?;
      _payment = results[2] as Payment?;
    } catch (e) {
      _errorMessage = e.toString();
    }
  }

  Future<Payment> generatePayment({
    double? amount,
    String paymentType = 'full',
    String paymentMethod = 'online',
  }) async {
    if (_order == null) {
      throw StateError('Cannot generate payment without loaded order');
    }

    final payAmount = amount ?? _order!.total;
    final created = await repository.generatePayment(
      orderId: _order!.id,
      amount: payAmount,
      paymentType: paymentType,
      paymentMethod: paymentMethod,
    );
    _payment = created;
    notifyListeners();
    return created;
  }

  Future<Payment> confirmPayment(String paymentId, {String? paymentMethod}) async {
    final confirmed = await repository.confirmPayment(paymentId, paymentMethod: paymentMethod);
    _payment = confirmed;
    if (_order != null) {
      _order = await repository.fetchOrder(_order!.id);
    }
    notifyListeners();
    return confirmed;
  }

  Future<List<int>> getPaymentQrBytes(String paymentLink, {int size = 300}) {
    return repository.fetchPaymentQrBytes(paymentLink, size: size);
  }

  @override
  void dispose() {
    notificationProvider?.removeListener(_onNotificationReceived);
    super.dispose();
  }
}
