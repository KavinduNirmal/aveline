import 'package:flutter/foundation.dart';

import '../../domain/entities/order.dart';
import '../../domain/entities/order_item.dart';
import '../../domain/repositories/commerce_repository.dart';

class OrderCreationController extends ChangeNotifier {
  OrderCreationController({required this.repository});

  final CommerceRepository repository;

  String _orderType = 'in_store';
  String _customerName = '';
  String? _customerId;
  String? _customerTier;
  String? _notes;
  final List<OrderItem> _items = [];
  double _discountPercent = 0.0;
  bool _isSubmitting = false;
  String? _errorMessage;

  String get orderType => _orderType;
  String get customerName => _customerName;
  String? get customerId => _customerId;
  String? get customerTier => _customerTier;
  String? get notes => _notes;
  List<OrderItem> get items => List.unmodifiable(_items);
  double get discountPercent => _discountPercent;
  double get discount => (subtotal * (_discountPercent / 100.0)).clamp(0.0, subtotal);
  bool get isSubmitting => _isSubmitting;
  String? get errorMessage => _errorMessage;

  double get subtotal => _items.fold(0.0, (sum, i) => sum + i.totalPrice);
  double get totalCost =>
      _items.fold(0.0, (sum, i) => sum + (i.wholesaleCost * i.quantity));
  double get total => (subtotal - discount).clamp(0.0, double.infinity);
  double get margin => total - totalCost;
  double get marginPercent => total > 0 ? (margin / total) * 100 : 0.0;

  bool get triggersApprovalWarning =>
      (subtotal > 0 && _discountPercent > 15.0) || (subtotal > 0 && marginPercent < 20.0);

  bool get canSubmit =>
      _customerName.trim().isNotEmpty && _items.isNotEmpty && !_isSubmitting;

  void setOrderType(String type) {
    if (_orderType == type) return;
    _orderType = type;
    notifyListeners();
  }

  void setCustomerName(String name) {
    _customerName = name;
    notifyListeners();
  }

  void setCustomer({
    required String id,
    required String name,
    String? tier,
  }) {
    _customerId = id;
    _customerName = name;
    _customerTier = tier;
    notifyListeners();
  }

  void setNotes(String? value) {
    _notes = value;
    notifyListeners();
  }

  void setDiscountPercent(double percent) {
    _discountPercent = percent.clamp(0.0, 100.0);
    notifyListeners();
  }

  void setDiscount(double percent) {
    setDiscountPercent(percent);
  }

  void addItem(OrderItem item) {
    final existingIndex = _items.indexWhere((i) => i.itemId == item.itemId);
    if (existingIndex >= 0) {
      final existing = _items[existingIndex];
      _items[existingIndex] = existing.copyWith(
        quantity: existing.quantity + item.quantity,
      );
    } else {
      _items.add(item);
    }
    notifyListeners();
  }

  void updateQuantity(String itemId, int qty) {
    if (qty <= 0) {
      removeItem(itemId);
      return;
    }
    final index = _items.indexWhere((i) => i.itemId == itemId);
    if (index >= 0) {
      _items[index] = _items[index].copyWith(quantity: qty);
      notifyListeners();
    }
  }

  void removeItem(String itemId) {
    _items.removeWhere((i) => i.itemId == itemId);
    notifyListeners();
  }

  Future<Order> submitOrder() async {
    if (!canSubmit) {
      throw StateError('Cannot submit incomplete order');
    }

    _isSubmitting = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final order = await repository.createOrder(
        customerName: _customerName.trim(),
        orderType: _orderType,
        items: _items,
        customerId: _customerId,
        discount: discount > 0 ? discount : null,
        customerTier: _customerTier,
        notes: _notes,
      );
      _isSubmitting = false;
      notifyListeners();
      return order;
    } catch (e) {
      _isSubmitting = false;
      _errorMessage = e.toString();
      notifyListeners();
      rethrow;
    }
  }

  void reset() {
    _orderType = 'in_store';
    _customerName = '';
    _customerId = null;
    _customerTier = null;
    _notes = null;
    _items.clear();
    _discountPercent = 0.0;
    _isSubmitting = false;
    _errorMessage = null;
    notifyListeners();
  }
}
