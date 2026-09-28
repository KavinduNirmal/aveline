class OrderItem {
  const OrderItem({
    this.id,
    required this.itemId,
    required this.itemName,
    this.quantity = 1,
    required this.unitPrice,
    this.wholesaleCost = 0.0,
    double? totalPrice,
  }) : totalPrice = totalPrice ?? (unitPrice * quantity);

  final String? id;
  final String itemId;
  final String itemName;
  final int quantity;
  final double unitPrice;
  final double wholesaleCost;
  final double totalPrice;

  double get margin => totalPrice - (wholesaleCost * quantity);
  double get marginPercent =>
      totalPrice > 0 ? ((totalPrice - (wholesaleCost * quantity)) / totalPrice) * 100 : 0.0;

  OrderItem copyWith({
    String? id,
    String? itemId,
    String? itemName,
    int? quantity,
    double? unitPrice,
    double? wholesaleCost,
    double? totalPrice,
  }) {
    final nextQty = quantity ?? this.quantity;
    final nextUnitPrice = unitPrice ?? this.unitPrice;
    return OrderItem(
      id: id ?? this.id,
      itemId: itemId ?? this.itemId,
      itemName: itemName ?? this.itemName,
      quantity: nextQty,
      unitPrice: nextUnitPrice,
      wholesaleCost: wholesaleCost ?? this.wholesaleCost,
      totalPrice: totalPrice ?? (nextUnitPrice * nextQty),
    );
  }
}
