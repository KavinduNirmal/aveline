import 'dart:async';
import 'package:flutter/foundation.dart';

import '../data/api_search_repository.dart';

/// The lifecycle status of the search overlay.
enum SearchStatus {
  idle,
  loading,
  success,
  empty,
  error,
}

/// Reactive controller driving search input, debouncing, and result updates.
class SearchController extends ChangeNotifier {
  SearchController(this._repository);

  final SearchRepository _repository;
  Timer? _debounceTimer;
  String _query = '';
  String _scope = 'all';
  SearchStatus _status = SearchStatus.idle;
  List<SearchResultItem> _results = const [];
  String? _errorMessage;

  String get query => _query;
  String get scope => _scope;
  SearchStatus get status => _status;
  List<SearchResultItem> get results => _results;
  String? get errorMessage => _errorMessage;
  bool get isLoading => _status == SearchStatus.loading;

  void onQueryChanged(String newQuery) {
    _query = newQuery;
    _debounceTimer?.cancel();

    if (_query.trim().length < 2) {
      _results = const [];
      _status = SearchStatus.idle;
      _errorMessage = null;
      notifyListeners();
      return;
    }

    _status = SearchStatus.loading;
    notifyListeners();

    _debounceTimer = Timer(const Duration(milliseconds: 300), () {
      _executeSearch();
    });
  }

  void setScope(String newScope) {
    if (_scope == newScope) return;
    _scope = newScope;
    if (_query.trim().length >= 2) {
      _executeSearch();
    }
  }

  Future<void> _executeSearch() async {
    try {
      final items = await _repository.search(_query, scope: _scope);
      _results = items;
      _status = items.isEmpty ? SearchStatus.empty : SearchStatus.success;
      _errorMessage = null;
    } catch (e) {
      _status = SearchStatus.error;
      _errorMessage = e.toString();
    } finally {
      notifyListeners();
    }
  }

  void clear() {
    _debounceTimer?.cancel();
    _query = '';
    _results = const [];
    _status = SearchStatus.idle;
    _errorMessage = null;
    notifyListeners();
  }

  @override
  void dispose() {
    _debounceTimer?.cancel();
    super.dispose();
  }
}
