import '../domain/catalog_filters.dart';
import '../domain/catalog_product.dart';
import '../domain/customer_match.dart';
import '../domain/outfit_composition.dart';
import '../domain/outfit_payloads.dart';
import '../domain/product_payloads.dart';
import '../domain/sale_payloads.dart';
import '../domain/sale_receipt.dart';
import '../domain/sourcing_payloads.dart';
import '../domain/sourcing_request.dart';
import '../domain/sourcing_status.dart';
import '../domain/supplier.dart';
import '../domain/vision_analysis.dart';

/// The narrowing a page of the catalog is fetched against.
class CatalogProductQuery {
  const CatalogProductQuery({
    this.search = '',
    this.tagIds = const <String>{},
    this.filters = const CatalogFilters.none(),
  });

  /// Free text from the catalog's own search field.
  final String search;

  /// Ids of the shop tags narrowing the list.
  final Set<String> tagIds;

  /// The options chosen on the filter screen.
  final CatalogFilters filters;

  /// Whether nothing is narrowing the catalog, so the whole list is in scope.
  bool get isEmpty =>
      search.trim().isEmpty && tagIds.isEmpty && filters.isEmpty;
}

/// One page of catalog products.
class CatalogProductPage {
  const CatalogProductPage({required this.products, required this.hasMore});

  final List<CatalogProduct> products;

  /// Whether a further page exists after this one.
  final bool hasMore;
}

/// Fetches the catalog a page at a time.
///
/// Implementations are expected to be cheap to call repeatedly: the list asks
/// for the next page every time the grid approaches its end.
abstract interface class CatalogProductRepository {
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  });

  /// Fetches one piece by id, or `null` when the shop no longer carries it.
  ///
  /// The detail screen resolves through this rather than trusting the piece it
  /// was handed: the router re-parses a location on every refresh, and the
  /// `extra` a push carried does not survive that.
  Future<CatalogProduct?> fetchProduct(String id);

  /// Moves one piece to [status] and returns it as the server now holds it.
  ///
  /// The server's own row comes back rather than the local copy with a field swapped: the
  /// status vocabulary and the `isAvailable` derivation live on the API
  /// (`CatalogStatusVocabulary`), so a client that mirrored either would be a second place
  /// to keep them right. It also means a change the API refused cannot draw as though it
  /// had stuck.
  Future<CatalogProduct> updateStatus(String id, CatalogItemStatus status);

  /// Logs a sourcing request for a piece the shop does not carry, returning its id.
  ///
  /// The ticket's fields are the piece's own, so the caller names the piece and how many
  /// are wanted rather than assembling the request body itself.
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  });

  /// Staged image upload to the backend media store.
  Future<ImageUploadResult> uploadImage({
    required List<int> bytes,
    required String fileName,
    String? contentType,
  });

  /// Analyzes a product image using Elle Vision AI.
  Future<VisionAnalysis> analyzeImage({
    String? imageRefId,
    String? imageUrl,
    String? fileNameHint,
  });

  /// Creates a new catalog piece.
  Future<CatalogProduct> createProduct(CreateProductPayload payload);

  /// Updates an existing catalog piece.
  Future<CatalogProduct> updateProduct(String id, UpdateProductPayload payload);

  /// Deletes (soft-deletes) a piece from the catalog.
  Future<void> deleteProduct(String id);

  /// Fetches curated lookbooks and outfit capsules from the boutique.
  Future<List<OutfitComposition>> getLookbooks({String? occasion, String? query});

  /// Composes a styled lookbook around a primary item using Elle AI.
  Future<OutfitComposition> composeOutfit(ComposeOutfitPayload payload);

  /// Updates metadata (name, occasion, style notes) on an existing lookbook.
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload);

  /// Removes a composed lookbook from the boutique.
  Future<void> deleteLookbook(String id);

  /// Lists bespoke sourcing requests with optional status and query filtering.
  Future<List<SourcingRequest>> getSourcingRequests({String? status, String? query});

  /// Creates a new bespoke sourcing commission request ticket.
  Future<SourcingRequest> createSourcingRequest(CreateSourcingRequestPayload payload);

  /// Updates the lifecycle pipeline stage of a sourcing request.
  Future<SourcingRequest> updateSourcingStatus(String id, SourcingStatus status, {String? notes});

  /// Lists integrated partner craft ateliers and suppliers.
  Future<List<Supplier>> getSuppliers();

  /// Inspects sample wholesale catalog items from a partner supplier.
  Future<List<SupplierCatalogItem>> getSupplierCatalog(String supplierId);

  /// Records an in-store counter sale of a catalog piece and creates a journal entry.
  Future<CatalogSaleReceipt> recordSale({
    required String itemId,
    required RecordSalePayload payload,
  });

  /// Adjusts stock for a piece (e.g. reduce stock or mark out of stock).
  Future<CatalogProduct> adjustStock({
    required String itemId,
    required int quantity,
    CatalogItemStatus? status,
  });

  /// Retrieves VIP customer taste-profile affinity matches for a catalog item.
  Future<List<CustomerMatch>> getCustomerMatches(String itemId);

  /// Triggers AI recalculation of VIP customer affinity matches for an item.
  Future<List<CustomerMatch>> generateCustomerMatches(String itemId);

  /// Marks a VIP match as acted upon (concierge outreach started).
  Future<void> markMatchActed(String matchId);
}
