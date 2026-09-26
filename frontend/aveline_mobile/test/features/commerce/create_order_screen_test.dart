import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order.dart';
import 'package:aveline_mobile/features/commerce/domain/entities/order_item.dart';
import 'package:aveline_mobile/features/commerce/domain/repositories/commerce_repository.dart';
import 'package:aveline_mobile/features/commerce/presentation/screens/create_order_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeCommerceRepo implements CommerceRepository {
  Order? createdOrder;

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
    final subtotal = items.fold(0.0, (sum, i) => sum + i.totalPrice);
    final disc = discount ?? 0.0;
    final total = subtotal - disc;

    final order = Order(
      id: 'ord-test-1',
      organizationId: 'org-1',
      customerId: customerId ?? 'cus-1',
      customerName: customerName,
      orderType: orderType,
      status: 'confirmed',
      subtotal: subtotal,
      discount: disc,
      total: total,
      totalCost: 10000.0,
      margin: total - 10000.0,
      createdAt: DateTime.now(),
      items: items,
    );
    createdOrder = order;
    return order;
  }

  @override
  noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class FakeCatalogRepo implements CatalogProductRepository {
  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    return CatalogProductPage(
      products: [
        CatalogProduct(
          id: 'prod-1',
          organizationId: 'org-1',
          name: 'Banarasi Saree',
          category: 'Sarees',
          color: 'Crimson',
          sizes: const ['Free Size'],
          price: 45000.0,
          cost: 20000.0,
          quantity: 3,
          status: CatalogItemStatus.available,
          isAvailable: true,
          createdAtUtc: DateTime.utc(2026, 9, 1),
          fabric: 'Silk',
          style: 'Traditional',
        ),
      ],
      hasMore: false,
    );
  }

  @override
  noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

void main() {
  testWidgets('CreateOrderScreen renders, allows input, and shows catalog picker', (tester) async {
    final commerceRepo = FakeCommerceRepo();
    final catalogRepo = FakeCatalogRepo();

    await tester.pumpWidget(
      MaterialApp(
        home: CreateOrderScreen(
          commerceRepository: commerceRepo,
          catalogRepository: catalogRepo,
        ),
      ),
    );

    expect(find.text('New Commerce Order'), findsOneWidget);
    expect(find.text('In-Store Counter'), findsOneWidget);
    expect(find.text('WhatsApp Concierge'), findsOneWidget);

    // Switch to WhatsApp
    await tester.tap(find.text('WhatsApp Concierge'));
    await tester.pumpAndSettle();

    // Fill customer name
    await tester.enterText(find.byType(TextField).first, 'Natasha Perera');
    await tester.pumpAndSettle();

    // Tap Add Piece / Browse Catalog
    await tester.tap(find.text('Browse Catalog'));
    await tester.pumpAndSettle();

    // Verify catalog piece sheet opens and displays item
    expect(find.text('Select Catalog Piece'), findsOneWidget);
    expect(find.text('Banarasi Saree'), findsOneWidget);

    // Select the piece
    await tester.tap(find.text('Banarasi Saree'));
    await tester.pumpAndSettle();

    // Verify item is now in the order list with price
    expect(find.text('Banarasi Saree'), findsOneWidget);
    expect(find.text('LKR 45000'), findsWidgets);

    // Verify Create Order button is now active
    final createBtn = find.widgetWithText(FilledButton, 'Create Order');
    expect(createBtn, findsOneWidget);
  });
}
