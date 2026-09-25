import 'package:flutter/material.dart';

import '../../domain/catalog_product.dart';

/// Statistical KPI overview tiles on top of the Pieces catalog tab.
class CatalogKpiCards extends StatelessWidget {
  const CatalogKpiCards({
    super.key,
    required this.products,
    this.isLoading = false,
  });

  final List<CatalogProduct> products;
  final bool isLoading;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final totalPieces = products.length;
    final totalUnits = products.fold<int>(0, (sum, p) => sum + p.quantity);
    final totalValuation = products.fold<double>(0.0, (sum, p) => sum + (p.price * p.quantity));
    final lowStockCount = products.where((p) => p.isLowStock || (p.quantity > 0 && p.quantity <= 2)).length;

    String formatValuation(double val) {
      if (val >= 10000000) {
        return 'Rs ${(val / 10000000).toStringAsFixed(1)}Cr';
      } else if (val >= 100000) {
        return 'Rs ${(val / 100000).toStringAsFixed(1)}L';
      } else if (val >= 1000) {
        return 'Rs ${(val / 1000).toStringAsFixed(0)}k';
      }
      return 'Rs ${val.toStringAsFixed(0)}';
    }

    return SizedBox(
      height: 94,
      child: ListView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 20),
        children: [
          // 1. Pieces & Units
          _KpiTile(
            key: const Key('catalog_kpi_pieces'),
            icon: Icons.checkroom_rounded,
            label: 'PIECES',
            value: '$totalPieces',
            subtitle: '$totalUnits units in stock',
            scheme: scheme,
            theme: theme,
          ),
          const SizedBox(width: 10),

          // 2. Stock Valuation
          _KpiTile(
            key: const Key('catalog_kpi_valuation'),
            icon: Icons.trending_up_rounded,
            label: 'STOCK VALUE',
            value: formatValuation(totalValuation),
            subtitle: 'At recorded price',
            scheme: scheme,
            theme: theme,
          ),
          const SizedBox(width: 10),

          // 3. Low Stock Alert
          _KpiTile(
            key: const Key('catalog_kpi_low_stock'),
            icon: Icons.warning_amber_rounded,
            label: 'LOW STOCK',
            value: '$lowStockCount',
            subtitle: lowStockCount > 0 ? '<= 2 units left' : 'All well stocked',
            isWarning: lowStockCount > 0,
            scheme: scheme,
            theme: theme,
          ),
        ],
      ),
    );
  }
}

class _KpiTile extends StatelessWidget {
  const _KpiTile({
    super.key,
    required this.icon,
    required this.label,
    required this.value,
    required this.subtitle,
    this.isWarning = false,
    required this.scheme,
    required this.theme,
  });

  final IconData icon;
  final String label;
  final String value;
  final String subtitle;
  final bool isWarning;
  final ColorScheme scheme;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    final warningColor = const Color(0xFFD97706);
    final warningBg = const Color(0xFFFEF3C7);

    return Container(
      width: 140,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: isWarning ? warningBg.withValues(alpha: 0.3) : scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: isWarning
              ? warningColor.withValues(alpha: 0.4)
              : scheme.outlineVariant.withValues(alpha: 0.6),
        ),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.03),
            blurRadius: 8,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Row(
            children: [
              Icon(
                icon,
                size: 14,
                color: isWarning ? warningColor : scheme.primary,
              ),
              const SizedBox(width: 4),
              Expanded(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 9,
                    fontWeight: FontWeight.w800,
                    letterSpacing: 0.8,
                    color: isWarning ? warningColor : scheme.onSurfaceVariant,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            value,
            style: TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: isWarning ? warningColor : scheme.onSurface,
            ),
          ),
          Text(
            subtitle,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 10,
              color: isWarning ? warningColor.withValues(alpha: 0.8) : scheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}
