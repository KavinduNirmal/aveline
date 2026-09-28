import '../../domain/entities/order.dart';
import '../../domain/entities/order_item.dart';

class OrderItemDto {
  const OrderItemDto({
    this.id,
    required this.itemId,
    required this.itemName,
    required this.quantity,
    required this.unitPrice,
    required this.wholesaleCost,
    required this.totalPrice,
  });

  factory OrderItemDto.fromJson(Map<String, dynamic> json) {
    return OrderItemDto(
      id: json['id'] as String?,
      itemId: (json['itemId'] ?? json['id'] ?? '') as String,
      itemName: (json['itemName'] ?? json['name'] ?? json['title'] ?? '') as String,
      quantity: (json['quantity'] as num?)?.toInt() ?? 1,
      unitPrice: (json['unitPrice'] as num?)?.toDouble() ?? 0.0,
      wholesaleCost: (json['wholesaleCost'] as num?)?.toDouble() ?? 0.0,
      totalPrice: (json['totalPrice'] as num?)?.toDouble() ?? 0.0,
    );
  }

  factory OrderItemDto.fromDomain(OrderItem item) {
    return OrderItemDto(
      id: item.id,
      itemId: item.itemId,
      itemName: item.itemName,
      quantity: item.quantity,
      unitPrice: item.unitPrice,
      wholesaleCost: item.wholesaleCost,
      totalPrice: item.totalPrice,
    );
  }

  final String? id;
  final String itemId;
  final String itemName;
  final int quantity;
  final double unitPrice;
  final double wholesaleCost;
  final double totalPrice;

  Map<String, dynamic> toJson() {
    return {
      if (id != null) 'id': id,
      'itemId': itemId,
      'itemName': itemName,
      'quantity': quantity,
      'unitPrice': unitPrice,
      'wholesaleCost': wholesaleCost,
      'totalPrice': totalPrice,
    };
  }

  OrderItem toDomain() {
    return OrderItem(
      id: id,
      itemId: itemId,
      itemName: itemName,
      quantity: quantity,
      unitPrice: unitPrice,
      wholesaleCost: wholesaleCost,
      totalPrice: totalPrice,
    );
  }
}

class CreateOrderDto {
  const CreateOrderDto({
    required this.customerId,
    required this.customerName,
    this.orderType = 'in_store',
    required this.items,
    this.discount,
    this.customerTier,
    this.notes,
  });

  final String customerId;
  final String customerName;
  final String orderType;
  final List<OrderItemDto> items;
  final double? discount;
  final String? customerTier;
  final String? notes;

  Map<String, dynamic> toJson() {
    return {
      'customerId': customerId,
      'customerName': customerName,
      'orderType': orderType,
      'items': items.map((i) => i.toJson()).toList(),
      if (discount != null) 'discount': discount,
      if (customerTier != null) 'customerTier': customerTier,
      if (notes != null) 'notes': notes,
    };
  }
}

class OrderResponseDto {
  const OrderResponseDto({
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

  factory OrderResponseDto.fromJson(Map<String, dynamic> json) {
    final rawItems = (json['items'] as List<dynamic>?) ?? const [];
    return OrderResponseDto(
      id: (json['id'] ?? '') as String,
      organizationId: (json['organizationId'] ?? '') as String,
      customerId: (json['customerId'] ?? '') as String,
      customerName: (json['customerName'] ?? '') as String,
      orderType: (json['orderType'] ?? 'in_store') as String,
      status: (json['status'] ?? 'pending_approval') as String,
      subtotal: (json['subtotal'] as num?)?.toDouble() ?? 0.0,
      discount: (json['discount'] as num?)?.toDouble() ?? 0.0,
      total: (json['total'] as num?)?.toDouble() ?? 0.0,
      totalCost: (json['totalCost'] as num?)?.toDouble() ?? 0.0,
      margin: (json['margin'] as num?)?.toDouble() ?? 0.0,
      createdBy: json['createdBy'] as String?,
      createdAt: json['createdAt'] != null
          ? DateTime.tryParse(json['createdAt'] as String) ?? DateTime.now()
          : DateTime.now(),
      updatedAt: json['updatedAt'] != null
          ? DateTime.tryParse(json['updatedAt'] as String)
          : null,
      items: rawItems
          .map((i) => OrderItemDto.fromJson(i as Map<String, dynamic>))
          .toList(),
    );
  }

  final String id;
  final String organizationId;
  final String customerId;
  final String customerName;
  final String orderType;
  final String status;
  final double subtotal;
  final double discount;
  final double total;
  final double totalCost;
  final double margin;
  final String? createdBy;
  final DateTime createdAt;
  final DateTime? updatedAt;
  final List<OrderItemDto> items;

  Order toDomain() {
    return Order(
      id: id,
      organizationId: organizationId,
      customerId: customerId,
      customerName: customerName,
      orderType: orderType,
      status: status,
      subtotal: subtotal,
      discount: discount,
      total: total,
      totalCost: totalCost,
      margin: margin,
      createdBy: createdBy,
      createdAt: createdAt,
      updatedAt: updatedAt,
      items: items.map((i) => i.toDomain()).toList(),
    );
  }
}
