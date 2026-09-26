import '../domain/catalog_filters.dart';
import '../domain/catalog_product.dart';

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
}
