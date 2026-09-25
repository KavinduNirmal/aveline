import 'package:flutter/material.dart';

import '../../data/catalog_product_repository.dart';
import '../../data/demo_catalog_product_repository.dart';
import '../../domain/catalog_product.dart';
import '../../domain/outfit_item.dart';
import '../compose_outfit_controller.dart';

/// Screen for composing a styled lookbook / ensemble around a primary piece using Elle AI.
class ComposeOutfitScreen extends StatefulWidget {
  const ComposeOutfitScreen({
    super.key,
    this.initialHero,
    this.repository,
  });

  final CatalogProduct? initialHero;
  final CatalogProductRepository? repository;

  @override
  State<ComposeOutfitScreen> createState() => _ComposeOutfitScreenState();
}

class _ComposeOutfitScreenState extends State<ComposeOutfitScreen> {
  late final ComposeOutfitController _controller;

  @override
  void initState() {
    super.initState();
    _controller = ComposeOutfitController(
      repository: widget.repository ?? DemoCatalogProductRepository(),
      initialHero: widget.initialHero,
    );
    _controller.loadInventory();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _handleSave() async {
    final result = await _controller.saveLookbook();
    if (result != null && mounted) {
      Navigator.of(context).pop(result);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'Compose Look with Elle',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w600,
            fontFamily: 'PlayfairDisplay',
          ),
        ),
        elevation: 0,
        backgroundColor: Colors.transparent,
      ),
      body: Stack(
        children: [
          SafeArea(
            child: ListenableBuilder(
              listenable: _controller,
              builder: (context, _) {
                if (_controller.isLoadingInventory) {
                  return const Center(
                    child: CircularProgressIndicator(),
                  );
                }

                return ListView(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 96),
                  children: [
                    if (_controller.errorMessage != null) ...[
                      Container(
                        padding: const EdgeInsets.all(12),
                        margin: const EdgeInsets.only(bottom: 16),
                        decoration: BoxDecoration(
                          color: scheme.errorContainer,
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: Text(
                          _controller.errorMessage!,
                          style: TextStyle(
                            color: scheme.onErrorContainer,
                            fontSize: 13,
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ),
                    ],

                    // Section 1: Hero Piece Selection
                    _FormCard(
                      title: '1. Primary Statement Piece',
                      subtitle: 'Choose the anchor garment from your inventory.',
                      children: [
                        if (_controller.inventoryPieces.isEmpty)
                          const Text('No pieces available in catalog inventory.')
                        else
                          SizedBox(
                            height: 130,
                            child: ListView.separated(
                              scrollDirection: Axis.horizontal,
                              itemCount: _controller.inventoryPieces.length,
                              separatorBuilder: (context, index) => const SizedBox(width: 10),
                              itemBuilder: (context, index) {
                                final product = _controller.inventoryPieces[index];
                                final isSelected = _controller.selectedHero?.id == product.id;
                                return _HeroProductItem(
                                  key: Key('compose_hero_${product.id}'),
                                  product: product,
                                  isSelected: isSelected,
                                  onTap: () => _controller.selectHero(product),
                                  scheme: scheme,
                                );
                              },
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    // Section 2: Ceremonial Occasion
                    _FormCard(
                      title: '2. Ceremonial Occasion',
                      subtitle: 'Target the event styling and mood palette.',
                      children: [
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            for (final occ in composeOccasions)
                              ChoiceChip(
                                key: Key('compose_occ_$occ'),
                                label: Text(occ, style: const TextStyle(fontSize: 12)),
                                selected: _controller.selectedOccasion == occ,
                                onSelected: (selected) {
                                  if (selected) _controller.setOccasion(occ);
                                },
                              ),
                          ],
                        ),
                        const SizedBox(height: 16),
                        SizedBox(
                          width: double.infinity,
                          child: ElevatedButton.icon(
                            key: const Key('compose_elle_trigger_button'),
                            onPressed: _controller.isComposing ? null : _controller.composeWithElle,
                            icon: _controller.isComposing
                                ? const SizedBox(
                                    width: 16,
                                    height: 16,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                      valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                                    ),
                                  )
                                : const Icon(Icons.auto_awesome, size: 18),
                            label: Text(
                              _controller.isComposing ? 'Elle is styling ensemble...' : 'Compose with Elle AI',
                              style: const TextStyle(fontWeight: FontWeight.w600),
                            ),
                            style: ElevatedButton.styleFrom(
                              backgroundColor: scheme.primary,
                              foregroundColor: scheme.onPrimary,
                              padding: const EdgeInsets.symmetric(vertical: 14),
                              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    // Section 3: Composed Ensemble Breakdown
                    if (_controller.composedItems.isNotEmpty) ...[
                      _FormCard(
                        title: '3. Curated Ensemble Breakdown',
                        subtitle: 'Coordinated by Elle with complementary drape and accessories.',
                        children: [
                          Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              Expanded(
                                child: Text(
                                  '${_controller.composedItems.length} Pieces Coordinated',
                                  style: theme.textTheme.labelMedium?.copyWith(
                                    color: scheme.onSurfaceVariant,
                                    fontWeight: FontWeight.w600,
                                  ),
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              const SizedBox(width: 8),
                              Text(
                                'Total: Rs ${_controller.calculatedTotalPrice.toStringAsFixed(0)}',
                                style: theme.textTheme.titleSmall?.copyWith(
                                  color: scheme.primary,
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 12),
                          for (final item in _controller.composedItems) ...[
                            _ComposedItemTile(item: item, scheme: scheme),
                            const SizedBox(height: 8),
                          ],
                        ],
                      ),
                      const SizedBox(height: 16),

                      // Section 4: Title & Styling Notes
                      _FormCard(
                        title: '4. Editorial Title & Notes',
                        children: [
                          Text(
                            'Ensemble Name *',
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: scheme.onSurfaceVariant,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                          const SizedBox(height: 6),
                          TextField(
                            key: const Key('compose_outfit_title_input'),
                            controller: _controller.nameController,
                            decoration: InputDecoration(
                              hintText: 'e.g. Royal Sangeet Emerald Look',
                              contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                              border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                              filled: true,
                              fillColor: scheme.surfaceContainerLowest,
                            ),
                          ),
                          const SizedBox(height: 14),
                          Text(
                            'Elle Couture Styling Narrative',
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: scheme.onSurfaceVariant,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                          const SizedBox(height: 6),
                          TextField(
                            key: const Key('compose_outfit_notes_input'),
                            controller: _controller.styleNotesController,
                            maxLines: 3,
                            decoration: InputDecoration(
                              hintText: 'Draping recommendations, jewelry advice...',
                              border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                              filled: true,
                              fillColor: scheme.surfaceContainerLowest,
                            ),
                          ),
                        ],
                      ),
                    ],
                  ],
                );
              },
            ),
          ),

          // Bottom Save Bar
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
                    final canSave = _controller.composedItems.isNotEmpty && !_controller.isSaving;
                    return SizedBox(
                      width: double.infinity,
                      height: 48,
                      child: ElevatedButton(
                        key: const Key('compose_outfit_save_button'),
                        onPressed: canSave ? _handleSave : null,
                        style: ElevatedButton.styleFrom(
                          backgroundColor: scheme.primary,
                          foregroundColor: scheme.onPrimary,
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                        child: _controller.isSaving
                            ? const SizedBox(
                                width: 20,
                                height: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                                ),
                              )
                            : const Text(
                                'Save Ensemble to Lookbooks',
                                style: TextStyle(fontWeight: FontWeight.w600, fontSize: 15),
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
  const _FormCard({
    required this.title,
    this.subtitle,
    required this.children,
  });

  final String title;
  final String? subtitle;
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
          if (subtitle != null) ...[
            const SizedBox(height: 2),
            Text(
              subtitle!,
              style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
            ),
          ],
          const SizedBox(height: 14),
          ...children,
        ],
      ),
    );
  }
}

class _HeroProductItem extends StatelessWidget {
  const _HeroProductItem({
    super.key,
    required this.product,
    required this.isSelected,
    required this.onTap,
    required this.scheme,
  });

  final CatalogProduct product;
  final bool isSelected;
  final VoidCallback onTap;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Container(
          width: 96,
          padding: const EdgeInsets.all(6),
          decoration: BoxDecoration(
            color: isSelected ? scheme.primary.withValues(alpha: 0.08) : scheme.surfaceContainerLowest,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: isSelected ? scheme.primary : scheme.outlineVariant,
              width: isSelected ? 2 : 1,
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(8),
                  child: Stack(
                    fit: StackFit.expand,
                    children: [
                      if (product.imageUrl != null && product.imageUrl!.trim().isNotEmpty)
                        Image.network(
                          product.imageUrl!,
                          fit: BoxFit.cover,
                          errorBuilder: (context, error, stackTrace) => _placeholder(),
                        )
                      else
                        _placeholder(),
                      if (isSelected)
                        Positioned(
                          right: 4,
                          top: 4,
                          child: Container(
                            padding: const EdgeInsets.all(2),
                            decoration: BoxDecoration(
                              color: scheme.primary,
                              shape: BoxShape.circle,
                            ),
                            child: const Icon(Icons.check, size: 12, color: Colors.white),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 6),
              Text(
                product.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600),
              ),
              Text(
                'Rs ${product.price.toStringAsFixed(0)}',
                style: TextStyle(fontSize: 10.5, color: scheme.primary, fontWeight: FontWeight.bold),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _placeholder() {
    return Container(
      color: scheme.surfaceContainerHighest,
      child: Icon(Icons.checkroom, size: 24, color: scheme.outlineVariant),
    );
  }
}

class _ComposedItemTile extends StatelessWidget {
  const _ComposedItemTile({required this.item, required this.scheme});

  final OutfitItem item;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Row(
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(6),
            child: SizedBox(
              width: 44,
              height: 44,
              child: item.imageUrl.trim().isNotEmpty
                  ? Image.network(item.imageUrl, fit: BoxFit.cover, errorBuilder: (context, error, stackTrace) => _placeholder())
                  : _placeholder(),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1.5),
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerHighest,
                        borderRadius: BorderRadius.circular(4),
                      ),
                      child: Text(
                        item.position.toUpperCase(),
                        style: TextStyle(fontSize: 8.5, fontWeight: FontWeight.bold, color: scheme.onSurfaceVariant),
                      ),
                    ),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        item.category,
                        style: TextStyle(fontSize: 10, color: scheme.onSurfaceVariant),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 2),
                Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
              ],
            ),
          ),
          Text(
            'Rs ${item.price.toStringAsFixed(0)}',
            style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold, color: scheme.primary),
          ),
        ],
      ),
    );
  }

  Widget _placeholder() {
    return Container(
      color: scheme.surfaceContainerHighest,
      child: Icon(Icons.checkroom, size: 18, color: scheme.outlineVariant),
    );
  }
}
