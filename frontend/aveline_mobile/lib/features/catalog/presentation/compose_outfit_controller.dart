import 'package:flutter/material.dart';

import '../data/catalog_product_repository.dart';
import '../domain/catalog_product.dart';
import '../domain/outfit_composition.dart';
import '../domain/outfit_item.dart';
import '../domain/outfit_payloads.dart';

/// Available ceremonial occasions for outfit composition.
const List<String> composeOccasions = [
  'Sangeet & Reception',
  'Groom Royal Wedding',
  'Bridal Heirloom',
  'Cocktail Reception & Gala',
  'Festive Evening Soirée',
];

/// Controller for the Compose Outfit with Elle AI studio flow.
class ComposeOutfitController extends ChangeNotifier {
  ComposeOutfitController({
    required this.repository,
    CatalogProduct? initialHero,
  }) : _selectedHero = initialHero {
    nameController = TextEditingController();
    styleNotesController = TextEditingController();
  }

  final CatalogProductRepository repository;

  CatalogProduct? _selectedHero;
  List<CatalogProduct> _inventoryPieces = const [];
  String _selectedOccasion = composeOccasions.first;
  bool _isLoadingInventory = false;
  bool _isComposing = false;
  bool _isSaving = false;
  String? _errorMessage;

  List<OutfitItem> _composedItems = const [];
  String? _composedLookbookId;

  late final TextEditingController nameController;
  late final TextEditingController styleNotesController;

  CatalogProduct? get selectedHero => _selectedHero;
  List<CatalogProduct> get inventoryPieces => _inventoryPieces;
  String get selectedOccasion => _selectedOccasion;
  bool get isLoadingInventory => _isLoadingInventory;
  bool get isComposing => _isComposing;
  bool get isSaving => _isSaving;
  String? get errorMessage => _errorMessage;
  List<OutfitItem> get composedItems => _composedItems;

  double get calculatedTotalPrice {
    return _composedItems.fold<double>(0.0, (sum, i) => sum + i.price);
  }

  @override
  void dispose() {
    nameController.dispose();
    styleNotesController.dispose();
    super.dispose();
  }

  /// Loads pieces from the catalog for selecting the primary anchor item.
  Future<void> loadInventory() async {
    _isLoadingInventory = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final page = await repository.fetchPage(
        page: 0,
        pageSize: 50,
        query: const CatalogProductQuery(),
      );
      _inventoryPieces = page.products;
      if (_selectedHero == null && _inventoryPieces.isNotEmpty) {
        _selectedHero = _inventoryPieces.first;
      }
      _isLoadingInventory = false;
      notifyListeners();
    } catch (e) {
      _errorMessage = 'Failed to load pieces: $e';
      _isLoadingInventory = false;
      notifyListeners();
    }
  }

  /// Selects the primary anchor piece for the ensemble.
  void selectHero(CatalogProduct product) {
    if (_selectedHero?.id == product.id) return;
    _selectedHero = product;
    _composedItems = const [];
    notifyListeners();
  }

  /// Changes the ceremonial occasion.
  void setOccasion(String occasion) {
    if (_selectedOccasion == occasion) return;
    _selectedOccasion = occasion;
    notifyListeners();
  }

  /// Triggers Elle AI outfit composition around the selected hero piece.
  Future<void> composeWithElle() async {
    if (_selectedHero == null) {
      _errorMessage = 'Please select a primary piece to anchor the look.';
      notifyListeners();
      return;
    }

    _isComposing = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final hero = _selectedHero!;
      final payload = ComposeOutfitPayload(
        name: '$_selectedOccasion - ${hero.color} Look',
        primaryItemId: hero.id,
        occasion: _selectedOccasion,
        notes: 'Occasion: $_selectedOccasion. Fabric: ${hero.fabric ?? "Pure Silk"}.',
      );

      final result = await repository.composeOutfit(payload);

      _composedLookbookId = result.id;
      _composedItems = result.items;
      nameController.text = result.name;
      styleNotesController.text = result.styleNotes;
      _isComposing = false;
      notifyListeners();
    } catch (e) {
      _errorMessage = 'Failed to compose lookbook: $e';
      _isComposing = false;
      notifyListeners();
    }
  }

  /// Saves the composed lookbook and returns the resulting entity.
  Future<OutfitComposition?> saveLookbook() async {
    if (_selectedHero == null || _composedItems.isEmpty) {
      _errorMessage = 'Please compose the look before saving.';
      notifyListeners();
      return null;
    }

    final title = nameController.text.trim();
    if (title.isEmpty) {
      _errorMessage = 'Please enter a name for the ensemble.';
      notifyListeners();
      return null;
    }

    _isSaving = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final hero = _selectedHero!;
      OutfitComposition result;

      if (_composedLookbookId != null && _composedLookbookId!.isNotEmpty) {
        // Update with user-edited title and notes
        result = await repository.updateLookbook(
          _composedLookbookId!,
          UpdateLookbookPayload(
            name: title,
            occasion: _selectedOccasion,
            styleNotes: styleNotesController.text.trim(),
          ),
        );
      } else {
        result = await repository.composeOutfit(
          ComposeOutfitPayload(
            name: title,
            primaryItemId: hero.id,
            occasion: _selectedOccasion,
            notes: styleNotesController.text.trim(),
          ),
        );
      }

      _isSaving = false;
      notifyListeners();
      return result;
    } catch (e) {
      _errorMessage = 'Failed to save lookbook: $e';
      _isSaving = false;
      notifyListeners();
      return null;
    }
  }
}
