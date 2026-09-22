import 'dart:math' as math;

import '../domain/catalog_filters.dart';
import '../domain/catalog_product.dart';
import 'catalog_product_repository.dart';

/// A boutique's worth of pieces, paged in memory.
///
/// Stands in for `POST /internal/visual/inventory/search` until the inventory
/// slice is wired. It filters the same way the query asks the API to, so the
/// screen's search, tags and filter options already behave like the real thing,
/// and it fills every field of `InventoryItemDto` so the detail screen has real
/// data to lay out.
class DemoCatalogProductRepository implements CatalogProductRepository {
  DemoCatalogProductRepository({
    this.pageDelay = const Duration(milliseconds: 350),
  });

  /// How long a page takes to arrive.
  ///
  /// The pool is local, so without a delay the list would arrive instantly and
  /// the infinite-scroll loading state would never be seen. Tests pass
  /// `Duration.zero`.
  final Duration pageDelay;

  List<CatalogProduct>? _pool;

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final matching = _filter(_pool ??= _buildPool(), query);
    final start = page * pageSize;
    if (start >= matching.length) {
      return const CatalogProductPage(
        products: <CatalogProduct>[],
        hasMore: false,
      );
    }

    final end = math.min(start + pageSize, matching.length);
    return CatalogProductPage(
      products: matching.sublist(start, end),
      hasMore: end < matching.length,
    );
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    for (final product in pool) {
      if (product.id == id) {
        return product;
      }
    }
    return null;
  }

  @override
  Future<CatalogProduct> updateStatus(
    String id,
    CatalogItemStatus status,
  ) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    final pool = _pool ??= _buildPool();
    final index = pool.indexWhere((piece) => piece.id == id);
    if (index < 0) {
      throw StateError('No demo piece with id $id.');
    }

    // The API derives `isAvailable` from the status, so the stand-in derives it the same way
    // rather than letting the two disagree on the screen. The change is held in the pool, so
    // it survives for the session and the grid reads it back.
    final updated = pool[index].copyWith(
      status: status,
      isAvailable: status == CatalogItemStatus.available,
    );
    pool[index] = updated;
    return updated;
  }

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) async {
    if (pageDelay > Duration.zero) {
      await Future<void>.delayed(pageDelay);
    }

    return 'demo-sourcing-${piece.id}';
  }

  List<CatalogProduct> _filter(
    List<CatalogProduct> all,
    CatalogProductQuery query,
  ) {
    final search = query.search.trim().toLowerCase();
    return all.where((product) {
      if (search.isNotEmpty && !_matchesSearch(product, search)) {
        return false;
      }
      if (query.tagIds.isNotEmpty && !product.tags.any(query.tagIds.contains)) {
        return false;
      }
      return _matchesFilters(product, query.filters);
    }).toList();
  }

  bool _matchesSearch(CatalogProduct product, String search) {
    final haystack = <String>[
      product.name,
      product.category,
      product.color,
      product.fabric ?? '',
      product.style ?? '',
      product.sku ?? '',
      product.sourceLabel,
      product.description ?? '',
      ...product.sizes,
    ].join(' ').toLowerCase();
    return haystack.contains(search);
  }

  bool _matchesFilters(CatalogProduct product, CatalogFilters filters) {
    final availability = filters.selectedIn(CatalogFilterGroup.availability);
    if (availability.isNotEmpty &&
        !availability.contains(product.status.label)) {
      return false;
    }

    final categories = filters.selectedIn(CatalogFilterGroup.category);
    if (categories.isNotEmpty && !categories.contains(product.category)) {
      return false;
    }

    final fabrics = filters.selectedIn(CatalogFilterGroup.fabric);
    if (fabrics.isNotEmpty && !fabrics.contains(product.fabric)) {
      return false;
    }

    final sizes = filters.selectedIn(CatalogFilterGroup.size);
    if (sizes.isNotEmpty && !product.sizes.any(sizes.contains)) {
      return false;
    }

    final bands = filters.selectedIn(CatalogFilterGroup.price);
    if (bands.isNotEmpty &&
        !bands.any((band) => _inBand(product.price, band))) {
      return false;
    }

    return true;
  }

  static bool _inBand(double price, String band) => switch (band) {
    'Under 25k' => price < 25000,
    '25k - 75k' => price >= 25000 && price < 75000,
    '75k - 150k' => price >= 75000 && price <= 150000,
    'Over 150k' => price > 150000,
    _ => true,
  };

  static List<CatalogProduct> _buildPool() {
    return [
      for (var i = 0; i < _pieces.length; i++) _productFor(i, _pieces[i]),
    ];
  }

  static CatalogProduct _productFor(int index, _Piece piece) {
    final price = 18500.0 + (index % 12) * 12000 + (index % 3) * 1500;
    final status = _statuses[index % _statuses.length];
    // Walked in fives so the range reads like a real policy rather than a
    // single number repeated.
    final discountMin = 5 + (index % 3) * 5;

    return CatalogProduct(
      id: 'piece-${(index + 1).toString().padLeft(3, '0')}',
      organizationId: 'org-demo',
      name: piece.name,
      category: piece.category,
      color: _colors[index % _colors.length],
      sizes: _sizes[index % _sizes.length],
      price: price,
      // A mark-up that varies by piece rather than one flat multiplier.
      cost: price * (0.42 + (index % 5) * 0.06),
      quantity: _quantityFor(index, status),
      status: status,
      isAvailable: status == CatalogItemStatus.available,
      createdAtUtc: DateTime.utc(
        2026,
        9,
        17,
      ).subtract(Duration(days: index * 3)),
      fabric: piece.fabric,
      style: _styles[index % _styles.length],
      sku:
          'AVL-${(index + 1).toString().padLeft(4, '0')}-'
          '${piece.category.substring(0, 1).toUpperCase()}',
      description: piece.description,
      sourcedFrom: _sources[index % _sources.length],
      discountMinPercent: discountMin,
      discountMaxPercent: discountMin + 10,
      metadata: {
        'Care': _care[index % _care.length],
        'Origin': _origins[index % _origins.length],
        'Batch':
            'B-${2026 - index % 3}-'
            '${(index + 1).toString().padLeft(3, '0')}',
      },
      tags: _tags[index % _tags.length],
    );
  }

  static int _quantityFor(int index, CatalogItemStatus status) {
    return switch (status) {
      // Every fourth available piece is down to its last one or two, so the
      // grid carries low-stock pieces without inventing a separate pool.
      CatalogItemStatus.available => index % 4 == 0 ? 1 : 3 + (index % 9),
      CatalogItemStatus.onHold => index % 3,
      CatalogItemStatus.unavailable => index % 2,
      CatalogItemStatus.soldOut || CatalogItemStatus.archived => 0,
    };
  }

  static const List<CatalogItemStatus> _statuses = [
    CatalogItemStatus.available,
    CatalogItemStatus.available,
    CatalogItemStatus.available,
    CatalogItemStatus.onHold,
    CatalogItemStatus.available,
    CatalogItemStatus.soldOut,
    CatalogItemStatus.available,
    CatalogItemStatus.unavailable,
    CatalogItemStatus.available,
    CatalogItemStatus.archived,
  ];

  static const List<String> _colors = [
    'Wine',
    'Rose quartz',
    'Champagne',
    'Emerald',
    'Midnight',
    'Ivory',
    'Saffron',
    'Lilac',
  ];

  static const List<List<String>> _sizes = [
    ['36', '38', '40', '42'],
    ['S', 'M', 'L'],
    ['38', '40', 'Custom'],
    ['One size'],
    ['34', '36', '38'],
  ];

  static const List<String> _styles = [
    'Contemporary festive',
    'Bridal classic',
    'Everyday luxury',
    'Evening',
    'Resort',
  ];

  static const List<String> _sources = [
    'Varanasi Weavers',
    'Handloom Silk Mill, Kandy',
    'Colombo Atelier',
    'Jaipur Block Prints',
    'Kandy Loom House',
  ];

  static const List<String> _origins = ['Sri Lanka', 'India', 'Italy'];

  static const List<String> _care = [
    'Dry clean only',
    'Hand wash cold',
    'Dry clean recommended',
  ];

  static const List<Set<String>> _tags = [
    {'new-in', 'bridal'},
    {'festive', 'handloom'},
    {'evening', 'bestsellers'},
    {'raw-silk', 'atelier-pick'},
    {'bridal', 'festive'},
    {'new-in', 'under-25k'},
    {'handloom', 'atelier-pick'},
    {'evening', 'reserved'},
  ];
}

/// The name, category, fabric and copy of one demo piece.
typedef _Piece = ({
  String name,
  String category,
  String fabric,
  String description,
});

const List<_Piece> _pieces = [
  (
    name: 'Handloom Silk Saree',
    category: 'Sarees',
    fabric: 'Raw silk',
    description:
        'Handwoven raw silk with a fine zari border, finished in the atelier.',
  ),
  (
    name: 'Kanjivaram Zari Saree',
    category: 'Sarees',
    fabric: 'Raw silk',
    description: 'A temple-border Kanjivaram for weddings and long evenings.',
  ),
  (
    name: 'Georgette Party Saree',
    category: 'Sarees',
    fabric: 'Chiffon',
    description: 'Feather-light georgette with a scattered sequin pallu.',
  ),
  (
    name: 'Organza Sequin Saree',
    category: 'Sarees',
    fabric: 'Organza',
    description: 'Crisp organza carrying a hand-set sequin field.',
  ),
  (
    name: 'Embroidered Velvet Lehenga',
    category: 'Lehengas',
    fabric: 'Velvet',
    description: 'Deep velvet with tonal thread work across the skirt.',
  ),
  (
    name: 'Chikankari Lehenga',
    category: 'Lehengas',
    fabric: 'Chiffon',
    description: 'Lucknowi chikankari on a soft chiffon base.',
  ),
  (
    name: 'Bridal Velvet Lehenga',
    category: 'Lehengas',
    fabric: 'Velvet',
    description: 'A bridal set with zardozi bodice and a full circular skirt.',
  ),
  (
    name: 'Pastel Tulle Lehenga',
    category: 'Lehengas',
    fabric: 'Organza',
    description: 'Layered tulle in a soft pastel, cut for movement.',
  ),
  (
    name: 'Column Evening Gown',
    category: 'Gowns',
    fabric: 'Chiffon',
    description: 'A clean column line with a draped shoulder.',
  ),
  (
    name: 'Draped Satin Gown',
    category: 'Gowns',
    fabric: 'Raw silk',
    description: 'Bias-cut satin that falls into a soft cowl at the back.',
  ),
  (
    name: 'Velvet Cocktail Gown',
    category: 'Gowns',
    fabric: 'Velvet',
    description: 'A short velvet gown with a sculpted neckline.',
  ),
  (
    name: 'Beaded Cape Gown',
    category: 'Gowns',
    fabric: 'Organza',
    description: 'Sheer cape overlay with hand-beaded shoulders.',
  ),
  (
    name: 'Silk Trench Coat',
    category: 'Outerwear',
    fabric: 'Raw silk',
    description: 'A light trench in raw silk, unlined for the tropics.',
  ),
  (
    name: 'Handloom Cotton Jacket',
    category: 'Outerwear',
    fabric: 'Handloom cotton',
    description: 'Structured handloom cotton with a single-button close.',
  ),
  (
    name: 'Embroidered Cape',
    category: 'Outerwear',
    fabric: 'Organza',
    description: 'An open organza cape with a fine embroidered edge.',
  ),
  (
    name: 'Handloom Cotton Coat',
    category: 'Outerwear',
    fabric: 'Handloom cotton',
    description: 'A relaxed coat in undyed handloom cotton.',
  ),
  (
    name: 'Puff Sleeve Silk Blouse',
    category: 'Silk blouses',
    fabric: 'Raw silk',
    description: 'A puff-sleeve blouse cut to sit under a heavy saree.',
  ),
  (
    name: 'Chiffon Blouse',
    category: 'Silk blouses',
    fabric: 'Chiffon',
    description: 'A soft chiffon blouse with a covered button placket.',
  ),
  (
    name: 'Handloom Cotton Blouse',
    category: 'Silk blouses',
    fabric: 'Handloom cotton',
    description: 'Everyday handloom cotton with a high neck.',
  ),
  (
    name: 'Velvet Blouse',
    category: 'Silk blouses',
    fabric: 'Velvet',
    description: 'A velvet blouse with a deep back and tie detail.',
  ),
  (
    name: 'Zari Clutch',
    category: 'Accessories',
    fabric: 'Raw silk',
    description: 'A slim clutch woven with a fine gold zari.',
  ),
  (
    name: 'Beaded Potli Bag',
    category: 'Accessories',
    fabric: 'Velvet',
    description: 'A drawstring potli in velvet with beaded tassels.',
  ),
  (
    name: 'Silk Scarf',
    category: 'Accessories',
    fabric: 'Chiffon',
    description: 'A hand-rolled silk scarf in the season palette.',
  ),
  (
    name: 'Embroidered Belt',
    category: 'Accessories',
    fabric: 'Handloom cotton',
    description: 'A wide embroidered belt that cinches a saree drape.',
  ),
];
