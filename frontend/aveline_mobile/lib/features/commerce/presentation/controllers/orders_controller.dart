import 'package:flutter/foundation.dart';

import '../../domain/entities/order.dart';
import '../../domain/repositories/commerce_repository.dart';

class OrdersController extends ChangeNotifier {
  OrdersController({required this.repository});

  final CommerceRepository repository;

  List<Order> _orders = [];
  bool _isLoading = false;
  String? _errorMessage;
  String _selectedStatus = 'all';

  List<Order> get orders => List.unmodifiable(_orders);
  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;
  String get selectedStatus => _selectedStatus;

  Future<void> fetchOrders({String? status}) async {
    if (status != null) {
      _selectedStatus = status;
    }
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      _orders = await repository.fetchOrders(
        status: _selectedStatus == 'all' ? null : _selectedStatus,
      );
    } catch (e) {
      _errorMessage = e.toString();
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  void setFilter(String status) {
    if (_selectedStatus == status) return;
    _selectedStatus = status;
    fetchOrders(status: status);
  }
}
