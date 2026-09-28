import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../data/catalog_product_repository.dart';
import '../../data/demo_catalog_product_repository.dart';
import '../../domain/catalog_product.dart';
import '../add_product_controller.dart';
import '../widgets/color_swatch_picker.dart';
import '../widgets/delete_product_sheet.dart';
import '../widgets/image_capture_section.dart';
import '../widgets/size_chip_selector.dart';

/// Screen for creating a new catalog piece or editing an existing one with AI assistance.
class AddEditProductScreen extends StatefulWidget {
  const AddEditProductScreen({
    super.key,
    this.product,
    this.repository,
  });

  final CatalogProduct? product;
  final CatalogProductRepository? repository;

  @override
  State<AddEditProductScreen> createState() => _AddEditProductScreenState();
}

class _AddEditProductScreenState extends State<AddEditProductScreen> {
  late final AddProductController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AddProductController(
      repository: widget.repository ?? DemoCatalogProductRepository(),
      editingProduct: widget.product,
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _handleSave() async {
    final result = await _controller.submit();
    if (!mounted) return;

    if (result != null) {
      Navigator.of(context).pop(result);
    } else if (_controller.errorMessage != null) {
      AppToast.show(context, _controller.errorMessage!, error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isEditing = widget.product != null;

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          key: const Key('add_product_back_button'),
          icon: const Icon(Icons.arrow_back_rounded),
          onPressed: () => Navigator.of(context).maybePop(),
        ),
        title: Text(
          isEditing ? 'Edit Piece' : 'Add New Piece',
          style: theme.textTheme.titleLarge?.copyWith(
            fontWeight: FontWeight.w600,
          ),
        ),
        centerTitle: false,
        actions: [
          if (isEditing && widget.product != null)
            IconButton(
              key: const Key('add_edit_product_delete_button'),
              icon: Icon(Icons.delete_outline_rounded, color: scheme.error),
              tooltip: 'Delete Piece',
              onPressed: () async {
                final deleted = await DeleteProductSheet.show(
                  context,
                  piece: widget.product!,
                  repository: widget.repository ?? DemoCatalogProductRepository(),
                );
                if (deleted == true && context.mounted) {
                  Navigator.of(context).pop();
                }
              },
            ),
        ],
      ),
      body: Stack(
        children: [
          const Positioned.fill(child: BrandBackdrop()),
          Positioned.fill(
            child: ListenableBuilder(
              listenable: _controller,
              builder: (listContext, child) {
                return ListView(
                  padding: const EdgeInsets.fromLTRB(20, 16, 20, 100),
                  children: [
                    // Section 1: Photograph & Vision AI
                    _FormCard(
                      title: 'Photograph & Vision AI',
                      children: [
                        ImageCaptureSection(
                          localImageFile: _controller.localImageFile,
                          networkImageUrl: _controller.imageUrl,
                          isUploading: _controller.isUploadingImage,
                          isAnalyzing: _controller.isAnalyzingImage,
                          analysisResult: _controller.analysisResult,
                          onPickImage: _controller.pickAndAnalyzeImage,
                          onClearImage: _controller.clearImage,
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    // Section 2: Piece Identity & Category
                    _FormCard(
                      title: 'Identity & Category',
                      children: [
                        _InputField(
                          key: const Key('add_product_name_input'),
                          label: 'Piece Name *',
                          controller: _controller.nameController,
                          hintText: 'e.g. Royal Brocade Silk Saree',
                        ),
                        const SizedBox(height: 12),
                        Row(
                          children: [
                            Expanded(
                              child: _InputField(
                                key: const Key('add_product_sku_input'),
                                label: 'SKU / Code',
                                controller: _controller.skuController,
                                hintText: 'AVL-101',
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 14),
                        const SectionOverline('Category'),
                        const SizedBox(height: 8),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            for (final cat in catalogCategories)
                              ChoiceChip(
                                label: Text(cat),
                                selected: _controller.category == cat,
                                onSelected: (selected) {
                                  if (selected) _controller.setCategory(cat);
                                },
                              ),
                          ],
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    // Section 3: Visual & Physical Attributes
                    _FormCard(
                      title: 'Color, Fabric & Sizes',
                      children: [
                        const SectionOverline('Color Swatch'),
                        const SizedBox(height: 8),
                        ColorSwatchPicker(
                          selectedColor: _controller.color,
                          selectedHex: _controller.colorHex,
                          onColorSelected: _controller.setColor,
                        ),
                        const SizedBox(height: 12),
                        _InputField(
                          key: const Key('add_product_color_input'),
                          label: 'Color Name',
                          initialValue: _controller.color,
                          onChanged: (val) => _controller.setColor(val),
                          hintText: 'e.g. Emerald Green, Imperial Burgundy',
                        ),
                        const SizedBox(height: 12),
                        Row(
                          children: [
                            Expanded(
                              child: _InputField(
                                key: const Key('add_product_fabric_input'),
                                label: 'Fabric',
                                controller: _controller.fabricController,
                                hintText: 'e.g. Pure Mulberry Silk',
                              ),
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: _InputField(
                                key: const Key('add_product_style_input'),
                                label: 'Style / Aesthetic',
                                controller: _controller.styleController,
                                hintText: 'e.g. Contemporary Festive',
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        _InputField(
                          key: const Key('add_product_pattern_input'),
                          label: 'Pattern / Embroidery',
                          controller: _controller.patternController,
                          hintText: 'e.g. Gold Zari Brocade, Handwoven Floral',
                        ),
                        const SizedBox(height: 14),
                        const SectionOverline('Available Sizes'),
                        const SizedBox(height: 8),
                        SizeChipSelector(
                          selectedSizes: _controller.sizes,
                          onToggleSize: _controller.toggleSize,
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    // Section 4: Pricing & Inventory
                    _FormCard(
                      title: 'Pricing & Inventory',
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: _InputField(
                                key: const Key('add_product_price_input'),
                                label: 'Retail Price (Rs) *',
                                controller: _controller.priceController,
                                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                                hintText: '1250',
                              ),
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: _InputField(
                                key: const Key('add_product_cost_input'),
                                label: 'Wholesale Cost (Rs)',
                                controller: _controller.costController,
                                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                                hintText: '550',
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        _InputField(
                          key: const Key('add_product_quantity_input'),
                          label: 'Initial Stock Quantity',
                          controller: _controller.quantityController,
                          keyboardType: TextInputType.number,
                          hintText: '4',
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    // Section 5: Storytelling & Description
                    _FormCard(
                      title: 'Description & Styling Notes',
                      children: [
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Flexible(
                              child: Text(
                                'Couture Narrative',
                                style: theme.textTheme.labelMedium?.copyWith(
                                  color: scheme.onSurfaceVariant,
                                ),
                              ),
                            ),
                            TextButton.icon(
                              key: const Key('add_product_generate_ai_button'),
                              onPressed: _controller.generateAiDescription,
                              icon: const Icon(Icons.auto_awesome, size: 14),
                              label: const Text('Generate with AI', style: TextStyle(fontSize: 12)),
                            ),
                          ],
                        ),
                        const SizedBox(height: 6),
                        TextField(
                          key: const Key('add_product_description_input'),
                          controller: _controller.descriptionController,
                          maxLines: 4,
                          decoration: InputDecoration(
                            hintText: 'Describe drape, styling recommendations, and boutique notes...',
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(12),
                            ),
                            filled: true,
                            fillColor: scheme.surfaceContainerLowest,
                          ),
                        ),
                      ],
                    ),
                    if (isEditing && widget.product != null) ...[
                      const SizedBox(height: 16),
                      OutlinedButton.icon(
                        key: const Key('add_edit_delete_piece_btn'),
                        onPressed: () async {
                          final deleted = await DeleteProductSheet.show(
                            context,
                            piece: widget.product!,
                            repository: widget.repository ?? DemoCatalogProductRepository(),
                          );
                          if (deleted == true && context.mounted) {
                            Navigator.of(context).pop();
                          }
                        },
                        icon: Icon(Icons.delete_outline_rounded, color: scheme.error, size: 18),
                        label: Text(
                          'Delete Piece from Catalog',
                          style: TextStyle(color: scheme.error, fontWeight: FontWeight.w600),
                        ),
                        style: OutlinedButton.styleFrom(
                          padding: const EdgeInsets.symmetric(vertical: 14),
                          side: BorderSide(color: scheme.error.withValues(alpha: 0.5)),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                      ),
                    ],
                  ],
                );
              },
            ),
          ),

          // Bottom Bar Action
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
              decoration: BoxDecoration(
                color: scheme.surface,
                border: Border(top: BorderSide(color: scheme.outlineVariant)),
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withValues(alpha: 0.05),
                    blurRadius: 10,
                    offset: const Offset(0, -4),
                  ),
                ],
              ),
              child: SafeArea(
                top: false,
                child: ListenableBuilder(
                  listenable: _controller,
                  builder: (context, _) {
                    return SizedBox(
                      width: double.infinity,
                      height: 48,
                      child: ElevatedButton(
                        key: const Key('add_product_submit_button'),
                        onPressed: _controller.isSubmitting ? null : _handleSave,
                        style: ElevatedButton.styleFrom(
                          backgroundColor: scheme.primary,
                          foregroundColor: scheme.onPrimary,
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12),
                          ),
                        ),
                        child: _controller.isSubmitting
                            ? const SizedBox(
                                width: 20,
                                height: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                                ),
                              )
                            : Text(
                                isEditing ? 'Save Changes' : 'Add Piece to Catalog',
                                style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15),
                              ),
                      ),
                    );
                  },
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _FormCard extends StatelessWidget {
  const _FormCard({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 14),
          ...children,
        ],
      ),
    );
  }
}

class _InputField extends StatelessWidget {
  const _InputField({
    Key? key,
    required this.label,
    this.controller,
    this.initialValue,
    this.onChanged,
    this.hintText,
    this.keyboardType,
  })  : fieldKey = key,
        super(key: null);

  final Key? fieldKey;
  final String label;
  final TextEditingController? controller;
  final String? initialValue;
  final ValueChanged<String>? onChanged;
  final String? hintText;
  final TextInputType? keyboardType;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: theme.textTheme.labelMedium?.copyWith(
            color: scheme.onSurfaceVariant,
            fontWeight: FontWeight.w500,
          ),
        ),
        const SizedBox(height: 6),
        TextFormField(
          key: fieldKey,
          controller: controller,
          initialValue: controller == null ? initialValue : null,
          onChanged: onChanged,
          keyboardType: keyboardType,
          decoration: InputDecoration(
            hintText: hintText,
            contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(12),
            ),
            filled: true,
            fillColor: scheme.surfaceContainerLowest,
          ),
        ),
      ],
    );
  }
}
