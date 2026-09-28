import 'package:flutter/material.dart';

import '../../domain/outfit_composition.dart';
import '../../domain/outfit_item.dart';
import 'lookbook_detail_sheet.dart';

/// Luxury boutique card representing a composed lookbook / ensemble.
class LookbookCard extends StatelessWidget {
  const LookbookCard({
    super.key,
    required this.lookbook,
    required this.onEdit,
    required this.onDelete,
    this.onTap,
  });

  final OutfitComposition lookbook;
  final VoidCallback onEdit;
  final VoidCallback onDelete;
  final VoidCallback? onTap;

  void _showDetails(BuildContext context) {
    if (onTap != null) {
      onTap!();
      return;
    }

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => LookbookDetailSheet(
        lookbook: lookbook,
        onEdit: () {
          Navigator.of(context).pop();
          onEdit();
        },
        onDelete: () {
          Navigator.of(context).pop();
          onDelete();
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.8)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.04),
            blurRadius: 10,
            offset: const Offset(0, 3),
          ),
        ],
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(16),
        child: InkWell(
          onTap: () => _showDetails(context),
          borderRadius: BorderRadius.circular(16),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Top Header: Occasion + Actions Menu
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                            decoration: BoxDecoration(
                              color: scheme.primary.withValues(alpha: 0.1),
                              borderRadius: BorderRadius.circular(6),
                            ),
                            child: Text(
                              lookbook.occasion.toUpperCase(),
                              style: TextStyle(
                                fontSize: 9.5,
                                fontWeight: FontWeight.w700,
                                letterSpacing: 0.6,
                                color: scheme.primary,
                              ),
                            ),
                          ),
                          const SizedBox(height: 6),
                          Text(
                            lookbook.name,
                            style: theme.textTheme.titleMedium?.copyWith(
                              fontWeight: FontWeight.bold,
                              fontFamily: 'PlayfairDisplay',
                            ),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 8),
                    PopupMenuButton<String>(
                      key: Key('lookbook_menu_${lookbook.id}'),
                      icon: Icon(Icons.more_horiz, size: 20, color: scheme.onSurfaceVariant),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      onSelected: (action) {
                        if (action == 'view') {
                          _showDetails(context);
                        } else if (action == 'edit') {
                          onEdit();
                        } else if (action == 'delete') {
                          onDelete();
                        }
                      },
                      itemBuilder: (context) => [
                        const PopupMenuItem(
                          value: 'view',
                          child: Row(
                            children: [
                              Icon(Icons.visibility_outlined, size: 16),
                              SizedBox(width: 8),
                              Text('View Details', style: TextStyle(fontSize: 13)),
                            ],
                          ),
                        ),
                        const PopupMenuItem(
                          value: 'edit',
                          child: Row(
                            children: [
                              Icon(Icons.edit_outlined, size: 16),
                              SizedBox(width: 8),
                              Text('Edit Details', style: TextStyle(fontSize: 13)),
                            ],
                          ),
                        ),
                        PopupMenuItem(
                          value: 'delete',
                          child: Row(
                            children: [
                              Icon(Icons.delete_outline, size: 16, color: scheme.error),
                              const SizedBox(width: 8),
                              Text(
                                'Delete Lookbook',
                                style: TextStyle(fontSize: 13, color: scheme.error),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
                const SizedBox(height: 12),

                // Pieces Thumbnail Strip
                if (lookbook.items.isNotEmpty) ...[
                  SizedBox(
                    height: 72,
                    child: ListView.separated(
                      scrollDirection: Axis.horizontal,
                      itemCount: lookbook.items.length,
                      separatorBuilder: (context, index) => const SizedBox(width: 8),
                      itemBuilder: (context, index) {
                        final item = lookbook.items[index];
                        return _PieceThumbnail(item: item, scheme: scheme);
                      },
                    ),
                  ),
                  const SizedBox(height: 12),
                ],

                // Elle AI Styling Commentary Snippet
                if (lookbook.styleNotes.trim().isNotEmpty) ...[
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                    decoration: BoxDecoration(
                      color: scheme.surfaceContainerHighest.withValues(alpha: 0.35),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(Icons.auto_awesome, size: 13, color: scheme.primary),
                        const SizedBox(width: 6),
                        Expanded(
                          child: Text(
                            lookbook.styleNotes,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 11.5,
                              fontStyle: FontStyle.italic,
                              color: scheme.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                ],

                // Footer: Item Count & Total Price
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Expanded(
                      child: Text(
                        '${lookbook.items.length} ${lookbook.items.length == 1 ? 'piece' : 'pieces'} curated',
                        style: TextStyle(
                          fontSize: 11.5,
                          color: scheme.onSurfaceVariant,
                          fontWeight: FontWeight.w500,
                        ),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          'Total: ',
                          style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                        ),
                        Text(
                          'Rs ${lookbook.totalPrice.toStringAsFixed(0)}',
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.bold,
                            color: scheme.primary,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PieceThumbnail extends StatelessWidget {
  const _PieceThumbnail({required this.item, required this.scheme});

  final OutfitItem item;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 72,
      height: 72,
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(10),
        child: Stack(
          fit: StackFit.expand,
          children: [
            if (item.imageUrl.trim().isNotEmpty)
              Image.network(
                item.imageUrl,
                fit: BoxFit.cover,
                errorBuilder: (context, error, stackTrace) => _placeholder(),
              )
            else
              _placeholder(),
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
                color: Colors.black54,
                child: Text(
                  item.position.toUpperCase(),
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 8,
                    fontWeight: FontWeight.w700,
                  ),
                  textAlign: TextAlign.center,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _placeholder() {
    return Center(
      child: Icon(Icons.checkroom, size: 22, color: scheme.outlineVariant),
    );
  }
}
