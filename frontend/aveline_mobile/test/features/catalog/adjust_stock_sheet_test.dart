import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_tag.dart';
import 'package:aveline_mobile/features/catalog/domain/customer_match.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_composition.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/product_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_receipt.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_request.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_status.dart';
import 'package:aveline_mobile/features/catalog/domain/stock_adjustment_mode.dart';
import 'package:aveline_mobile/features/catalog/domain/supplier.dart';
import 'package:aveline_mobile/features/catalog/domain/vision_analysis.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/adjust_stock_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeAdjustStockRepository implements CatalogProductRepository {
  _FakeAdjustStockRepository(this.product);

  final CatalogProduct product;
  int? adjustedQuantity;
  CatalogItemStatus? adjustedStatus;

  @override
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  }) async {
    adjustedQuantity = quantity;
    adjustedStatus = status;
    return product.copyWith(
      quantity: quantity,
      status: status ?? (quantity <= 0 ? CatalogItemStatus.soldOut : product.status),
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
  Future<CatalogSaleReceipt> recordSale({
    required String itemId,
    required RecordSalePayload payload,
  }) => throw UnimplementedError();

  @override
  Future<List<CatalogTag>> fetchTags() => Future.value(const []);
}

CatalogProduct _buildTestProduct({int quantity = 6}) {
  return CatalogProduct(
    id: 'piece-adj-1',
    organizationId: 'org-1',
    name: 'Royal Velvet Sherwani',
    category: 'Outerwear',
    color: 'Navy',
    sizes: const ['40'],
    price: 95000.0,
    cost: 45000.0,
    quantity: quantity,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 24),
    sku: 'AVL-OUT-001',
  );
}

void main() {
  testWidgets('AdjustStockSheet in reduce mode renders count comparison and adjusts quantity', (tester) async {
    final product = _buildTestProduct(quantity: 6);
    final repo = _FakeAdjustStockRepository(product);

    CatalogProduct? updatedResult;

    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: ElevatedButton(
              onPressed: () async {
                updatedResult = await AdjustStockSheet.show(
                  context,
                  piece: product,
                  mode: StockAdjustmentMode.reduce,
                  repository: repo,
                );
              },
              child: const Text('Open Reduce'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open Reduce'));
    await tester.pumpAndSettle();

    expect(find.text('Reduce Stock'), findsWidgets);
    expect(find.text('Royal Velvet Sherwani'), findsOneWidget);
    expect(find.text('BEFORE: '), findsOneWidget);
    expect(find.text('6'), findsWidgets);

    // Initial Stock After = 6 - 1 = 5
    expect(find.byKey(const Key('adjust_stock_after_value')), findsOneWidget);
    expect(find.text('5'), findsOneWidget);

    // Increment pieces to remove to 2
    await tester.tap(find.byKey(const Key('adjust_stock_qty_increment')));
    await tester.pumpAndSettle();

    // Stock After: 6 - 2 = 4
    expect(find.text('4'), findsOneWidget);

    // Confirm adjustment
    await tester.tap(find.byKey(const Key('adjust_stock_confirm_button')));
    await tester.pumpAndSettle();

    expect(repo.adjustedQuantity, 4);
    expect(updatedResult, isNotNull);
    expect(updatedResult?.quantity, 4);
  });

  testWidgets('AdjustStockSheet in outOfStock mode renders warning and zeroes count', (tester) async {
    final product = _buildTestProduct(quantity: 4);
    final repo = _FakeAdjustStockRepository(product);

    CatalogProduct? updatedResult;

    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: ElevatedButton(
              onPressed: () async {
                updatedResult = await AdjustStockSheet.show(
                  context,
                  piece: product,
                  mode: StockAdjustmentMode.outOfStock,
                  repository: repo,
                );
              },
              child: const Text('Open OutOfStock'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open OutOfStock'));
    await tester.pumpAndSettle();

    expect(find.text('Mark Out of Stock'), findsWidgets);
    expect(
      find.text('The count becomes 0 and the piece is marked Sold out, so it stops being offered on the floor. Set a new count from the edit screen when it returns.'),
      findsOneWidget,
    );

    // Stock After = 0
    expect(find.text('0'), findsOneWidget);

    // Confirm Mark Out of Stock
    await tester.tap(find.byKey(const Key('adjust_stock_confirm_button')));
    await tester.pumpAndSettle();

    expect(repo.adjustedQuantity, 0);
    expect(repo.adjustedStatus, CatalogItemStatus.soldOut);
    expect(updatedResult, isNotNull);
    expect(updatedResult?.quantity, 0);
    expect(updatedResult?.status, CatalogItemStatus.soldOut);
  });
}
