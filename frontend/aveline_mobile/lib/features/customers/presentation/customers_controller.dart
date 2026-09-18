import 'package:flutter/foundation.dart';

import '../data/customer_repository.dart';
import '../domain/customer_book.dart';

/// Holds the client book the screen renders.
///
/// Owns the loaded book, the in-flight guard and the error state. The screen owns
/// the narrowing in force and asks for the book again whenever it changes; the
/// controller decides whether that is a real request and drops a reply that
/// belongs to a narrowing the associate has already moved on from.
class CustomersController extends ChangeNotifier {
  CustomersController(this._repository);

  final CustomerRepository _repository;

  CustomerBook _book = CustomerBook.empty;
  CustomerQuery _query = const CustomerQuery();

  bool _isLoading = false;
  bool _hasLoadedOnce = false;
  String? _errorMessage;
  bool _disposed = false;

  /// Identifies the narrowing the in-flight request belongs to.
  ///
  /// Typing narrows the search faster than a reply can arrive, and a book that
  /// lands late must not replace the one for the query the associate is on.
  int _queryId = 0;

  CustomerBook get book => _book;
  CustomerQuery get query => _query;
  bool get isLoading => _isLoading;
  bool get hasLoadedOnce => _hasLoadedOnce;
  String? get errorMessage => _errorMessage;

  /// Whether the current narrowing finished and matched nobody.
  bool get isEmpty => _hasLoadedOnce && _book.isEmpty;

  /// Loads the book for [query], or re-runs the one already held when none is
  /// given — which is what the error state's retry does.
  Future<void> load({CustomerQuery? query}) async {
    if (query != null) {
      _query = query;
    }

    final id = ++_queryId;
    _isLoading = true;
    _hasLoadedOnce = false;
    _errorMessage = null;
    _book = CustomerBook.empty;
    _notify();

    try {
      final book = await _repository.fetchBook(query: _query);
      if (id != _queryId) {
        return;
      }
      _book = book;
      _hasLoadedOnce = true;
    } catch (error) {
      if (id != _queryId) {
        return;
      }
      _errorMessage = error.toString().replaceFirst('Exception: ', '');
      _hasLoadedOnce = true;
    } finally {
      if (id == _queryId) {
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

  /// `notifyListeners` that tolerates a book landing after the screen is gone.
  void _notify() {
    if (!_disposed) {
      notifyListeners();
    }
  }
}
