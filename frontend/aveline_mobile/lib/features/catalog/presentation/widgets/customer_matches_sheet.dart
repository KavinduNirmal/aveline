import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../data/catalog_product_repository.dart';
import '../../domain/catalog_product.dart';
import '../../domain/customer_match.dart';

/// Luxury bottom sheet for inspecting VIP client affinity matches and launching Salon outreach.
class CustomerMatchesSheet extends StatefulWidget {
  const CustomerMatchesSheet({
    super.key,
    required this.piece,
    required this.repository,
    this.onOpenSalon,
  });

  final CatalogProduct piece;
  final CatalogProductRepository repository;
  final void Function(String customerId, String clientName)? onOpenSalon;

  static Future<void> show(
    BuildContext context, {
    required CatalogProduct piece,
    required CatalogProductRepository repository,
    void Function(String customerId, String clientName)? onOpenSalon,
  }) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (sheetContext) => CustomerMatchesSheet(
        piece: piece,
        repository: repository,
        onOpenSalon: onOpenSalon,
      ),
    );
  }

  @override
  State<CustomerMatchesSheet> createState() => _CustomerMatchesSheetState();
}

class _CustomerMatchesSheetState extends State<CustomerMatchesSheet> {
  List<CustomerMatch> _matches = const [];
  bool _isLoading = true;
  bool _isGenerating = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _loadMatches();
  }

  Future<void> _loadMatches() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final loaded = await widget.repository.getCustomerMatches(widget.piece.id);
      if (mounted) {
        setState(() {
          _matches = loaded;
          _isLoading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _errorMessage = 'Could not load VIP matches.';
          _isLoading = false;
        });
      }
    }
  }

  Future<void> _generateFreshMatches() async {
    setState(() => _isGenerating = true);
    try {
      final fresh = await widget.repository.generateCustomerMatches(widget.piece.id);
      if (mounted) {
        setState(() {
          _matches = fresh;
          _isGenerating = false;
        });
        AppToast.show(context, 'Discovered ${fresh.length} VIP client affinity matches.');
      }
    } catch (e) {
      if (mounted) {
        setState(() => _isGenerating = false);
        AppToast.show(context, 'Could not recalculate matches.', error: true);
      }
    }
  }

  Future<void> _handleAction(CustomerMatch match) async {
    try {
      await widget.repository.markMatchActed(match.id);
      if (mounted) {
        setState(() {
          _matches = _matches
              .map((m) => m.id == match.id ? m.copyWith(employeeActed: true) : m)
              .toList();
        });
        AppToast.show(
          context,
          'Salon outreach initiated for ${match.customerName}. Recommendation drafted.',
        );
        widget.onOpenSalon?.call(match.customerId, match.customerName);
      }
    } catch (_) {
      if (mounted) {
        AppToast.show(context, 'Could not update match status.', error: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final maxSheetHeight = MediaQuery.of(context).size.height * 0.88;

    return Container(
      constraints: BoxConstraints(maxHeight: maxSheetHeight),
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.15),
            blurRadius: 24,
            offset: const Offset(0, -6),
          ),
        ],
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Handle Bar
            const SizedBox(height: 12),
            Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: scheme.outlineVariant,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
            const SizedBox(height: 12),

            // Sheet Header
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Row(
                children: [
                  Container(
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      color: scheme.primary.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Icon(Icons.auto_awesome, size: 18, color: scheme.primary),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'VIP Client Matches',
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w600,
                            fontFamily: 'PlayfairDisplay',
                          ),
                        ),
                        Text(
                          'AI-curated affinities from client color and silhouette profiles',
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ),
                  ),
                  TextButton.icon(
                    key: const Key('vip_matches_recalc_btn'),
                    onPressed: _isGenerating ? null : _generateFreshMatches,
                    icon: _isGenerating
                        ? const SizedBox(
                            width: 14,
                            height: 14,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.refresh_rounded, size: 16),
                    label: Text(_isGenerating ? 'Computing' : 'Recalculate', style: const TextStyle(fontSize: 12)),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),

            // Piece Summary Card
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
                ),
                child: Row(
                  children: [
                    ClipRRect(
                      borderRadius: BorderRadius.circular(8),
                      child: SizedBox(
                        width: 46,
                        height: 46,
                        child: widget.piece.imageUrl != null && widget.piece.imageUrl!.trim().isNotEmpty
                            ? Image.network(
                                widget.piece.imageUrl!,
                                fit: BoxFit.cover,
                                errorBuilder: (context, error, stackTrace) => Container(
                                  color: scheme.surfaceContainerHighest,
                                  child: Icon(Icons.checkroom, color: scheme.primary, size: 20),
                                ),
                              )
                            : Container(
                                color: scheme.surfaceContainerHighest,
                                child: Icon(Icons.checkroom, color: scheme.primary, size: 20),
                              ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            widget.piece.name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            '${widget.piece.skuLabel} · ${widget.piece.category}',
                            style: TextStyle(
                              fontSize: 11,
                              color: scheme.onSurfaceVariant,
                              fontFamily: 'RobotoMono',
                            ),
                          ),
                        ],
                      ),
                    ),
                    Text(
                      widget.piece.priceLabel,
                      style: TextStyle(
                        fontWeight: FontWeight.bold,
                        fontSize: 14,
                        color: scheme.primary,
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
            const Divider(height: 1),

            // Matches List
            Expanded(
              child: _isLoading
                  ? const Center(child: CircularProgressIndicator())
                  : _errorMessage != null
                      ? Center(child: Text(_errorMessage!))
                      : _matches.isEmpty
                          ? _buildEmptyState(scheme)
                          : ListView.separated(
                              key: const Key('vip_matches_list_view'),
                              padding: const EdgeInsets.fromLTRB(20, 14, 20, 24),
                              itemCount: _matches.length,
                              separatorBuilder: (context, index) => const SizedBox(height: 12),
                              itemBuilder: (context, index) {
                                final match = _matches[index];
                                return _CustomerMatchCard(
                                  key: Key('vip_match_card_${match.id}'),
                                  match: match,
                                  onAction: () => _handleAction(match),
                                  scheme: scheme,
                                );
                              },
                            ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildEmptyState(ColorScheme scheme) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.favorite_border_rounded, size: 40, color: scheme.outlineVariant),
            const SizedBox(height: 10),
            const Text(
              'No VIP affinities discovered yet',
              style: TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
            ),
            const SizedBox(height: 4),
            Text(
              'When VIP clients save color/fabric preferences matching this piece, recommendations will appear here.',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
            ),
          ],
        ),
      ),
    );
  }
}

class _CustomerMatchCard extends StatelessWidget {
  const _CustomerMatchCard({
    super.key,
    required this.match,
    required this.onAction,
    required this.scheme,
  });

  final CustomerMatch match;
  final VoidCallback onAction;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
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
          // Header: Avatar, Name, Email, Match Badge
          Row(
            children: [
              CircleAvatar(
                radius: 18,
                backgroundColor: scheme.primary.withValues(alpha: 0.1),
                onBackgroundImageError: match.customerAvatar != null && match.customerAvatar!.trim().isNotEmpty
                    ? (exception, stackTrace) {}
                    : null,
                backgroundImage: match.customerAvatar != null && match.customerAvatar!.trim().isNotEmpty
                    ? NetworkImage(match.customerAvatar!)
                    : null,
                child: match.customerAvatar == null || match.customerAvatar!.trim().isEmpty
                    ? Text(
                        match.customerName.isNotEmpty ? match.customerName[0] : 'V',
                        style: TextStyle(fontWeight: FontWeight.bold, color: scheme.primary, fontSize: 13),
                      )
                    : null,
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      match.customerName,
                      style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13.5),
                    ),
                    Text(
                      match.customerEmail,
                      style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                    ),
                  ],
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: scheme.primary.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: scheme.primary.withValues(alpha: 0.25)),
                ),
                child: Text(
                  match.matchScoreLabel,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.bold,
                    color: scheme.primary,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),

          // Score Progress Bar
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: match.scoreProgress,
              backgroundColor: scheme.surfaceContainerHighest,
              valueColor: AlwaysStoppedAnimation<Color>(scheme.primary),
              minHeight: 4,
            ),
          ),
          const SizedBox(height: 10),

          // AI Affinity Reasoning
          Container(
            padding: const EdgeInsets.all(10),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerHighest.withValues(alpha: 0.4),
              borderRadius: BorderRadius.circular(10),
              border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.4)),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.auto_awesome, size: 14, color: scheme.primary),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    match.matchReason,
                    style: TextStyle(
                      fontSize: 11.5,
                      height: 1.35,
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                ),
              ],
            ),
          ),

          // Preference Chips
          if (match.preferredColor != null || match.preferredFabric != null || match.preferredSize != null) ...[
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              runSpacing: 4,
              children: [
                if (match.preferredColor != null)
                  _buildTag('Color: ${match.preferredColor}'),
                if (match.preferredFabric != null)
                  _buildTag('Fabric: ${match.preferredFabric}'),
                if (match.preferredSize != null)
                  _buildTag('Size: ${match.preferredSize}'),
              ],
            ),
          ],
          const SizedBox(height: 12),
          const Divider(height: 1),
          const SizedBox(height: 8),

          // Action Button Row
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              if (match.employeeActed)
                Row(
                  children: [
                    const Icon(Icons.check_circle, size: 14, color: Color(0xFF10B981)),
                    const SizedBox(width: 4),
                    Text(
                      'Outreach Contacted',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                )
              else
                Text(
                  'Pending outreach',
                  style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                ),
              ElevatedButton.icon(
                key: Key('vip_outreach_btn_${match.id}'),
                onPressed: onAction,
                icon: const Icon(Icons.chat_bubble_outline_rounded, size: 14),
                label: Text(
                  match.employeeActed ? 'Re-open Salon' : 'Start Outreach',
                  style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
                ),
                style: ElevatedButton.styleFrom(
                  backgroundColor: match.employeeActed ? scheme.surfaceContainerHighest : scheme.primary,
                  foregroundColor: match.employeeActed ? scheme.onSurface : scheme.onPrimary,
                  padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                  elevation: 0,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildTag(String label) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.6),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.4)),
      ),
      child: Text(
        label,
        style: TextStyle(fontSize: 9.5, color: scheme.onSurfaceVariant),
      ),
    );
  }
}
