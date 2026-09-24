import 'package:flutter/material.dart';

import '../../../catalog/data/catalog_product_repository.dart';
import '../../../catalog/domain/catalog_product.dart';
import '../../domain/entities/order_item.dart';

class CatalogItemPickerSheet extends StatefulWidget {
  const CatalogItemPickerSheet({
    super.key,
    required this.catalogRepository,
    required this.onItemSelected,
  });

  final CatalogProductRepository catalogRepository;
  final ValueChanged<OrderItem> onItemSelected;

  static Future<void> show(
    BuildContext context, {
    required CatalogProductRepository catalogRepository,
    required ValueChanged<OrderItem> onItemSelected,
  }) {
    return showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => CatalogItemPickerSheet(
        catalogRepository: catalogRepository,
        onItemSelected: onItemSelected,
      ),
    );
  }

  @override
  State<CatalogItemPickerSheet> createState() => _CatalogItemPickerSheetState();
}

class _CatalogItemPickerSheetState extends State<CatalogItemPickerSheet> {
  final TextEditingController _searchController = TextEditingController();
  List<CatalogProduct> _products = [];
  bool _isLoading = true;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _loadProducts();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _loadProducts([String? search]) async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final page = await widget.catalogRepository.fetchPage(
        page: 0,
        pageSize: 30,
        query: CatalogProductQuery(search: search?.trim() ?? ''),
      );
      if (mounted) {
        setState(() {
          _products = page.products;
          _isLoading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _errorMessage = e.toString();
          _isLoading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      color: scheme.surface,
      borderRadius: const BorderRadius.vertical(top: Radius.circular(28)),
      clipBehavior: Clip.antiAlias,
      child: SizedBox(
        height: MediaQuery.of(context).size.height * 0.75,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
          Center(
            child: Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: scheme.outlineVariant,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 16),
          Text(
            'Select Catalog Piece',
            style: theme.textTheme.titleLarge?.copyWith(
              fontFamily: 'Playfair Display',
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              hintText: 'Search piece name, SKU, or category...',
              prefixIcon: const Icon(Icons.search_rounded),
              suffixIcon: _searchController.text.isNotEmpty
                  ? IconButton(
                      icon: const Icon(Icons.clear_rounded),
                      onPressed: () {
                        _searchController.clear();
                        _loadProducts();
                      },
                    )
                  : null,
              filled: true,
              fillColor: scheme.surfaceContainerLow,
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(16),
                borderSide: BorderSide.none,
              ),
              contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            ),
            onSubmitted: (val) => _loadProducts(val),
          ),
          const SizedBox(height: 16),
          Expanded(
            child: _isLoading
                ? const Center(child: CircularProgressIndicator())
                : _errorMessage != null
                    ? Center(
                        child: Text(
                          _errorMessage!,
                          style: TextStyle(color: scheme.error),
                        ),
                      )
                    : _products.isEmpty
                        ? const Center(
                            child: Text('No catalog pieces found.'),
                          )
                        : ListView.separated(
                            itemCount: _products.length,
                            separatorBuilder: (_, __) => const Divider(height: 1),
                            itemBuilder: (context, index) {
                              final p = _products[index];
                              return ListTile(
                                contentPadding: const EdgeInsets.symmetric(vertical: 4),
                                title: Text(
                                  p.name,
                                  style: theme.textTheme.titleMedium?.copyWith(
                                    fontWeight: FontWeight.w500,
                                  ),
                                ),
                                subtitle: Text(
                                  'SKU: ${p.sku ?? '—'} · ${p.category}',
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color: scheme.onSurfaceVariant,
                                  ),
                                ),
                                trailing: Column(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  crossAxisAlignment: CrossAxisAlignment.end,
                                  children: [
                                    Text(
                                      'LKR ${p.price.toStringAsFixed(0)}',
                                      style: theme.textTheme.titleSmall?.copyWith(
                                        fontWeight: FontWeight.w600,
                                        color: scheme.primary,
                                      ),
                                    ),
                                    if (p.cost > 0)
                                      Text(
                                        'Cost: LKR ${p.cost.toStringAsFixed(0)}',
                                        style: theme.textTheme.bodySmall?.copyWith(
                                          color: scheme.outline,
                                          fontSize: 10,
                                        ),
                                      ),
                                  ],
                                ),
                                onTap: () {
                                  widget.onItemSelected(
                                    OrderItem(
                                      itemId: p.id,
                                      itemName: p.name,
                                      quantity: 1,
                                      unitPrice: p.price,
                                      wholesaleCost: p.cost > 0 ? p.cost : p.price * 0.6,
                                    ),
                                  );
                                  Navigator.of(context).pop();
                                },
                              );
                            },
                          ),
          ),
        ],
      ),
    ),
  ),
);
  }
}
