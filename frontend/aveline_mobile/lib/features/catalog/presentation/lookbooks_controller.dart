import 'package:flutter/foundation.dart';

import '../data/catalog_product_repository.dart';
import '../domain/outfit_composition.dart';
import '../domain/outfit_payloads.dart';

/// Preset ceremonial occasion filter options for lookbooks.
const List<String> lookbookOccasions = [
  'All',
  'Sangeet & Reception',
  'Groom Royal Wedding',
  'Bridal Heirloom',
  'Cocktail Reception & Gala',
  'Festive Evening Soirée',
];

/// Controller managing lookbook collection state, filtering, composition, updates, and deletion.
class LookbooksController extends ChangeNotifier {
  LookbooksController(this._repository);

  final CatalogProductRepository _repository;

  List<OutfitComposition> _lookbooks = const [];
  bool _isLoading = false;
  String? _error;
  String _selectedOccasion = 'All';
  String _searchQuery = '';

  List<OutfitComposition> get lookbooks => _lookbooks;
  bool get isLoading => _isLoading;
  String? get error => _error;
  String get selectedOccasion => _selectedOccasion;
  String get searchQuery => _searchQuery;

  /// Lookbooks narrowed by the active occasion and search query.
  List<OutfitComposition> get filteredLookbooks {
    var result = _lookbooks;

    if (_selectedOccasion != 'All') {
      final occ = _selectedOccasion.toLowerCase();
      result = result.where((l) => l.occasion.toLowerCase().contains(occ)).toList();
    }

    if (_searchQuery.trim().isNotEmpty) {
      final q = _searchQuery.trim().toLowerCase();
      result = result.where((l) =>
          l.name.toLowerCase().contains(q) ||
          l.styleNotes.toLowerCase().contains(q) ||
          l.items.any((it) => it.name.toLowerCase().contains(q))).toList();
    }

    return result;
  }

  /// Loads lookbooks from the repository.
  Future<void> loadLookbooks() async {
    _isLoading = true;
    _error = null;
    notifyListeners();

    try {
      final results = await _repository.getLookbooks();
      _lookbooks = results;
      _isLoading = false;
      notifyListeners();
    } catch (e) {
      _error = 'Failed to load lookbooks: $e';
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Sets the active occasion filter.
  void setOccasion(String occasion) {
    if (_selectedOccasion == occasion) return;
    _selectedOccasion = occasion;
    notifyListeners();
  }

  /// Sets the active search query.
  void setSearchQuery(String query) {
    if (_searchQuery == query) return;
    _searchQuery = query;
    notifyListeners();
  }

  /// Composes a new lookbook with Elle AI and prepends to the collection.
  Future<OutfitComposition> composeLookbook(ComposeOutfitPayload payload) async {
    _isLoading = true;
    _error = null;
    notifyListeners();

    try {
      final composed = await _repository.composeOutfit(payload);
      _lookbooks = [composed, ..._lookbooks.where((l) => l.id != composed.id)];
      _isLoading = false;
      notifyListeners();
      return composed;
    } catch (e) {
      _error = 'Failed to compose lookbook: $e';
      _isLoading = false;
      notifyListeners();
      rethrow;
    }
  }

  /// Updates an existing lookbook's metadata and synchronizes local state.
  Future<OutfitComposition> updateLookbook(String id, UpdateLookbookPayload payload) async {
    _error = null;
    try {
      final updated = await _repository.updateLookbook(id, payload);
      final index = _lookbooks.indexWhere((l) => l.id == id);
      if (index >= 0) {
        final list = List<OutfitComposition>.from(_lookbooks);
        list[index] = updated;
        _lookbooks = List.unmodifiable(list);
        notifyListeners();
      }
      return updated;
    } catch (e) {
      _error = 'Failed to update lookbook: $e';
      notifyListeners();
      rethrow;
    }
  }

  /// Removes a lookbook and optimistic-filters the local collection.
  Future<void> deleteLookbook(String id) async {
    _error = null;
    try {
      await _repository.deleteLookbook(id);
      _lookbooks = _lookbooks.where((l) => l.id != id).toList();
      notifyListeners();
    } catch (e) {
      _error = 'Failed to delete lookbook: $e';
      notifyListeners();
      rethrow;
    }
  }
}
