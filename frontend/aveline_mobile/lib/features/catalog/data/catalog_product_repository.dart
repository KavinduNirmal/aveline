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
}
