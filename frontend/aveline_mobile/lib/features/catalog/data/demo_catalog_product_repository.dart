import 'dart:math' as math;

import '../domain/catalog_filters.dart';
import '../domain/catalog_product.dart';
import '../domain/catalog_tag.dart';
import '../domain/customer_match.dart';
import '../domain/outfit_composition.dart';
import '../domain/outfit_item.dart';
import '../domain/outfit_payloads.dart';
import '../domain/product_payloads.dart';
import '../domain/sale_payloads.dart';
import '../domain/sale_receipt.dart';
import '../domain/sourcing_payloads.dart';
import '../domain/sourcing_request.dart';
import '../domain/sourcing_status.dart';
import '../domain/supplier.dart';
import '../domain/vision_analysis.dart';
import 'catalog_product_repository.dart';
import 'demo_catalog_tags.dart';

/// A boutique's worth of pieces, paged in memory.
///
/// Stands in for `POST /internal/visual/inventory/search` until the inventory
/// slice is wired. It filters the same way the query asks the API to, so the
/// screen's search, tags and filter options already behave like the real thing,
/// and it fills every field of `InventoryItemDto` so the detail screen has real
/// data to lay out.
class DemoCatalogProductRepository implements CatalogProductRepository {
  DemoCatalogProductRepository({
    this.pageDelay = const Duration(milliseconds: 350),
  });

  /// How long a page takes to arrive.
  ///
  /// The pool is local, so without a delay the list would arrive instantly and
  /// the infinite-scroll loading state would never be seen. Tests pass
  /// `Duration.zero`.
  final Duration pageDelay;

  List<CatalogProduct>? _pool;
  List<OutfitComposition>? _lookbooksPool;
  List<SourcingRequest>? _sourcingPool;
  List<Supplier>? _suppliersPool;

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final matching = _filter(_pool ??= _buildPool(), query);
    final start = page * pageSize;
    if (start >= matching.length) {
      return const CatalogProductPage(
        products: <CatalogProduct>[],
        hasMore: false,
      );
    }

    final end = math.min(start + pageSize, matching.length);
    return CatalogProductPage(
      products: matching.sublist(start, end),
      hasMore: end < matching.length,
    );
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    for (final product in pool) {
      if (product.id == id) {
        return product;
      }
    }
    return null;
  }

  @override
  Future<CatalogProduct> updateStatus(
    String id,
    CatalogItemStatus status,
  ) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final index = pool.indexWhere((piece) => piece.id == id);
    if (index < 0) {
      throw StateError('No demo piece with id $id.');
    }

    // The API derives `isAvailable` from the status, so the stand-in derives it the same way
    // rather than letting the two disagree on the screen. The change is held in the pool, so
    // it survives for the session and the grid reads it back.
    final updated = pool[index].copyWith(
      status: status,
      isAvailable: status == CatalogItemStatus.available,
    );
    pool[index] = updated;
    return updated;
  }

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    return 'demo-sourcing-${piece.id}';
  }

  @override
  Future<ImageUploadResult> uploadImage({
    required List<int> bytes,
    required String fileName,
    String? contentType,
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    return ImageUploadResult(
      id: 'demo-img-${DateTime.now().millisecondsSinceEpoch}',
      url: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
      fileName: fileName,
      fileSizeBytes: bytes.length,
      contentType: contentType ?? 'image/jpeg',
    );
  }

  @override
  Future<VisionAnalysis> analyzeImage({
    String? imageRefId,
    String? imageUrl,
    String? fileNameHint,
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final lowerHint = (fileNameHint ?? '').toLowerCase();
    final isLehenga = lowerHint.contains('lehenga');
    final isGown = lowerHint.contains('gown');

    final category = isLehenga ? 'Lehengas' : isGown ? 'Gowns' : 'Sarees';
    final name = isLehenga
        ? 'Crimson Zari Bridal Lehenga'
        : isGown
            ? 'Burgundy Sculpted Velvet Gown'
            : 'Heirloom Kanjeevaram Silk Saree';

    return VisionAnalysis(
      category: category,
      detectedColor: 'Crimson Red',
      colorHex: '#800020',
      fabric: isGown ? 'Micro Velvet' : 'Pure Mulberry Silk',
      style: 'Royal Heritage',
      pattern: 'Gold Zari Brocade',
      suggestedItemName: name,
      confidenceScore: 0.96,
      visualAttributes: const ['Crimson Red', 'Mulberry Silk', 'Gold Zari Brocade', 'Heirloom'],
      summary: 'Handcrafted couture piece woven from pure silk with intricate metallic zari detailing.',
      stylingNotes: 'Pair with handcrafted antique gold temple jewelry and a gold-embroidered clutch.',
      isFallback: false,
    );
  }

  @override
  Future<CatalogProduct> createProduct(CreateProductPayload payload) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final newId = 'demo-piece-${DateTime.now().millisecondsSinceEpoch}';
    final product = CatalogProduct(
      id: newId,
      organizationId: 'demo-org-1',
      name: payload.name,
      category: payload.category,
      color: payload.color,
      sizes: payload.sizes.isEmpty ? const ['One size'] : payload.sizes,
      price: payload.price,
      cost: payload.cost,
      quantity: payload.quantity,
      status: CatalogItemStatus.available,
      isAvailable: true,
      createdAtUtc: DateTime.now(),
      fabric: payload.fabric,
      style: payload.style,
      imageUrl: payload.imageUrl,
      sku: payload.sku ?? 'AVL-${math.Random().nextInt(900) + 100}',
      description: payload.description,
    );

    pool.insert(0, product);
    return product;
  }

  @override
  Future<CatalogProduct> updateProduct(String id, UpdateProductPayload payload) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final index = pool.indexWhere((piece) => piece.id == id);
    if (index < 0) {
      throw StateError('No demo piece with id $id.');
    }

    final existing = pool[index];
    final updated = CatalogProduct(
      id: existing.id,
      organizationId: existing.organizationId,
      name: payload.name ?? existing.name,
      category: payload.category ?? existing.category,
      color: payload.color ?? existing.color,
      sizes: payload.sizes ?? existing.sizes,
      price: payload.price ?? existing.price,
      cost: payload.cost ?? existing.cost,
      quantity: payload.quantity ?? existing.quantity,
      status: payload.status != null ? CatalogItemStatus.parse(payload.status) : existing.status,
      isAvailable: existing.isAvailable,
      createdAtUtc: existing.createdAtUtc,
      fabric: payload.fabric ?? existing.fabric,
      style: payload.style ?? existing.style,
      imageUrl: payload.imageUrl ?? existing.imageUrl,
      sku: payload.sku ?? existing.sku,
      description: payload.description ?? existing.description,
      metadata: existing.metadata,
      tags: existing.tags,
    );

    pool[index] = updated;
    return updated;
  }

  @override
  Future<void> deleteProduct(String id) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    pool.removeWhere((piece) => piece.id == id);
  }

  @override
  Future<List<OutfitComposition>> getLookbooks({String? occasion, String? query}) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _lookbooksPool ??= _buildLookbooksPool();
    var list = pool.toList();

    if (occasion != null && occasion.trim().isNotEmpty && occasion.toLowerCase() != 'all') {
      final occ = occasion.trim().toLowerCase();
      list = list.where((l) => l.occasion.toLowerCase().contains(occ)).toList();
    }

    if (query != null && query.trim().isNotEmpty) {
      final q = query.trim().toLowerCase();
      list = list
          .where((l) =>
              l.name.toLowerCase().contains(q) ||
              l.styleNotes.toLowerCase().contains(q) ||
              l.items.any((it) => it.name.toLowerCase().contains(q)))
          .toList();
    }

    return list;
  }

  @override
  Future<OutfitComposition> composeOutfit(ComposeOutfitPayload payload) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final lookbooks = _lookbooksPool ??= _buildLookbooksPool();

    final hero = pool.firstWhere(
      (p) => p.id == payload.primaryItemId,
      orElse: () => pool.first,
    );

    final otherPieces = pool.where((p) => p.id != hero.id).toList();
    final compPieces = otherPieces.take(2).toList();

    final items = <OutfitItem>[
      OutfitItem(
        id: 'oi-${DateTime.now().millisecondsSinceEpoch}-1',
        itemId: hero.id,
        name: hero.name,
        category: hero.category,
        price: hero.price,
        imageUrl: hero.imageUrl ?? '',
        position: 'top',
        notes: 'Primary anchor piece',
      ),
    ];

    if (compPieces.isNotEmpty) {
      items.add(
        OutfitItem(
          id: 'oi-${DateTime.now().millisecondsSinceEpoch}-2',
          itemId: compPieces[0].id,
          name: compPieces[0].name,
          category: compPieces[0].category,
          price: compPieces[0].price,
          imageUrl: compPieces[0].imageUrl ?? '',
          position: 'accessory',
          notes: 'Coordinated embellishment pairing',
        ),
      );
    }

    if (compPieces.length > 1) {
      items.add(
        OutfitItem(
          id: 'oi-${DateTime.now().millisecondsSinceEpoch}-3',
          itemId: compPieces[1].id,
          name: compPieces[1].name,
          category: compPieces[1].category,
          price: compPieces[1].price,
          imageUrl: compPieces[1].imageUrl ?? '',
          position: 'drape',
          notes: 'Complementary color & fabric drape',
        ),
      );
    }

    final total = items.fold<double>(0.0, (sum, i) => sum + i.price);
    final occasion = payload.occasion?.trim().isNotEmpty == true
        ? payload.occasion!
        : 'Ceremonial & Evening';

    final composition = OutfitComposition(
      id: 'lookbook-${DateTime.now().millisecondsSinceEpoch}',
      name: payload.name.trim().isNotEmpty
          ? payload.name
          : '$occasion - ${hero.color} Look',
      occasion: occasion,
      totalPrice: total,
      styleNotes: payload.notes?.trim().isNotEmpty == true
          ? payload.notes!
          : 'Elle suggests pairing "${hero.name}" with accent drapes to accentuate the silhouette for $occasion. Handcrafted jewelry balances the visual harmony.',
      heroImageUrl: hero.imageUrl ?? '',
      createdAtUtc: DateTime.now(),
      items: items,
      organizationId: 'demo-org-1',
    );

    lookbooks.insert(0, composition);
    return composition;
  }

  @override
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _lookbooksPool ??= _buildLookbooksPool();
    final index = pool.indexWhere((l) => l.id == id);
    if (index < 0) {
      throw StateError('No demo lookbook with id $id.');
    }

    final existing = pool[index];
    final updated = existing.copyWith(
      name: payload.name ?? existing.name,
      occasion: payload.occasion ?? existing.occasion,
      styleNotes: payload.styleNotes ?? existing.styleNotes,
    );

    pool[index] = updated;
    return updated;
  }

  @override
  Future<void> deleteLookbook(String id) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _lookbooksPool ??= _buildLookbooksPool();
    pool.removeWhere((l) => l.id == id);
  }

  @override
  Future<List<SourcingRequest>> getSourcingRequests({String? status, String? query}) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _sourcingPool ??= _buildSourcingPool();
    var list = pool.toList();

    if (status != null && status.trim().isNotEmpty && status.toLowerCase() != 'all') {
      final targetStatus = SourcingStatus.fromWire(status);
      list = list.where((t) => t.status == targetStatus).toList();
    }

    if (query != null && query.trim().isNotEmpty) {
      final q = query.trim().toLowerCase();
      list = list.where((t) {
        return t.clientName.toLowerCase().contains(q) ||
            t.category.toLowerCase().contains(q) ||
            t.color.toLowerCase().contains(q) ||
            t.itemDescription.toLowerCase().contains(q) ||
            t.supplierName.toLowerCase().contains(q);
      }).toList();
    }

    return list;
  }

  @override
  Future<SourcingRequest> createSourcingRequest(CreateSourcingRequestPayload payload) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _sourcingPool ??= _buildSourcingPool();
    final suppliers = _suppliersPool ??= _buildSuppliersPool();
    final supplier = suppliers.firstWhere(
      (s) => s.id == payload.supplierId,
      orElse: () => suppliers.first,
    );

    final cost = payload.estimatedCost;
    final target = payload.targetPrice;
    final markup = cost > 0 ? (target - cost) / cost : 1.0;

    final newTicket = SourcingRequest(
      id: 'src-${DateTime.now().millisecondsSinceEpoch}',
      clientName: payload.clientName.trim(),
      category: payload.category,
      color: payload.color.trim().isNotEmpty ? payload.color.trim() : 'Custom Hue',
      itemDescription: payload.description.trim().isNotEmpty
          ? payload.description.trim()
          : 'Bespoke design commission.',
      referenceImageUrl: payload.referenceImageUrl ??
          'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
      supplierId: supplier.id,
      supplierName: supplier.name,
      estimatedCost: cost,
      proposedMarkup: double.parse(markup.toStringAsFixed(2)),
      targetPrice: target,
      status: SourcingStatus.pending,
      createdAt: DateTime.now(),
      urgency: payload.urgency,
    );

    pool.insert(0, newTicket);
    return newTicket;
  }

  @override
  Future<SourcingRequest> updateSourcingStatus(String id, SourcingStatus status, {String? notes}) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _sourcingPool ??= _buildSourcingPool();
    final index = pool.indexWhere((t) => t.id == id);
    if (index < 0) {
      throw StateError('No demo sourcing ticket with id $id.');
    }

    final existing = pool[index];
    final updated = existing.copyWith(
      status: status,
      itemDescription: notes != null && notes.trim().isNotEmpty
          ? '${existing.itemDescription}\n[Update]: ${notes.trim()}'
          : existing.itemDescription,
    );

    pool[index] = updated;
    return updated;
  }

  @override
  Future<List<Supplier>> getSuppliers() async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    return (_suppliersPool ??= _buildSuppliersPool()).toList();
  }

  @override
  Future<CatalogSaleReceipt> recordSale({
    required String itemId,
    required RecordSalePayload payload,
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final index = pool.indexWhere((p) => p.id == itemId);
    if (index < 0) {
      throw StateError('No demo piece with id $itemId.');
    }

    final piece = pool[index];
    if (payload.quantity > piece.quantity) {
      throw StateError('insufficient-stock');
    }

    final newQuantity = piece.quantity - payload.quantity;
    final newStatus = newQuantity <= 0
        ? CatalogItemStatus.soldOut
        : piece.status;

    final updated = piece.copyWith(
      quantity: newQuantity,
      status: newStatus,
      isAvailable: newQuantity > 0 && newStatus == CatalogItemStatus.available,
    );
    pool[index] = updated;

    final total = payload.unitPrice * payload.quantity;
    final receipt = CatalogSaleReceipt(
      itemId: piece.id,
      itemName: piece.name,
      sku: piece.sku,
      quantitySold: payload.quantity,
      unitPrice: payload.unitPrice,
      totalAmount: total,
      remainingStock: newQuantity,
      status: newStatus,
      ledgerEntryId: 'ledger-tx-${DateTime.now().millisecondsSinceEpoch}',
      recordedAtUtc: DateTime.now().toUtc(),
    );

    return receipt;
  }

  @override
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final index = pool.indexWhere((p) => p.id == itemId);
    if (index < 0) {
      throw StateError('No demo piece with id $itemId.');
    }

    final piece = pool[index];
    final targetStatus = status ??
        (quantity <= 0 ? CatalogItemStatus.soldOut : piece.status);

    final updated = piece.copyWith(
      quantity: quantity,
      status: targetStatus,
      isAvailable: quantity > 0 && targetStatus == CatalogItemStatus.available,
    );
    pool[index] = updated;

    return updated;
  }

  final Map<String, List<CustomerMatch>> _matchesPool = {};

  @override
  Future<List<CustomerMatch>> getCustomerMatches(String itemId) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    return _matchesPool.putIfAbsent(itemId, () => _buildDefaultMatches(itemId));
  }

  @override
  Future<List<CustomerMatch>> generateCustomerMatches(String itemId) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final fresh = _buildDefaultMatches(itemId);
    _matchesPool[itemId] = fresh;
    return fresh;
  }

  @override
  Future<void> markMatchActed(String matchId) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    for (final list in _matchesPool.values) {
      final index = list.indexWhere((m) => m.id == matchId);
      if (index >= 0) {
        list[index] = list[index].copyWith(employeeActed: true);
        break;
      }
    }
  }

  @override
  Future<List<SupplierCatalogItem>> getSupplierCatalog(String supplierId) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final suppliers = _suppliersPool ??= _buildSuppliersPool();
    final match = suppliers.firstWhere(
      (s) => s.id == supplierId,
      orElse: () => suppliers.first,
    );
    return match.catalogItems;
  }

  @override
  Future<List<CatalogTag>> fetchTags() async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }
    return demoCatalogTags();
  }

  List<CustomerMatch> _buildDefaultMatches(String itemId) {
    return [
      CustomerMatch(
        id: 'match-1-$itemId',
        customerId: 'cust-101',
        customerName: 'Maya Lin',
        customerEmail: 'maya.lin@vip.aveline.com',
        customerAvatar: 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=200&q=80',
        matchConfidence: 0.94,
        matchReason: 'Client frequently orders emerald raw silk in size 38 with gold zari embroidery.',
        preferredColor: 'Emerald Green',
        preferredFabric: 'Raw Silk',
        preferredSize: '38',
        employeeActed: false,
      ),
      CustomerMatch(
        id: 'match-2-$itemId',
        customerId: 'cust-102',
        customerName: 'Priya Sharma',
        customerEmail: 'priya.sharma@vip.aveline.com',
        customerAvatar: 'https://images.unsplash.com/photo-1517841905240-472988babdf9?auto=format&fit=crop&w=200&q=80',
        matchConfidence: 0.89,
        matchReason: 'Active luxury profile favoring celebratory festive palettes and handloom weaves.',
        preferredColor: 'Burgundy',
        preferredFabric: 'Mulberry Silk',
        preferredSize: '40',
        employeeActed: false,
      ),
      CustomerMatch(
        id: 'match-3-$itemId',
        customerId: 'cust-103',
        customerName: 'Elena Rostova',
        customerEmail: 'elena.rostova@vip.aveline.com',
        customerAvatar: 'https://images.unsplash.com/photo-1524504388940-b1c1722653e1?auto=format&fit=crop&w=200&q=80',
        matchConfidence: 0.82,
        matchReason: 'Previous buyer of curated evening drapes and bespoke regal gowns.',
        preferredColor: 'Midnight Blue',
        preferredFabric: 'Organza',
        preferredSize: '38',
        employeeActed: false,
      ),
    ];
  }

  List<Supplier> _buildSuppliersPool() {
    return const [
      Supplier(
        id: 'sup-1',
        name: 'Varanasi Silk Works',
        location: 'Varanasi, Uttar Pradesh',
        specialty: 'Pure Katan Silk & Brocade',
        rating: 4.9,
        contactEmail: 'atelier@varanasi.craft',
        phone: '+91 542 228 9011',
        leadTimeDays: 21,
        minimumOrder: 850.0,
        isActive: true,
        sampleCatalogCount: 3,
        catalogItems: [
          SupplierCatalogItem(
            id: 'ws-1',
            name: 'Imperial Katan Brocade Saree',
            category: 'Sarees',
            fabric: 'Pure Katan Silk',
            wholesalePrice: 650.0,
            imageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=600&q=80',
          ),
          SupplierCatalogItem(
            id: 'ws-2',
            name: 'Gold Zari Handloom Dupatta',
            category: 'Drapes & Shawls',
            fabric: 'Raw Silk',
            wholesalePrice: 280.0,
            imageUrl: 'https://images.unsplash.com/photo-1609357605129-26f69add5d6e?auto=format&fit=crop&w=600&q=80',
          ),
          SupplierCatalogItem(
            id: 'ws-3',
            name: 'Crimson Velvet Bridal Border',
            category: 'Accessories',
            fabric: 'Micro Velvet',
            wholesalePrice: 190.0,
            imageUrl: 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=600&q=80',
          ),
        ],
      ),
      Supplier(
        id: 'sup-2',
        name: 'Jaipur Royal Handlooms',
        location: 'Jaipur, Rajasthan',
        specialty: 'Gota Patti & Bandhani Zari',
        rating: 4.8,
        contactEmail: 'orders@jaipurcrafts.art',
        phone: '+91 141 400 3322',
        leadTimeDays: 14,
        minimumOrder: 600.0,
        isActive: true,
        sampleCatalogCount: 2,
        catalogItems: [
          SupplierCatalogItem(
            id: 'ws-4',
            name: 'Hand-dyed Bandhani Silk Lehenga Panel',
            category: 'Lehengas',
            fabric: 'Georgette Silk',
            wholesalePrice: 520.0,
            imageUrl: 'https://images.unsplash.com/photo-1595777457583-95e059d581b8?auto=format&fit=crop&w=600&q=80',
          ),
          SupplierCatalogItem(
            id: 'ws-5',
            name: 'Gota Patti Embroidered Kurti Set',
            category: 'Kurtas & Tunics',
            fabric: 'Chanderi Silk',
            wholesalePrice: 340.0,
            imageUrl: 'https://images.unsplash.com/photo-1566174053879-31528523f8ae?auto=format&fit=crop&w=600&q=80',
          ),
        ],
      ),
      Supplier(
        id: 'sup-3',
        name: 'Surat Embroideries & Zari',
        location: 'Surat, Gujarat',
        specialty: 'Heavy Threadwork & Mirror Craft',
        rating: 4.7,
        contactEmail: 'concierge@suratzari.in',
        phone: '+91 261 289 1144',
        leadTimeDays: 10,
        minimumOrder: 450.0,
        isActive: true,
        sampleCatalogCount: 2,
        catalogItems: [
          SupplierCatalogItem(
            id: 'ws-6',
            name: 'Mirrorwork Silk Choli',
            category: 'Silk Blouses',
            fabric: 'Raw Silk',
            wholesalePrice: 220.0,
            imageUrl: 'https://images.unsplash.com/photo-1518049362265-d5b2a6467637?auto=format&fit=crop&w=600&q=80',
          ),
          SupplierCatalogItem(
            id: 'ws-7',
            name: 'Threadwork Evening Stole',
            category: 'Drapes & Shawls',
            fabric: 'Organza',
            wholesalePrice: 180.0,
            imageUrl: 'https://images.unsplash.com/photo-1601924994987-69e26d50dc26?auto=format&fit=crop&w=600&q=80',
          ),
        ],
      ),
      Supplier(
        id: 'sup-4',
        name: 'Kanchipuram Heritage Guild',
        location: 'Kanchipuram, Tamil Nadu',
        specialty: 'Pure Gold Zari Weaving',
        rating: 5.0,
        contactEmail: 'guild@kanchipuram.gov',
        phone: '+91 44 2722 0910',
        leadTimeDays: 28,
        minimumOrder: 1200.0,
        isActive: true,
        sampleCatalogCount: 2,
        catalogItems: [
          SupplierCatalogItem(
            id: 'ws-8',
            name: 'Temple Border Pure Kanchipuram Saree',
            category: 'Sarees',
            fabric: 'Mulberry Silk',
            wholesalePrice: 950.0,
            imageUrl: 'https://images.unsplash.com/photo-1617627143750-d86bc21e42bb?auto=format&fit=crop&w=600&q=80',
          ),
          SupplierCatalogItem(
            id: 'ws-9',
            name: 'Coromandel Gold Zari Dupatta',
            category: 'Drapes & Shawls',
            fabric: 'Heavy Silk',
            wholesalePrice: 480.0,
            imageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=600&q=80',
          ),
        ],
      ),
    ];
  }

  List<SourcingRequest> _buildSourcingPool() {
    return [
      SourcingRequest(
        id: 'src-1',
        clientName: 'Mrs. Radhika Merchant',
        category: 'Lehengas',
        color: 'Peacock Emerald',
        itemDescription: 'Custom bridal lehenga with antique gold zardozi embroidery and custom monogram drape.',
        referenceImageUrl: 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
        supplierId: 'sup-1',
        supplierName: 'Varanasi Silk Works',
        estimatedCost: 950.0,
        targetPrice: 2000.0,
        proposedMarkup: 1.11,
        status: SourcingStatus.pending,
        createdAt: DateTime.now().subtract(const Duration(hours: 4)),
        urgency: 'high',
      ),
      SourcingRequest(
        id: 'src-2',
        clientName: 'Devraj Rajput',
        category: 'Sherwanis',
        color: 'Ivory Cream',
        itemDescription: 'Raw silk tailored sherwani with hand-crafted pearl button placket and brocade stole.',
        referenceImageUrl: 'https://images.unsplash.com/photo-1594938298603-c8148c4dae35?auto=format&fit=crop&w=800&q=80',
        supplierId: 'sup-2',
        supplierName: 'Jaipur Royal Handlooms',
        estimatedCost: 650.0,
        targetPrice: 1450.0,
        proposedMarkup: 1.23,
        status: SourcingStatus.quoted,
        createdAt: DateTime.now().subtract(const Duration(days: 1)),
        urgency: 'medium',
      ),
      SourcingRequest(
        id: 'src-3',
        clientName: 'Tara Singhania',
        category: 'Anarkalis',
        color: 'Royal Midnight Blue',
        itemDescription: 'Layered organza floor-length silhouette with hand-embroidered sequin border.',
        referenceImageUrl: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
        supplierId: 'sup-3',
        supplierName: 'Surat Embroideries & Zari',
        estimatedCost: 400.0,
        targetPrice: 950.0,
        proposedMarkup: 1.38,
        status: SourcingStatus.approved,
        createdAt: DateTime.now().subtract(const Duration(days: 2)),
        urgency: 'medium',
      ),
      SourcingRequest(
        id: 'src-4',
        clientName: 'Ananya Verma',
        category: 'Sarees',
        color: 'Blush Coral Pink',
        itemDescription: 'Heavy bridal Banarasi drape with kadwa weaving technique and tissue border.',
        referenceImageUrl: 'https://images.unsplash.com/photo-1617627143750-d86bc21e42bb?auto=format&fit=crop&w=800&q=80',
        supplierId: 'sup-4',
        supplierName: 'Kanchipuram Heritage Guild',
        estimatedCost: 1100.0,
        targetPrice: 2600.0,
        proposedMarkup: 1.36,
        status: SourcingStatus.ordered,
        createdAt: DateTime.now().subtract(const Duration(days: 3)),
        urgency: 'urgent',
      ),
      SourcingRequest(
        id: 'src-5',
        clientName: 'Mira Kapoor',
        category: 'Bandhgalas',
        color: 'Imperial Crimson',
        itemDescription: 'Heritage velvet bandhgala jacket with bespoke gold bullion crest.',
        referenceImageUrl: 'https://images.unsplash.com/photo-1593030761757-71fae45fa0e7?auto=format&fit=crop&w=800&q=80',
        supplierId: 'sup-2',
        supplierName: 'Jaipur Royal Handlooms',
        estimatedCost: 750.0,
        targetPrice: 1600.0,
        proposedMarkup: 1.13,
        status: SourcingStatus.fulfilled,
        createdAt: DateTime.now().subtract(const Duration(days: 5)),
        urgency: 'low',
      ),
      SourcingRequest(
        id: 'src-6',
        clientName: 'Pooja Oberoi',
        category: 'Drapes & Shawls',
        color: 'Deep Ruby',
        itemDescription: 'Pure pashmina shawl with reversible sozni embroidery work.',
        referenceImageUrl: 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
        supplierId: 'sup-1',
        supplierName: 'Varanasi Silk Works',
        estimatedCost: 500.0,
        targetPrice: 1100.0,
        proposedMarkup: 1.20,
        status: SourcingStatus.archived,
        createdAt: DateTime.now().subtract(const Duration(days: 7)),
        urgency: 'low',
      ),
    ];
  }

  List<OutfitComposition> _buildLookbooksPool() {
    return [
      OutfitComposition(
        id: 'lb-1',
        name: 'Emerald Heritage Sangeet Look',
        occasion: 'Sangeet & Reception',
        totalPrice: 79000.0,
        styleNotes:
            'Elle suggests pairing the rich emerald silk weave with antique temple gold and warm champagne accents for evening chandeliers.',
        heroImageUrl:
            'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
        createdAtUtc: DateTime.now().subtract(const Duration(days: 2)),
        items: const [
          OutfitItem(
            id: 'oi-1',
            itemId: 'piece-001',
            name: 'Imperial Emerald Brocade Saree',
            category: 'Sarees',
            price: 45000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
            position: 'top',
            notes: 'Primary anchor statement piece',
          ),
          OutfitItem(
            id: 'oi-2',
            itemId: 'piece-002',
            name: 'Heritage Kundan & Pearl Choker Set',
            category: 'Jewelry',
            price: 22000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1599643478518-a784e5dc4c8f?auto=format&fit=crop&w=800&q=80',
            position: 'accessory',
            notes: 'Antique gold & un-cut polki stone accent',
          ),
          OutfitItem(
            id: 'oi-3',
            itemId: 'piece-003',
            name: 'Champagne Tissue Zari Dupatta',
            category: 'Drapes',
            price: 12000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
            position: 'drape',
            notes: 'Lightweight metallic shimmer drape',
          ),
        ],
        organizationId: 'demo-org-1',
      ),
      OutfitComposition(
        id: 'lb-2',
        name: 'Imperial Crimson Bridal Heirloom',
        occasion: 'Bridal Heirloom',
        totalPrice: 190000.0,
        styleNotes:
            'A ceremonial bridal silhouette anchored by intricate gold zari threadwork and handcrafted velvet border detailing.',
        heroImageUrl:
            'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
        createdAtUtc: DateTime.now().subtract(const Duration(days: 5)),
        items: const [
          OutfitItem(
            id: 'oi-4',
            itemId: 'piece-004',
            name: 'Crimson Zari Brocade Bridal Lehenga',
            category: 'Lehengas',
            price: 120000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
            position: 'top',
            notes: 'Heavy bridal ceremonial lehenga',
          ),
          OutfitItem(
            id: 'oi-5',
            itemId: 'piece-005',
            name: 'Polki & Emerald Royal Bridal Haar',
            category: 'Jewelry',
            price: 45000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1599643478518-a784e5dc4c8f?auto=format&fit=crop&w=800&q=80',
            position: 'accessory',
            notes: 'Layered royal neckline embellishment',
          ),
          OutfitItem(
            id: 'oi-6',
            itemId: 'piece-006',
            name: 'Hand-Embroidered Velvet Second Veil',
            category: 'Drapes',
            price: 25000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
            position: 'drape',
            notes: 'Contrasting bridal head veil',
          ),
        ],
        organizationId: 'demo-org-1',
      ),
      OutfitComposition(
        id: 'lb-3',
        name: 'Maharaja Ivory Sherwani Set',
        occasion: 'Groom Royal Wedding',
        totalPrice: 131000.0,
        styleNotes:
            'Regal ivory raw silk tailoring with understated gold accents, paired with crimson silk safa and emerald mala.',
        heroImageUrl:
            'https://images.unsplash.com/photo-1507679799987-c73779587ccf?auto=format&fit=crop&w=800&q=80',
        createdAtUtc: DateTime.now().subtract(const Duration(days: 8)),
        items: const [
          OutfitItem(
            id: 'oi-7',
            itemId: 'piece-007',
            name: 'Ivory Raw Silk Embroidered Sherwani',
            category: 'Menswear',
            price: 85000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1507679799987-c73779587ccf?auto=format&fit=crop&w=800&q=80',
            position: 'top',
            notes: 'Hand-embroidered groom sherwani',
          ),
          OutfitItem(
            id: 'oi-8',
            itemId: 'piece-008',
            name: 'Emerald Beads Multi-Strand Mala',
            category: 'Jewelry',
            price: 28000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1599643478518-a784e5dc4c8f?auto=format&fit=crop&w=800&q=80',
            position: 'accessory',
            notes: 'Royal layered neck mala',
          ),
          OutfitItem(
            id: 'oi-9',
            itemId: 'piece-009',
            name: 'Crimson Silk Safa & Stole Set',
            category: 'Accessories',
            price: 18000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
            position: 'drape',
            notes: 'Groom headwear and ceremonial stole',
          ),
        ],
        organizationId: 'demo-org-1',
      ),
      OutfitComposition(
        id: 'lb-4',
        name: 'Midnight Velvet Gala Gown',
        occasion: 'Cocktail Reception & Gala',
        totalPrice: 92000.0,
        styleNotes:
            'Modern architectural silhouette with deep midnight lustre, accentuated by statement sapphire and diamond drops.',
        heroImageUrl:
            'https://images.unsplash.com/photo-1566174053879-31528523f8ae?auto=format&fit=crop&w=800&q=80',
        createdAtUtc: DateTime.now().subtract(const Duration(days: 10)),
        items: const [
          OutfitItem(
            id: 'oi-10',
            itemId: 'piece-010',
            name: 'Midnight Sculpted Velvet Gown',
            category: 'Gowns',
            price: 58000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1566174053879-31528523f8ae?auto=format&fit=crop&w=800&q=80',
            position: 'top',
            notes: 'Sculpted drape evening gown',
          ),
          OutfitItem(
            id: 'oi-11',
            itemId: 'piece-011',
            name: 'Sapphire & Diamond Solitaire Drops',
            category: 'Jewelry',
            price: 34000.0,
            imageUrl:
                'https://images.unsplash.com/photo-1599643478518-a784e5dc4c8f?auto=format&fit=crop&w=800&q=80',
            position: 'accessory',
            notes: 'Statement earrings',
          ),
        ],
        organizationId: 'demo-org-1',
      ),
    ];
  }

  List<CatalogProduct> _filter(
    List<CatalogProduct> all,
    CatalogProductQuery query,
  ) {
    final search = query.search.trim().toLowerCase();
    return all.where((product) {
      if (search.isNotEmpty && !_matchesSearch(product, search)) {
        return false;
      }
      if (query.tagIds.isNotEmpty && !product.tags.any(query.tagIds.contains)) {
        return false;
      }
      return _matchesFilters(product, query.filters);
    }).toList();
  }

  bool _matchesSearch(CatalogProduct product, String search) {
    final haystack = <String>[
      product.name,
      product.category,
      product.color,
      product.fabric ?? '',
      product.style ?? '',
      product.sku ?? '',
      product.sourceLabel,
      product.description ?? '',
      ...product.sizes,
    ].join(' ').toLowerCase();
    return haystack.contains(search);
  }

  bool _matchesFilters(CatalogProduct product, CatalogFilters filters) {
    final availability = filters.selectedIn(CatalogFilterGroup.availability);
    if (availability.isNotEmpty &&
        !availability.contains(product.status.label)) {
      return false;
    }

    final categories = filters.selectedIn(CatalogFilterGroup.category);
    if (categories.isNotEmpty && !categories.contains(product.category)) {
      return false;
    }

    final fabrics = filters.selectedIn(CatalogFilterGroup.fabric);
    if (fabrics.isNotEmpty && !fabrics.contains(product.fabric)) {
      return false;
    }

    final sizes = filters.selectedIn(CatalogFilterGroup.size);
    if (sizes.isNotEmpty && !product.sizes.any(sizes.contains)) {
      return false;
    }

    final bands = filters.selectedIn(CatalogFilterGroup.price);
    if (bands.isNotEmpty &&
        !bands.any((band) => _inBand(product.price, band))) {
      return false;
    }

    return true;
  }

  static bool _inBand(double price, String band) => switch (band) {
    'Under 25k' => price < 25000,
    '25k - 75k' => price >= 25000 && price < 75000,
    '75k - 150k' => price >= 75000 && price <= 150000,
    'Over 150k' => price > 150000,
    _ => true,
  };

  static List<CatalogProduct> _buildPool() {
    return [
      for (var i = 0; i < _pieces.length; i++) _productFor(i, _pieces[i]),
    ];
  }

  static CatalogProduct _productFor(int index, _Piece piece) {
    final price = 18500.0 + (index % 12) * 12000 + (index % 3) * 1500;
    final status = _statuses[index % _statuses.length];
    // Walked in fives so the range reads like a real policy rather than a
    // single number repeated.
    final discountMin = 5 + (index % 3) * 5;

    return CatalogProduct(
      id: 'piece-${(index + 1).toString().padLeft(3, '0')}',
      organizationId: 'org-demo',
      name: piece.name,
      category: piece.category,
      color: _colors[index % _colors.length],
      sizes: _sizes[index % _sizes.length],
      price: price,
      // A mark-up that varies by piece rather than one flat multiplier.
      cost: price * (0.42 + (index % 5) * 0.06),
      quantity: _quantityFor(index, status),
      status: status,
      isAvailable: status == CatalogItemStatus.available,
      createdAtUtc: DateTime.utc(
        2026,
        9,
        17,
      ).subtract(Duration(days: index * 3)),
      fabric: piece.fabric,
      style: _styles[index % _styles.length],
      sku:
          'AVL-${(index + 1).toString().padLeft(4, '0')}-'
          '${piece.category.substring(0, 1).toUpperCase()}',
      description: piece.description,
      sourcedFrom: _sources[index % _sources.length],
      discountMinPercent: discountMin,
      discountMaxPercent: discountMin + 10,
      metadata: {
        'Care': _care[index % _care.length],
        'Origin': _origins[index % _origins.length],
        'Batch':
            'B-${2026 - index % 3}-'
            '${(index + 1).toString().padLeft(3, '0')}',
      },
      tags: _tags[index % _tags.length],
    );
  }

  static int _quantityFor(int index, CatalogItemStatus status) {
    return switch (status) {
      // Every fourth available piece is down to its last one or two, so the
      // grid carries low-stock pieces without inventing a separate pool.
      CatalogItemStatus.available => index % 4 == 0 ? 1 : 3 + (index % 9),
      CatalogItemStatus.onHold => index % 3,
      CatalogItemStatus.unavailable => index % 2,
      CatalogItemStatus.soldOut || CatalogItemStatus.archived => 0,
    };
  }

  static const List<CatalogItemStatus> _statuses = [
    CatalogItemStatus.available,
    CatalogItemStatus.available,
    CatalogItemStatus.available,
    CatalogItemStatus.onHold,
    CatalogItemStatus.available,
    CatalogItemStatus.soldOut,
    CatalogItemStatus.available,
    CatalogItemStatus.unavailable,
    CatalogItemStatus.available,
    CatalogItemStatus.archived,
  ];

  static const List<String> _colors = [
    'Wine',
    'Rose quartz',
    'Champagne',
    'Emerald',
    'Midnight',
    'Ivory',
    'Saffron',
    'Lilac',
  ];

  static const List<List<String>> _sizes = [
    ['36', '38', '40', '42'],
    ['S', 'M', 'L'],
    ['38', '40', 'Custom'],
    ['One size'],
    ['34', '36', '38'],
  ];

  static const List<String> _styles = [
    'Contemporary festive',
    'Bridal classic',
    'Everyday luxury',
    'Evening',
    'Resort',
  ];

  static const List<String> _sources = [
    'Varanasi Weavers',
    'Handloom Silk Mill, Kandy',
    'Colombo Atelier',
    'Jaipur Block Prints',
    'Kandy Loom House',
  ];

  static const List<String> _origins = ['Sri Lanka', 'India', 'Italy'];

  static const List<String> _care = [
    'Dry clean only',
    'Hand wash cold',
    'Dry clean recommended',
  ];

  static const List<Set<String>> _tags = [
    {'new-in', 'bridal'},
    {'festive', 'handloom'},
    {'evening', 'bestsellers'},
    {'raw-silk', 'atelier-pick'},
    {'bridal', 'festive'},
    {'new-in', 'under-25k'},
    {'handloom', 'atelier-pick'},
    {'evening', 'reserved'},
  ];
}

/// The name, category, fabric and copy of one demo piece.
typedef _Piece = ({
  String name,
  String category,
  String fabric,
  String description,
});

const List<_Piece> _pieces = [
  (
    name: 'Handloom Silk Saree',
    category: 'Sarees',
    fabric: 'Raw silk',
    description:
        'Handwoven raw silk with a fine zari border, finished in the atelier.',
  ),
  (
    name: 'Kanjivaram Zari Saree',
    category: 'Sarees',
    fabric: 'Raw silk',
    description: 'A temple-border Kanjivaram for weddings and long evenings.',
  ),
  (
    name: 'Georgette Party Saree',
    category: 'Sarees',
    fabric: 'Chiffon',
    description: 'Feather-light georgette with a scattered sequin pallu.',
  ),
  (
    name: 'Organza Sequin Saree',
    category: 'Sarees',
    fabric: 'Organza',
    description: 'Crisp organza carrying a hand-set sequin field.',
  ),
  (
    name: 'Embroidered Velvet Lehenga',
    category: 'Lehengas',
    fabric: 'Velvet',
    description: 'Deep velvet with tonal thread work across the skirt.',
  ),
  (
    name: 'Chikankari Lehenga',
    category: 'Lehengas',
    fabric: 'Chiffon',
    description: 'Lucknowi chikankari on a soft chiffon base.',
  ),
  (
    name: 'Bridal Velvet Lehenga',
    category: 'Lehengas',
    fabric: 'Velvet',
    description: 'A bridal set with zardozi bodice and a full circular skirt.',
  ),
  (
    name: 'Pastel Tulle Lehenga',
    category: 'Lehengas',
    fabric: 'Organza',
    description: 'Layered tulle in a soft pastel, cut for movement.',
  ),
  (
    name: 'Column Evening Gown',
    category: 'Gowns',
    fabric: 'Chiffon',
    description: 'A clean column line with a draped shoulder.',
  ),
  (
    name: 'Draped Satin Gown',
    category: 'Gowns',
    fabric: 'Raw silk',
    description: 'Bias-cut satin that falls into a soft cowl at the back.',
  ),
  (
    name: 'Velvet Cocktail Gown',
    category: 'Gowns',
    fabric: 'Velvet',
    description: 'A short velvet gown with a sculpted neckline.',
  ),
  (
    name: 'Beaded Cape Gown',
    category: 'Gowns',
    fabric: 'Organza',
    description: 'Sheer cape overlay with hand-beaded shoulders.',
  ),
  (
    name: 'Silk Trench Coat',
    category: 'Outerwear',
    fabric: 'Raw silk',
    description: 'A light trench in raw silk, unlined for the tropics.',
  ),
  (
    name: 'Handloom Cotton Jacket',
    category: 'Outerwear',
    fabric: 'Handloom cotton',
    description: 'Structured handloom cotton with a single-button close.',
  ),
  (
    name: 'Embroidered Cape',
    category: 'Outerwear',
    fabric: 'Organza',
    description: 'An open organza cape with a fine embroidered edge.',
  ),
  (
    name: 'Handloom Cotton Coat',
    category: 'Outerwear',
    fabric: 'Handloom cotton',
    description: 'A relaxed coat in undyed handloom cotton.',
  ),
  (
    name: 'Puff Sleeve Silk Blouse',
    category: 'Silk blouses',
    fabric: 'Raw silk',
    description: 'A puff-sleeve blouse cut to sit under a heavy saree.',
  ),
  (
    name: 'Chiffon Blouse',
    category: 'Silk blouses',
    fabric: 'Chiffon',
    description: 'A soft chiffon blouse with a covered button placket.',
  ),
  (
    name: 'Handloom Cotton Blouse',
    category: 'Silk blouses',
    fabric: 'Handloom cotton',
    description: 'Everyday handloom cotton with a high neck.',
  ),
  (
    name: 'Velvet Blouse',
    category: 'Silk blouses',
    fabric: 'Velvet',
    description: 'A velvet blouse with a deep back and tie detail.',
  ),
  (
    name: 'Zari Clutch',
    category: 'Accessories',
    fabric: 'Raw silk',
    description: 'A slim clutch woven with a fine gold zari.',
  ),
  (
    name: 'Beaded Potli Bag',
    category: 'Accessories',
    fabric: 'Velvet',
    description: 'A drawstring potli in velvet with beaded tassels.',
  ),
  (
    name: 'Silk Scarf',
    category: 'Accessories',
    fabric: 'Chiffon',
    description: 'A hand-rolled silk scarf in the season palette.',
  ),
  (
    name: 'Embroidered Belt',
    category: 'Accessories',
    fabric: 'Handloom cotton',
    description: 'A wide embroidered belt that cinches a saree drape.',
  ),
];
