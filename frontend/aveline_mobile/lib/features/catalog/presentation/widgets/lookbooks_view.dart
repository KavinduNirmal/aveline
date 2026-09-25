import 'package:flutter/material.dart';

import '../../data/catalog_product_repository.dart';
import '../../domain/outfit_composition.dart';
import '../lookbooks_controller.dart';
import '../screens/compose_outfit_screen.dart';
import 'edit_lookbook_dialog.dart';
import 'lookbook_card.dart';
import 'occasion_filter_chips.dart';

/// Main Lookbooks & Ensembles view embedding occasion filtering, ensemble list, and AI composition.
class LookbooksView extends StatefulWidget {
  const LookbooksView({
    super.key,
    required this.repository,
    this.searchQuery = '',
  });

  final CatalogProductRepository repository;
  final String searchQuery;

  @override
  State<LookbooksView> createState() => _LookbooksViewState();
}

class _LookbooksViewState extends State<LookbooksView> {
  late final LookbooksController _controller;

  @override
  void initState() {
    super.initState();
    _controller = LookbooksController(widget.repository);
    _controller.setSearchQuery(widget.searchQuery);
    _controller.loadLookbooks();
  }

  @override
  void didUpdateWidget(covariant LookbooksView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.searchQuery != widget.searchQuery) {
      _controller.setSearchQuery(widget.searchQuery);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _openCompose() async {
    final result = await Navigator.of(context).push<OutfitComposition>(
      MaterialPageRoute(
        builder: (_) => ComposeOutfitScreen(repository: widget.repository),
      ),
    );

    if (result != null) {
      _controller.loadLookbooks();
    }
  }

  Future<void> _handleEdit(OutfitComposition lookbook) async {
    await showDialog<void>(
      context: context,
      builder: (_) => EditLookbookDialog(
        lookbook: lookbook,
        onSave: (payload) => _controller.updateLookbook(lookbook.id, payload),
      ),
    );
  }

  Future<void> _handleDelete(OutfitComposition lookbook) async {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        title: const Text('Delete Lookbook?'),
        content: Text(
          'Are you sure you want to remove "${lookbook.name}" from the boutique collection?',
          style: const TextStyle(fontSize: 14),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: const Text('Cancel'),
          ),
          ElevatedButton(
            key: const Key('confirm_delete_lookbook_button'),
            style: ElevatedButton.styleFrom(
              backgroundColor: scheme.error,
              foregroundColor: scheme.onError,
            ),
            onPressed: () => Navigator.of(ctx).pop(true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );

    if (confirmed == true) {
      await _controller.deleteLookbook(lookbook.id);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return ListenableBuilder(
      listenable: _controller,
      builder: (context, _) {
        final filtered = _controller.filteredLookbooks;

        return RefreshIndicator(
          onRefresh: _controller.loadLookbooks,
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 100),
            children: [
              // Top Bar: Occasion Filters + Compose Button
              Row(
                children: [
                  Expanded(
                    child: OccasionFilterChips(
                      selectedOccasion: _controller.selectedOccasion,
                      onOccasionSelected: _controller.setOccasion,
                    ),
                  ),
                  const SizedBox(width: 8),
                  IconButton.filled(
                    key: const Key('lookbooks_compose_header_button'),
                    onPressed: _openCompose,
                    icon: const Icon(Icons.auto_awesome, size: 18),
                    tooltip: 'Compose with Elle',
                    style: IconButton.styleFrom(
                      backgroundColor: scheme.primary,
                      foregroundColor: scheme.onPrimary,
                      padding: const EdgeInsets.all(8),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),

              if (_controller.isLoading && _controller.lookbooks.isEmpty) ...[
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 48),
                  child: Center(child: CircularProgressIndicator()),
                ),
              ] else if (_controller.error != null && _controller.lookbooks.isEmpty) ...[
                _buildErrorState(scheme),
              ] else if (filtered.isEmpty) ...[
                _buildEmptyState(theme, scheme),
              ] else ...[
                for (final look in filtered) ...[
                  LookbookCard(
                    key: Key('lookbook_card_${look.id}'),
                    lookbook: look,
                    onEdit: () => _handleEdit(look),
                    onDelete: () => _handleDelete(look),
                  ),
                  const SizedBox(height: 14),
                ],
              ],
            ],
          ),
        );
      },
    );
  }

  Widget _buildEmptyState(ThemeData theme, ColorScheme scheme) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 48),
      margin: const EdgeInsets.only(top: 12),
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: scheme.outlineVariant,
          style: BorderStyle.solid,
        ),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: scheme.primary.withValues(alpha: 0.1),
              shape: BoxShape.circle,
            ),
            child: Icon(Icons.style_outlined, size: 36, color: scheme.primary),
          ),
          const SizedBox(height: 16),
          Text(
            'No Lookbooks Found',
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w600,
              fontFamily: 'PlayfairDisplay',
            ),
          ),
          const SizedBox(height: 6),
          Text(
            'Curate your first styled ensemble with Elle to present complete luxury looks to your clients.',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 12.5,
              color: scheme.onSurfaceVariant,
              height: 1.4,
            ),
          ),
          const SizedBox(height: 20),
          ElevatedButton.icon(
            key: const Key('lookbooks_empty_compose_button'),
            onPressed: _openCompose,
            icon: const Icon(Icons.auto_awesome, size: 16),
            label: const Text('Compose First Look'),
            style: ElevatedButton.styleFrom(
              backgroundColor: scheme.primary,
              foregroundColor: scheme.onPrimary,
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildErrorState(ColorScheme scheme) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: BorderRadius.circular(16),
      ),
      child: Column(
        children: [
          Text(
            _controller.error!,
            style: TextStyle(color: scheme.onErrorContainer, fontSize: 13),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 12),
          ElevatedButton(
            onPressed: _controller.loadLookbooks,
            child: const Text('Retry'),
          ),
        ],
      ),
    );
  }
}
