import 'package:flutter/foundation.dart';

import '../data/catalog_product_repository.dart';
import '../domain/catalog_product.dart';

/// Pages the catalog into the list the screen renders.
///
/// Owns the loaded pages, the in-flight guard and the end-of-catalog flag. The
/// screen owns the scroll position and asks for the next page as the grid
/// approaches its end; the controller decides whether that is a real request.
class CatalogProductsController extends ChangeNotifier {
  CatalogProductsController(this._repository, {this.pageSize = 8});

  final CatalogProductRepository _repository;
  final int pageSize;

  final List<CatalogProduct> _products = <CatalogProduct>[];
  CatalogProductQuery _query = const CatalogProductQuery();

  /// The page the next [loadMore] asks for.
  int _nextPage = 0;

  bool _isLoading = false;
  bool _hasMore = true;
  bool _hasLoadedOnce = false;
  String? _errorMessage;
  bool _disposed = false;

  /// Identifies the query the in-flight request belongs to.
  ///
  /// A slow reply for an abandoned query is dropped rather than mixed into the
  /// list: typing narrowing the search can outrun the page it started.
  int _queryId = 0;

  List<CatalogProduct> get products => List.unmodifiable(_products);
  CatalogProductQuery get query => _query;
  bool get isLoading => _isLoading;
  bool get hasMore => _hasMore;
  bool get hasLoadedOnce => _hasLoadedOnce;
  String? get errorMessage => _errorMessage;

  /// Whether the current query finished and produced nothing.
  bool get isEmpty => _hasLoadedOnce && _products.isEmpty;

  /// Whether there is a page to ask for and none is already in flight.
  bool get canLoadMore => _hasMore && !_isLoading && _errorMessage == null;

  /// Replaces the list with the first page of [query].
  ///
  /// Passing no query re-runs the one already held, which is what the error
  /// state's retry does.
  Future<void> loadFirstPage({CatalogProductQuery? query}) async {
    if (query != null) {
      _query = query;
    }

    final id = ++_queryId;
    _isLoading = true;
    _hasLoadedOnce = false;
    _errorMessage = null;
    _products.clear();
    _notify();

    await _fetchPage(page: 0, replace: true, queryId: id);
  }

  /// Appends the next page, when there is one and none is in flight.
  Future<void> loadMore() async {
    if (!canLoadMore) {
      return;
    }
    await _fetchPage(page: _nextPage, replace: false, queryId: _queryId);
  }

  /// Retries whatever failed: the first page when the list is empty, the page
  /// that was in flight otherwise.
  Future<void> retry() async {
    if (_products.isEmpty) {
      await loadFirstPage();
      return;
    }

    _errorMessage = null;
    _notify();
    await loadMore();
  }

  Future<void> _fetchPage({
    required int page,
    required bool replace,
    required int queryId,
  }) async {
    _isLoading = true;
    _errorMessage = null;
    _notify();

    try {
      final result = await _repository.fetchPage(
        page: page,
        pageSize: pageSize,
        query: _query,
      );
      if (queryId != _queryId) {
        return;
      }

      if (replace) {
        _products.clear();
      }
      _products.addAll(result.products);
      _hasMore = result.hasMore;
      _nextPage = page + 1;
      _hasLoadedOnce = true;
    } catch (error) {
      if (queryId != _queryId) {
        return;
      }
      _errorMessage = error.toString().replaceFirst('Exception: ', '');
      _hasLoadedOnce = true;
    } finally {
      if (queryId == _queryId) {
        _isLoading = false;
        _notify();
      }
    }
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }

  /// `notifyListeners` that tolerates a page landing after the screen is gone.
  void _notify() {
    if (!_disposed) {
      notifyListeners();
    }
  }
}
