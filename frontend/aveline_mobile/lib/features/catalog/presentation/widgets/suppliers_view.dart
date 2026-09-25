import 'package:flutter/material.dart';

import '../../data/catalog_product_repository.dart';
import '../../domain/supplier.dart';
import 'supplier_catalog_sheet.dart';

/// 4th Dock tab view for browsing partner craft ateliers, fabric mills, and lead times.
class SuppliersView extends StatefulWidget {
  const SuppliersView({
    super.key,
    required this.repository,
  });

  final CatalogProductRepository repository;

  @override
  State<SuppliersView> createState() => _SuppliersViewState();
}

class _SuppliersViewState extends State<SuppliersView> {
  List<Supplier> _suppliers = const [];
  bool _isLoading = true;

  @override
  void initState() {
    super.initState();
    _loadSuppliers();
  }

  Future<void> _loadSuppliers() async {
    setState(() => _isLoading = true);
    try {
      final loaded = await widget.repository.getSuppliers();
      if (mounted) {
        setState(() {
          _suppliers = loaded;
          _isLoading = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() => _isLoading = false);
      }
    }
  }

  void _openSupplierCatalog(Supplier supplier) {
    SupplierCatalogSheet.show(
      context,
      supplier: supplier,
      repository: widget.repository,
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (_isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    return RefreshIndicator(
      onRefresh: _loadSuppliers,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 14, 20, 140),
        children: [
          // Section Header
          Text(
            'Partner Ateliers & Heritage Fabric Mills',
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w600,
              fontFamily: 'PlayfairDisplay',
            ),
          ),
          const SizedBox(height: 2),
          Text(
            'Direct supplier integrations for handloom silks, zari embroidery, and bespoke commissions',
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 16),

          if (_suppliers.isEmpty)
            _buildEmptyState(theme, scheme)
          else
            for (final supplier in _suppliers) ...[
              _AtelierCard(
                key: Key('supplier_card_${supplier.id}'),
                supplier: supplier,
                onViewCatalog: () => _openSupplierCatalog(supplier),
                scheme: scheme,
                theme: theme,
              ),
              const SizedBox(height: 14),
            ],
        ],
      ),
    );
  }

  Widget _buildEmptyState(ThemeData theme, ColorScheme scheme) {
    return Container(
      padding: const EdgeInsets.all(32),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Column(
        children: [
          Icon(Icons.business_outlined, size: 40, color: scheme.outlineVariant),
          const SizedBox(height: 12),
          Text('No Partner Ateliers Found', style: theme.textTheme.titleMedium),
          const SizedBox(height: 4),
          Text(
            'Connect your heritage suppliers and fabric mills to track sourcing lead times and minimum orders.',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
          ),
        ],
      ),
    );
  }
}

class _AtelierCard extends StatelessWidget {
  const _AtelierCard({
    super.key,
    required this.supplier,
    required this.onViewCatalog,
    required this.scheme,
    required this.theme,
  });

  final Supplier supplier;
  final VoidCallback onViewCatalog;
  final ColorScheme scheme;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.04),
            blurRadius: 10,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Header: Icon + Status Pill
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Container(
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  color: scheme.primary.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Icon(Icons.business_rounded, size: 20, color: scheme.primary),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: supplier.isActive
                      ? const Color(0xFF10B981).withValues(alpha: 0.12)
                      : scheme.surfaceContainerHighest,
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(
                    color: supplier.isActive
                        ? const Color(0xFF10B981).withValues(alpha: 0.3)
                        : scheme.outlineVariant,
                  ),
                ),
                child: Text(
                  supplier.isActive ? 'Active Partner' : 'Inactive',
                  style: TextStyle(
                    fontSize: 10.5,
                    fontWeight: FontWeight.bold,
                    color: supplier.isActive ? const Color(0xFF059669) : scheme.onSurfaceVariant,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          // Name & Location
          Text(
            supplier.name,
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.bold,
              fontSize: 15,
            ),
          ),
          const SizedBox(height: 2),
          Row(
            children: [
              Icon(Icons.location_on_outlined, size: 14, color: scheme.primary),
              const SizedBox(width: 4),
              Text(
                supplier.location,
                style: TextStyle(fontSize: 11.5, color: scheme.onSurfaceVariant),
              ),
            ],
          ),
          const SizedBox(height: 10),

          // Craft Specialty Banner
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(10),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerHighest.withValues(alpha: 0.5),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'CRAFT SPECIALTY:',
                  style: TextStyle(
                    fontSize: 9.5,
                    fontWeight: FontWeight.bold,
                    letterSpacing: 0.6,
                    color: scheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  supplier.specialty,
                  style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w500),
                ),
              ],
            ),
          ),
          const SizedBox(height: 10),

          // Contact Details
          if (supplier.contactEmail != null || supplier.phone != null) ...[
            if (supplier.contactEmail != null) ...[
              Row(
                children: [
                  Icon(Icons.mail_outline_rounded, size: 13, color: scheme.onSurfaceVariant),
                  const SizedBox(width: 6),
                  Text(
                    supplier.contactEmail!,
                    style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                  ),
                ],
              ),
              const SizedBox(height: 4),
            ],
            if (supplier.phone != null) ...[
              Row(
                children: [
                  Icon(Icons.phone_outlined, size: 13, color: scheme.onSurfaceVariant),
                  const SizedBox(width: 6),
                  Text(
                    supplier.phone!,
                    style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                  ),
                ],
              ),
              const SizedBox(height: 8),
            ],
          ],

          // Operational Metrics Grid (Lead Time & MOQ)
          Row(
            children: [
              Expanded(
                child: Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: scheme.surface,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.4)),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Lead Time', style: TextStyle(fontSize: 9.5, color: scheme.onSurfaceVariant)),
                      const SizedBox(height: 2),
                      Row(
                        children: [
                          Icon(Icons.schedule_rounded, size: 12, color: scheme.primary),
                          const SizedBox(width: 4),
                          Text(
                            supplier.leadTimeLabel,
                            style: const TextStyle(fontSize: 11.5, fontWeight: FontWeight.bold),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: scheme.surface,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.4)),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Min Order (MOQ)', style: TextStyle(fontSize: 9.5, color: scheme.onSurfaceVariant)),
                      const SizedBox(height: 2),
                      Row(
                        children: [
                          Icon(Icons.payments_outlined, size: 12, color: scheme.primary),
                          const SizedBox(width: 4),
                          Text(
                            supplier.minimumOrderLabel,
                            style: const TextStyle(fontSize: 11.5, fontWeight: FontWeight.bold),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          // Action: View Atelier Catalog
          SizedBox(
            width: double.infinity,
            child: OutlinedButton.icon(
              key: Key('view_supplier_catalog_btn_${supplier.id}'),
              onPressed: onViewCatalog,
              icon: const Icon(Icons.layers_outlined, size: 16),
              label: Text(
                'View Atelier Catalog (${supplier.sampleCatalogCount} pieces)',
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
              ),
              style: OutlinedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 10),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
