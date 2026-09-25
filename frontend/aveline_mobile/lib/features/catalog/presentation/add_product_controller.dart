import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../data/catalog_product_repository.dart';
import '../domain/catalog_product.dart';
import '../domain/product_payloads.dart';
import '../domain/vision_analysis.dart';

const List<String> catalogCategories = [
  'Sarees',
  'Lehengas',
  'Gowns',
  'Kurtas & Tunics',
  'Outerwear',
  'Drapes & Shawls',
  'Jewelry & Accessories',
];

/// Controller managing state and operations for creating or editing a catalog piece.
class AddProductController extends ChangeNotifier {
  AddProductController({
    required this.repository,
    this.editingProduct,
  }) {
    _init();
  }

  final CatalogProductRepository repository;
  final CatalogProduct? editingProduct;

  bool get isEditing => editingProduct != null;

  // Text Controllers
  final TextEditingController nameController = TextEditingController();
  final TextEditingController skuController = TextEditingController();
  final TextEditingController fabricController = TextEditingController();
  final TextEditingController styleController = TextEditingController();
  final TextEditingController patternController = TextEditingController();
  final TextEditingController priceController = TextEditingController();
  final TextEditingController costController = TextEditingController();
  final TextEditingController quantityController = TextEditingController();
  final TextEditingController descriptionController = TextEditingController();

  // State values
  String _category = 'Sarees';
  String get category => _category;

  String _color = '';
  String get color => _color;

  String? _colorHex;
  String? get colorHex => _colorHex;

  List<String> _sizes = ['38', '40', '42'];
  List<String> get sizes => List.unmodifiable(_sizes);

  File? _localImageFile;
  File? get localImageFile => _localImageFile;

  String? _imageUrl;
  String? get imageUrl => _imageUrl;

  String? _uploadedImageId;
  String? get uploadedImageId => _uploadedImageId;

  VisionAnalysis? _analysisResult;
  VisionAnalysis? get analysisResult => _analysisResult;

  bool _isUploadingImage = false;
  bool get isUploadingImage => _isUploadingImage;

  bool _isAnalyzingImage = false;
  bool get isAnalyzingImage => _isAnalyzingImage;

  bool _isSubmitting = false;
  bool get isSubmitting => _isSubmitting;

  String? _errorMessage;
  String? get errorMessage => _errorMessage;

  void _init() {
    if (editingProduct case final p?) {
      nameController.text = p.name;
      skuController.text = p.sku ?? '';
      _category = p.category.isNotEmpty ? p.category : 'Sarees';
      _color = p.color;
      _colorHex = null;
      fabricController.text = p.fabric ?? '';
      styleController.text = p.style ?? '';
      _sizes = List<String>.from(p.sizes.isNotEmpty ? p.sizes : ['One size']);
      priceController.text = p.price > 0 ? p.price.toStringAsFixed(0) : '';
      costController.text = p.cost > 0 ? p.cost.toStringAsFixed(0) : '';
      quantityController.text = p.quantity.toString();
      _imageUrl = p.imageUrl;
      descriptionController.text = p.description ?? '';
    } else {
      skuController.text = 'AVL-${math.Random().nextInt(900) + 100}';
      priceController.text = '1250';
      costController.text = '550';
      quantityController.text = '4';
      _sizes = ['38', '40', '42'];
    }
  }

  void setCategory(String newCategory) {
    if (_category != newCategory) {
      _category = newCategory;
      notifyListeners();
    }
  }

  void setColor(String newColor, [String? newHex]) {
    _color = newColor;
    _colorHex = newHex;
    notifyListeners();
  }

  void toggleSize(String size) {
    if (_sizes.contains(size)) {
      _sizes.remove(size);
    } else {
      _sizes.add(size);
    }
    notifyListeners();
  }

  void clearImage() {
    _localImageFile = null;
    _imageUrl = null;
    _uploadedImageId = null;
    _analysisResult = null;
    notifyListeners();
  }

  Future<void> pickAndAnalyzeImage(
    ImageSource source, {
    ImagePicker? pickerOverride,
  }) async {
    _errorMessage = null;
    final picker = pickerOverride ?? ImagePicker();

    try {
      final picked = await picker.pickImage(
        source: source,
        maxWidth: 1600,
        maxHeight: 1600,
        imageQuality: 85,
      );

      if (picked == null) return;

      final file = File(picked.path);
      final bytes = await picked.readAsBytes();
      final fileName = picked.name.isNotEmpty ? picked.name : 'garment_${DateTime.now().millisecondsSinceEpoch}.jpg';

      _localImageFile = file;
      _isUploadingImage = true;
      notifyListeners();

      // 1. Upload to backend media store
      final uploadResult = await repository.uploadImage(
        bytes: bytes,
        fileName: fileName,
        contentType: 'image/jpeg',
      );

      _uploadedImageId = uploadResult.id;
      _imageUrl = uploadResult.url;
      _isUploadingImage = false;
      _isAnalyzingImage = true;
      notifyListeners();

      // 2. Multimodal Vision AI Analysis
      final analysis = await repository.analyzeImage(
        imageRefId: uploadResult.id,
        fileNameHint: fileName,
      );

      _analysisResult = analysis;
      _isAnalyzingImage = false;

      // Auto-populate fields if currently blank
      if (nameController.text.trim().isEmpty && analysis.suggestedItemName != null) {
        nameController.text = analysis.suggestedItemName!;
      }
      if (analysis.category.isNotEmpty && catalogCategories.contains(analysis.category)) {
        _category = analysis.category;
      }
      if (analysis.detectedColor != null && analysis.detectedColor!.isNotEmpty) {
        _color = analysis.detectedColor!;
        _colorHex = analysis.colorHex;
      }
      if (fabricController.text.trim().isEmpty && analysis.fabric != null) {
        fabricController.text = analysis.fabric!;
      }
      if (styleController.text.trim().isEmpty && analysis.style != null) {
        styleController.text = analysis.style!;
      }
      if (patternController.text.trim().isEmpty && analysis.pattern != null) {
        patternController.text = analysis.pattern!;
      }
      if (descriptionController.text.trim().isEmpty && analysis.description != null) {
        descriptionController.text = analysis.description!;
      }

      notifyListeners();
    } catch (e) {
      _isUploadingImage = false;
      _isAnalyzingImage = false;
      _errorMessage = 'Could not analyze photograph: $e';
      notifyListeners();
    }
  }

  void generateAiDescription() {
    final activeColor = _color.isNotEmpty ? _color : 'Luxe';
    final activeFabric = fabricController.text.isNotEmpty ? fabricController.text : 'Pure Silk';
    final activeGarment = nameController.text.isNotEmpty ? nameController.text : _category;
    final activePattern = patternController.text.isNotEmpty ? patternController.text : 'Artisanal Weave';

    final text = 'Exquisite $activeColor $activeGarment woven from authentic $activeFabric, '
        'featuring an opulent $activePattern with a lustrous heirloom drape. '
        'Tailored with meticulous craftsmanship, ideal for royal occasions and celebratory galas.';

    descriptionController.text = text;
    notifyListeners();
  }

  Future<CatalogProduct?> submit() async {
    final name = nameController.text.trim();
    if (name.isEmpty) {
      _errorMessage = 'Please enter a name for the piece.';
      notifyListeners();
      return null;
    }

    final parsedPrice = double.tryParse(priceController.text.trim());
    if (parsedPrice == null || parsedPrice <= 0) {
      _errorMessage = 'Please enter a valid retail price greater than zero.';
      notifyListeners();
      return null;
    }

    final parsedCost = double.tryParse(costController.text.trim()) ?? 0.0;
    final parsedQuantity = int.tryParse(quantityController.text.trim()) ?? 1;

    _isSubmitting = true;
    _errorMessage = null;
    notifyListeners();

    try {
      CatalogProduct result;

      if (isEditing) {
        final payload = UpdateProductPayload(
          name: name,
          category: _category,
          color: _color.isNotEmpty ? _color : 'Unspecified',
          colorHex: _colorHex,
          sizes: _sizes,
          price: parsedPrice,
          cost: parsedCost,
          quantity: parsedQuantity,
          imageUrl: _imageUrl,
          sku: skuController.text.trim().isNotEmpty ? skuController.text.trim() : null,
          fabric: fabricController.text.trim().isNotEmpty ? fabricController.text.trim() : null,
          style: styleController.text.trim().isNotEmpty ? styleController.text.trim() : null,
          pattern: patternController.text.trim().isNotEmpty ? patternController.text.trim() : null,
          description: descriptionController.text.trim().isNotEmpty ? descriptionController.text.trim() : null,
        );

        result = await repository.updateProduct(editingProduct!.id, payload);
      } else {
        final payload = CreateProductPayload(
          name: name,
          category: _category,
          color: _color.isNotEmpty ? _color : 'Unspecified',
          colorHex: _colorHex,
          sizes: _sizes,
          price: parsedPrice,
          cost: parsedCost,
          quantity: parsedQuantity,
          imageUrl: _imageUrl,
          sku: skuController.text.trim().isNotEmpty ? skuController.text.trim() : null,
          fabric: fabricController.text.trim().isNotEmpty ? fabricController.text.trim() : null,
          style: styleController.text.trim().isNotEmpty ? styleController.text.trim() : null,
          pattern: patternController.text.trim().isNotEmpty ? patternController.text.trim() : null,
          description: descriptionController.text.trim().isNotEmpty ? descriptionController.text.trim() : null,
        );

        result = await repository.createProduct(payload);
      }

      _isSubmitting = false;
      notifyListeners();
      return result;
    } catch (e) {
      _isSubmitting = false;
      _errorMessage = 'Could not save piece: $e';
      notifyListeners();
      return null;
    }
  }

  @override
  void dispose() {
    nameController.dispose();
    skuController.dispose();
    fabricController.dispose();
    styleController.dispose();
    patternController.dispose();
    priceController.dispose();
    costController.dispose();
    quantityController.dispose();
    descriptionController.dispose();
    super.dispose();
  }
}
