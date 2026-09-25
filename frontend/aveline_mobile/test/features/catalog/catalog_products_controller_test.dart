import 'dart:async';

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
import 'package:aveline_mobile/features/catalog/domain/supplier.dart';
import 'package:aveline_mobile/features/catalog/domain/vision_analysis.dart';
import 'package:aveline_mobile/features/catalog/presentation/catalog_products_controller.dart';
import 'package:flutter_test/flutter_test.dart';

CatalogProduct _product(String id) => CatalogProduct(
  id: id,
  organizationId: 'org-1',
  name: 'Piece $id',
  description: 'A piece.',
  category: 'Sarees',
  color: 'Wine',
  sizes: const ['36', '38'],
  price: 24500,
  cost: 12000,
  quantity: 4,
  status: CatalogItemStatus.available,
  isAvailable: true,
  createdAtUtc: DateTime.utc(2026, 9, 1),
  fabric: 'Raw silk',
);

/// Serves a fixed list, honouring the page size and the last page's shortness.
class _ListRepository implements CatalogProductRepository {
  _ListRepository(this.pool);

  final List<CatalogProduct> pool;
  int calls = 0;

  // A read-only fake: these tests never take an action, so a mutation has nothing to say.
  @override
  Future<CatalogProduct> updateStatus(String id, CatalogItemStatus status) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<ImageUploadResult> uploadImage({
    required List<int> bytes,
    required String fileName,
    String? contentType,
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<VisionAnalysis> analyzeImage({
    String? imageRefId,
    String? imageUrl,
    String? fileNameHint,
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> createProduct(CreateProductPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> updateProduct(String id, UpdateProductPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<void> deleteProduct(String id) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<OutfitComposition>> getLookbooks({String? occasion, String? query}) =>
      Future.value(const []);

  @override
  Future<OutfitComposition> composeOutfit(ComposeOutfitPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<void> deleteLookbook(String id) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<SourcingRequest>> getSourcingRequests({String? status, String? query}) =>
      Future.value(const []);

  @override
  Future<SourcingRequest> createSourcingRequest(CreateSourcingRequestPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<SourcingRequest> updateSourcingStatus(String id, SourcingStatus status, {String? notes}) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<Supplier>> getSuppliers() =>
      Future.value(const []);

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
  }) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  }) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    calls++;
    final start = page * pageSize;
    if (start >= pool.length) {
      return const CatalogProductPage(
        products: <CatalogProduct>[],
        hasMore: false,
      );
    }
    final end = start + pageSize > pool.length ? pool.length : start + pageSize;
    return CatalogProductPage(
      products: pool.sublist(start, end),
      hasMore: end < pool.length,
    );
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async {
    for (final product in pool) {
      if (product.id == id) {
        return product;
      }
    }
    return null;
  }

  @override
  Future<List<CatalogTag>> fetchTags() => Future.value(const []);
}


/// A repository whose pages are completed by the test, so a reply can be landed
/// after a newer query has already started.
class _ManualRepository implements CatalogProductRepository {
  final List<Completer<CatalogProductPage>> pending = [];
  final List<CatalogProductQuery> queries = [];

  @override
  Future<CatalogProduct> updateStatus(String id, CatalogItemStatus status) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<ImageUploadResult> uploadImage({
    required List<int> bytes,
    required String fileName,
    String? contentType,
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<VisionAnalysis> analyzeImage({
    String? imageRefId,
    String? imageUrl,
    String? fileNameHint,
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> createProduct(CreateProductPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> updateProduct(String id, UpdateProductPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<void> deleteProduct(String id) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<OutfitComposition>> getLookbooks({String? occasion, String? query}) =>
      Future.value(const []);

  @override
  Future<OutfitComposition> composeOutfit(ComposeOutfitPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<void> deleteLookbook(String id) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<SourcingRequest>> getSourcingRequests({String? status, String? query}) =>
      Future.value(const []);

  @override
  Future<SourcingRequest> createSourcingRequest(CreateSourcingRequestPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<SourcingRequest> updateSourcingStatus(String id, SourcingStatus status, {String? notes}) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<Supplier>> getSuppliers() =>
      Future.value(const []);

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
  }) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  }) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) {
    queries.add(query);
    final completer = Completer<CatalogProductPage>();
    pending.add(completer);
    return completer.future;
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async => null;

  @override
  Future<List<CatalogTag>> fetchTags() => Future.value(const []);
}


/// Fails the first page and serves the second, for the retry path.
class _FlakyRepository implements CatalogProductRepository {
  int calls = 0;

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    calls++;
    if (calls == 1) {
      throw Exception('offline');
    }
    return CatalogProductPage(products: [_product('p1')], hasMore: false);
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async => null;

  @override
  Future<CatalogProduct> updateStatus(String id, CatalogItemStatus status) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<ImageUploadResult> uploadImage({
    required List<int> bytes,
    required String fileName,
    String? contentType,
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<VisionAnalysis> analyzeImage({
    String? imageRefId,
    String? imageUrl,
    String? fileNameHint,
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> createProduct(CreateProductPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> updateProduct(String id, UpdateProductPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<void> deleteProduct(String id) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<OutfitComposition>> getLookbooks({String? occasion, String? query}) =>
      Future.value(const []);

  @override
  Future<OutfitComposition> composeOutfit(ComposeOutfitPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<void> deleteLookbook(String id) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<SourcingRequest>> getSourcingRequests({String? status, String? query}) =>
      Future.value(const []);

  @override
  Future<SourcingRequest> createSourcingRequest(CreateSourcingRequestPayload payload) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<SourcingRequest> updateSourcingStatus(String id, SourcingStatus status, {String? notes}) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<Supplier>> getSuppliers() =>
      Future.value(const []);

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
  }) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  }) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<List<CatalogTag>> fetchTags() => Future.value(const []);
}


void main() {
  group('CatalogProductsController', () {
    test('loads the first page', () async {
      final pool = List.generate(10, (i) => _product('p${i + 1}'));
      final controller = CatalogProductsController(
        _ListRepository(pool),
        pageSize: 4,
      );

      await controller.loadFirstPage();

      expect(controller.products.map((p) => p.id), ['p1', 'p2', 'p3', 'p4']);
      expect(controller.hasMore, isTrue);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
      expect(controller.errorMessage, isNull);
    });

    test('appends the next page and stops at the end', () async {
      final pool = List.generate(6, (i) => _product('p${i + 1}'));
      final controller = CatalogProductsController(
        _ListRepository(pool),
        pageSize: 4,
      );

      await controller.loadFirstPage();
      await controller.loadMore();

      expect(controller.products, hasLength(6));
      expect(controller.hasMore, isFalse);
      expect(controller.canLoadMore, isFalse);

      // Asking again past the end is a no-op, not another round trip.
      await controller.loadMore();
      expect(controller.products, hasLength(6));
    });

    test('reports an empty result for a query that matches nothing', () async {
      final controller = CatalogProductsController(
        _ListRepository(const []),
        pageSize: 4,
      );

      await controller.loadFirstPage(
        query: const CatalogProductQuery(search: 'nothing'),
      );

      expect(controller.isEmpty, isTrue);
      expect(controller.products, isEmpty);
      expect(controller.hasMore, isFalse);
    });

    test('drops a page that a newer query has superseded', () async {
      final repository = _ManualRepository();
      final controller = CatalogProductsController(repository);

      final first = controller.loadFirstPage(
        query: const CatalogProductQuery(search: 'silk'),
      );
      final second = controller.loadFirstPage(
        query: const CatalogProductQuery(search: 'velvet'),
      );

      expect(repository.pending, hasLength(2));
      // The newer query lands first; the older reply must not be mixed in.
      repository.pending[1].complete(
        CatalogProductPage(products: [_product('velvet-1')], hasMore: false),
      );
      repository.pending[0].complete(
        CatalogProductPage(products: [_product('silk-1')], hasMore: false),
      );

      await Future.wait([first, second]);

      expect(controller.products.map((p) => p.id), ['velvet-1']);
    });

    test('reports a failed first page, then recovers on retry', () async {
      final controller = CatalogProductsController(_FlakyRepository());

      await controller.loadFirstPage();
      expect(controller.errorMessage, 'offline');
      expect(controller.products, isEmpty);
      expect(controller.canLoadMore, isFalse);

      await controller.retry();

      expect(controller.errorMessage, isNull);
      expect(controller.products.map((p) => p.id), ['p1']);
    });

    test('notifies listeners as each page lands', () async {
      final pool = List.generate(6, (i) => _product('p${i + 1}'));
      final controller = CatalogProductsController(
        _ListRepository(pool),
        pageSize: 4,
      );
      var notifications = 0;
      controller.addListener(() => notifications++);

      await controller.loadFirstPage();
      await controller.loadMore();

      // Loading and settling for each of the two pages.
      expect(notifications, greaterThanOrEqualTo(4));
      controller.dispose();
    });

    test('stays quiet once disposed', () async {
      final controller = CatalogProductsController(
        _ListRepository([_product('p1')]),
      );
      final pending = controller.loadFirstPage();

      controller.dispose();
      await pending;

      // No assertion beyond "does not throw": a page landing after the screen
      // is gone must not notify a disposed notifier.
      expect(controller.products, hasLength(1));
    });
  });
}
