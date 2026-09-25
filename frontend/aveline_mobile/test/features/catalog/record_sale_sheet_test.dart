import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/domain/customer_match.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_composition.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/product_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_receipt.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_request.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_status.dart';
import 'package:aveline_mobile/features/catalog/domain/supplier.dart';
import 'package:aveline_mobile/features/catalog/domain/vision_analysis.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/record_sale_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeRecordSaleRepository implements CatalogProductRepository {
  _FakeRecordSaleRepository(this.product);

  final CatalogProduct product;
  RecordSalePayload? recordedPayload;
  bool shouldFail = false;

  @override
  Future<CatalogSaleReceipt> recordSale({
    required String itemId,
    required RecordSalePayload payload,
  }) async {
    if (shouldFail) {
      throw StateError('insufficient-stock');
    }
    recordedPayload = payload;
    final remaining = product.quantity - payload.quantity;
    return CatalogSaleReceipt(
      itemId: itemId,
      itemName: product.name,
      sku: product.sku,
      quantitySold: payload.quantity,
      unitPrice: payload.unitPrice,
      totalAmount: payload.quantity * payload.unitPrice,
      remainingStock: remaining,
      status: remaining <= 0 ? CatalogItemStatus.soldOut : product.status,
      ledgerEntryId: 'ledger-mock-123',
      recordedAtUtc: DateTime.utc(2026, 9, 24, 14, 0),
    );
  }

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) => throw UnimplementedError();

  @override
  Future<CatalogProduct?> fetchProduct(String id) async => product;

  @override
  Future<CatalogProduct> updateStatus(String id, CatalogItemStatus status) =>
      throw UnimplementedError();

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) => throw UnimplementedError();

  @override
  Future<ImageUploadResult> uploadImage({
    required List<int> bytes,
    required String fileName,
    String? contentType,
  }) => throw UnimplementedError();

  @override
  Future<VisionAnalysis> analyzeImage({
    String? imageRefId,
    String? imageUrl,
    String? fileNameHint,
  }) => throw UnimplementedError();

  @override
  Future<CatalogProduct> createProduct(CreateProductPayload payload) =>
      throw UnimplementedError();

  @override
  Future<CatalogProduct> updateProduct(String id, UpdateProductPayload payload) =>
      throw UnimplementedError();

  @override
  Future<void> deleteProduct(String id) => throw UnimplementedError();

  @override
  Future<List<OutfitComposition>> getLookbooks({String? occasion, String? query}) =>
      Future.value(const []);

  @override
  Future<OutfitComposition> composeOutfit(ComposeOutfitPayload payload) =>
      throw UnimplementedError();

  @override
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload) =>
      throw UnimplementedError();

  @override
  Future<void> deleteLookbook(String id) => throw UnimplementedError();

  @override
  Future<List<SourcingRequest>> getSourcingRequests({String? status, String? query}) =>
      Future.value(const []);

  @override
  Future<SourcingRequest> createSourcingRequest(CreateSourcingRequestPayload payload) =>
      throw UnimplementedError();

  @override
  Future<SourcingRequest> updateSourcingStatus(String id, SourcingStatus status, {String? notes}) =>
      throw UnimplementedError();

  @override
  Future<List<Supplier>> getSuppliers() => Future.value(const []);

  @override
  Future<List<SupplierCatalogItem>> getSupplierCatalog(String supplierId) =>
      Future.value(const []);

  @override
  Future<List<CustomerMatch>> getCustomerMatches(String productId) =>
      Future.value(const []);

  @override
  Future<List<CustomerMatch>> generateCustomerMatches(String productId) =>
      Future.value(const []);

  @override
  Future<void> markMatchActed(String matchId) =>
      Future.value();

  @override
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  }) => throw UnimplementedError();
}

CatalogProduct _buildTestProduct({int quantity = 5, double price = 50000.0}) {
  return CatalogProduct(
    id: 'piece-1',
    organizationId: 'org-1',
    name: 'Kanjeevaram Gold Silk Saree',
    category: 'Sarees',
    color: 'Crimson',
    sizes: const ['36', '38'],
    price: price,
    cost: 25000.0,
    quantity: quantity,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 24),
    sku: 'AVL-SAR-001',
    discountMaxPercent: 15,
  );
}

void _useTallSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2400);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

void main() {
  testWidgets('RecordSaleSheet renders piece details, stepper, and default pricing', (tester) async {
    _useTallSurface(tester);
    final product = _buildTestProduct();
    final repo = _FakeRecordSaleRepository(product);

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: RecordSaleSheet(piece: product, repository: repo),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Record Counter Sale'), findsOneWidget);
    expect(find.text('Kanjeevaram Gold Silk Saree'), findsOneWidget);
    expect(find.text('AVL-SAR-001 · Sarees'), findsOneWidget);
    expect(find.text('5 in stock'), findsOneWidget);

    // Initial Quantity = 1
    expect(find.byKey(const Key('record_sale_quantity_input')), findsOneWidget);
    expect(find.text('1'), findsWidgets);

    // Initial Price = Rs 50,000
    expect(find.byKey(const Key('record_sale_price_input')), findsOneWidget);
    expect(find.text('Rs 50,000'), findsWidgets);

    // Initial Stock After = 4
    expect(find.byKey(const Key('record_sale_stock_after')), findsOneWidget);
    expect(find.text('4'), findsOneWidget);
  });

  testWidgets('RecordSaleSheet increments quantity and updates live total and remaining stock', (tester) async {
    _useTallSurface(tester);
    final product = _buildTestProduct(quantity: 3, price: 40000.0);
    final repo = _FakeRecordSaleRepository(product);

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: RecordSaleSheet(piece: product, repository: repo),
        ),
      ),
    );
    await tester.pumpAndSettle();

    // Increment Quantity from 1 to 2
    await tester.tap(find.byKey(const Key('record_sale_qty_increment')));
    await tester.pumpAndSettle();

    // Sale Total: 2 * 40,000 = Rs 80,000
    expect(find.text('Rs 80,000'), findsOneWidget);
    // Stock After: 3 - 2 = 1
    expect(find.text('1'), findsWidgets);

    // Increment Quantity to 3 (Max)
    await tester.tap(find.byKey(const Key('record_sale_qty_increment')));
    await tester.pumpAndSettle();

    // Sale Total: 3 * 40,000 = Rs 120,000
    expect(find.text('Rs 120,000'), findsOneWidget);
    // Stock After: 0 (Sold Out indicator)
    expect(find.text('(Sold Out)'), findsOneWidget);
  });

  testWidgets('RecordSaleSheet applies quick discount chips', (tester) async {
    _useTallSurface(tester);
    final product = _buildTestProduct(price: 100000.0);
    final repo = _FakeRecordSaleRepository(product);

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: RecordSaleSheet(piece: product, repository: repo),
        ),
      ),
    );
    await tester.pumpAndSettle();

    // Tap -10% discount chip
    await tester.tap(find.text('-10%'));
    await tester.pumpAndSettle();

    // Price should now be 90,000 (100,000 * 0.90)
    expect(find.text('Rs 90,000'), findsWidgets);

    // Tap Tag price chip to reset
    await tester.tap(find.text('Tag price'));
    await tester.pumpAndSettle();

    expect(find.text('Rs 100,000'), findsWidgets);
  });

  testWidgets('RecordSaleSheet submits sale and returns receipt', (tester) async {
    _useTallSurface(tester);
    final product = _buildTestProduct(quantity: 4, price: 60000.0);
    final repo = _FakeRecordSaleRepository(product);

    CatalogSaleReceipt? returnedReceipt;

    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: ElevatedButton(
              onPressed: () async {
                returnedReceipt = await RecordSaleSheet.show(
                  context,
                  piece: product,
                  repository: repo,
                );
              },
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();

    // Enter note
    await tester.enterText(
      find.byKey(const Key('record_sale_note_input')),
      'Bridal order with alterations',
    );
    await tester.pumpAndSettle();

    // Submit sale
    await tester.tap(find.byKey(const Key('record_sale_confirm_button')));
    await tester.pumpAndSettle();

    expect(repo.recordedPayload, isNotNull);
    expect(repo.recordedPayload?.quantity, 1);
    expect(repo.recordedPayload?.unitPrice, 60000.0);
    expect(repo.recordedPayload?.note, 'Bridal order with alterations');

    expect(returnedReceipt, isNotNull);
    expect(returnedReceipt?.itemId, 'piece-1');
    expect(returnedReceipt?.remainingStock, 3);
    expect(returnedReceipt?.totalAmount, 60000.0);
  });
}
