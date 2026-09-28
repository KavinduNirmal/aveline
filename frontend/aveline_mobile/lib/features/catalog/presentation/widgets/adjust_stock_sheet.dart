import 'package:flutter/material.dart';

import '../../data/catalog_product_repository.dart';
import '../../domain/catalog_product.dart';
import '../../domain/stock_adjustment_mode.dart';
import 'catalog_product_image.dart';

/// Modal bottom sheet for adjusting stock quantities or marking pieces out of stock.
///
/// Modifies the catalog count without creating financial sales transactions or takings journal rows.
class AdjustStockSheet extends StatefulWidget {
  const AdjustStockSheet({
    super.key,
    required this.piece,
    required this.mode,
    required this.repository,
  });

  final CatalogProduct piece;
  final StockAdjustmentMode mode;
  final CatalogProductRepository repository;

  /// Convenience helper to display the sheet modally.
  static Future<CatalogProduct?> show(
    BuildContext context, {
    required CatalogProduct piece,
    required StockAdjustmentMode mode,
    required CatalogProductRepository repository,
  }) {
    return showModalBottomSheet<CatalogProduct>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => AdjustStockSheet(
        piece: piece,
        mode: mode,
        repository: repository,
      ),
    );
  }

  @override
  State<AdjustStockSheet> createState() => _AdjustStockSheetState();
}

class _AdjustStockSheetState extends State<AdjustStockSheet> {
  late final TextEditingController _reduceByController;

  bool _isSaving = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _reduceByController = TextEditingController(text: '1');
    _reduceByController.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _reduceByController.dispose();
    super.dispose();
  }

  bool get _isOutOfStock => widget.mode == StockAdjustmentMode.outOfStock;

  int get _parsedReduceBy => int.tryParse(_reduceByController.text.trim()) ?? 0;

  bool get _reduceValid =>
      _parsedReduceBy > 0 && _parsedReduceBy <= widget.piece.quantity;

  int get _nextQuantity => _isOutOfStock
      ? 0
      : (widget.piece.quantity - (_reduceValid ? _parsedReduceBy : 0))
          .clamp(0, widget.piece.quantity);

  bool get _canSubmit =>
      _isOutOfStock ? widget.piece.quantity > 0 && !_isSaving : _reduceValid && !_isSaving;

  void _incrementReduce() {
    final current = _parsedReduceBy;
    if (current < widget.piece.quantity) {
      _reduceByController.text = (current + 1).toString();
    }
  }

  void _decrementReduce() {
    final current = _parsedReduceBy;
    if (current > 1) {
      _reduceByController.text = (current - 1).toString();
    }
  }

  Future<void> _submit() async {
    if (!_canSubmit) return;

    setState(() {
      _isSaving = true;
      _errorMessage = null;
    });

    try {
      final targetQuantity = _nextQuantity;
      final targetStatus = targetQuantity <= 0
          ? CatalogItemStatus.soldOut
          : widget.piece.status;

      final updated = await widget.repository.adjustStock(
        itemId: widget.piece.id,
        quantity: targetQuantity,
        status: targetStatus,
      );

      if (mounted) {
        Navigator.of(context).pop(updated);
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSaving = false;
          _errorMessage = 'Could not adjust stock: $e';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final viewInsets = MediaQuery.of(context).viewInsets;

    final isOut = _isOutOfStock;
    final iconColor = isOut ? scheme.error : scheme.primary;

    return Container(
      constraints: BoxConstraints(
        maxHeight: MediaQuery.of(context).size.height * 0.85,
      ),
      padding: EdgeInsets.only(bottom: viewInsets.bottom),
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 12),
            // Drag Handle
            Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: scheme.outlineVariant,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
            const SizedBox(height: 12),

            // Header
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Row(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    decoration: BoxDecoration(
                      color: iconColor.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(
                        color: iconColor.withValues(alpha: 0.25),
                      ),
                    ),
                    child: Icon(
                      isOut ? Icons.inventory_2_outlined : Icons.tune_rounded,
                      size: 20,
                      color: iconColor,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          isOut ? 'Mark Out of Stock' : 'Reduce Stock',
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.bold,
                            fontFamily: 'PlayfairDisplay',
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        Text(
                          'Edits the catalog count only. No sale is recorded.',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                            fontSize: 11,
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    icon: const Icon(Icons.close, size: 20),
                    onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
                    tooltip: 'Close',
                  ),
                ],
              ),
            ),
            const Divider(height: 20),

            // Scrollable Content
            Flexible(
              child: ListView(
                padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 4),
                shrinkWrap: true,
                children: [
                  // Piece Summary Card
                  _buildPieceSummary(scheme, theme),
                  const SizedBox(height: 16),

                  if (isOut)
                    _buildOutOfStockWarning(scheme, theme)
                  else
                    _buildReduceSection(scheme, theme),

                  const SizedBox(height: 16),

                  // Stock Before vs Stock After Summary
                  _buildStockComparisonBox(scheme, theme),

                  if (_errorMessage != null) ...[
                    const SizedBox(height: 12),
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: scheme.error.withValues(alpha: 0.1),
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(
                          color: scheme.error.withValues(alpha: 0.3),
                        ),
                      ),
                      child: Row(
                        children: [
                          Icon(Icons.error_outline, color: scheme.error, size: 18),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              _errorMessage!,
                              style: TextStyle(
                                color: scheme.error,
                                fontSize: 12,
                                fontWeight: FontWeight.w500,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],

                  const SizedBox(height: 20),
                ],
              ),
            ),

            // Footer Actions
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 10, 20, 16),
              child: Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      child: const Text('Cancel'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    flex: 2,
                    child: FilledButton.icon(
                      key: const Key('adjust_stock_confirm_button'),
                      onPressed: _canSubmit ? _submit : null,
                      style: FilledButton.styleFrom(
                        backgroundColor: isOut
                            ? scheme.error
                            : const Color(0xFF8B2E42),
                        foregroundColor: Colors.white,
                        padding: const EdgeInsets.symmetric(vertical: 14),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      icon: _isSaving
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                valueColor:
                                    AlwaysStoppedAnimation<Color>(Colors.white),
                              ),
                            )
                          : const Icon(Icons.check, size: 18),
                      label: Text(
                        _isSaving
                            ? 'Saving...'
                            : isOut
                                ? 'Mark Out of Stock'
                                : 'Reduce Stock',
                        style: const TextStyle(fontWeight: FontWeight.bold),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildPieceSummary(ColorScheme scheme, ThemeData theme) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.35),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.5)),
      ),
      child: Row(
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(10),
            child: SizedBox(
              width: 50,
              height: 50,
              child: CatalogProductImage(product: widget.piece, iconSize: 22),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  widget.piece.name,
                  style: theme.textTheme.titleSmall?.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                const SizedBox(height: 2),
                Text(
                  '${widget.piece.skuLabel} · ${widget.piece.category}',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    fontSize: 11,
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                const SizedBox(height: 2),
                Text(
                  '${widget.piece.quantity} in stock',
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w600,
                    color: scheme.primary,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildOutOfStockWarning(ColorScheme scheme, ThemeData theme) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: scheme.error.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: scheme.error.withValues(alpha: 0.25)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.warning_amber_rounded, size: 20, color: scheme.error),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              'The count becomes 0 and the piece is marked Sold out, so it stops being offered on the floor. Set a new count from the edit screen when it returns.',
              style: TextStyle(
                fontSize: 12,
                color: scheme.error,
                height: 1.35,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildReduceSection(ColorScheme scheme, ThemeData theme) {
    final qty = _parsedReduceBy;
    final maxQty = widget.piece.quantity;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Flexible(
              child: Text(
                'PIECES TO REMOVE',
                style: theme.textTheme.labelSmall?.copyWith(
                  letterSpacing: 1.1,
                  fontWeight: FontWeight.bold,
                  color: scheme.onSurfaceVariant,
                ),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                'Max $maxQty on hand',
                style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            // Decrement Button
            IconButton.filledTonal(
              key: const Key('adjust_stock_qty_decrement'),
              onPressed: qty > 1 ? _decrementReduce : null,
              icon: const Icon(Icons.remove, size: 18),
              style: IconButton.styleFrom(
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
              ),
            ),
            const SizedBox(width: 8),
            // Direct Text Input
            Expanded(
              child: TextFormField(
                key: const Key('adjust_stock_quantity_input'),
                controller: _reduceByController,
                keyboardType: TextInputType.number,
                textAlign: TextAlign.center,
                style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
                decoration: InputDecoration(
                  contentPadding: const EdgeInsets.symmetric(vertical: 12),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(10),
                  ),
                  filled: true,
                  fillColor: scheme.surfaceContainerHighest.withValues(alpha: 0.2),
                ),
              ),
            ),
            const SizedBox(width: 8),
            // Increment Button
            IconButton.filledTonal(
              key: const Key('adjust_stock_qty_increment'),
              onPressed: qty < maxQty ? _incrementReduce : null,
              icon: const Icon(Icons.add, size: 18),
              style: IconButton.styleFrom(
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Text(
          'Damaged, lost, returned to the atelier, or a corrected count.',
          style: theme.textTheme.bodySmall?.copyWith(
            color: scheme.onSurfaceVariant,
            fontSize: 11,
          ),
        ),
      ],
    );
  }

  Widget _buildStockComparisonBox(ColorScheme scheme, ThemeData theme) {
    final before = widget.piece.quantity;
    final after = _nextQuantity;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.35),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.5)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Flexible(
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Flexible(
                  child: Text(
                    'BEFORE: ',
                    style: theme.textTheme.labelSmall?.copyWith(
                      letterSpacing: 0.8,
                      fontSize: 10,
                      fontWeight: FontWeight.bold,
                      color: scheme.onSurfaceVariant,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                Text(
                  before.toString(),
                  style: const TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 14,
                  ),
                ),
              ],
            ),
          ),
          const Padding(
            padding: EdgeInsets.symmetric(horizontal: 4),
            child: Icon(Icons.arrow_forward_rounded, size: 16),
          ),
          Flexible(
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Flexible(
                  child: Text(
                    'AFTER: ',
                    style: theme.textTheme.labelSmall?.copyWith(
                      letterSpacing: 0.8,
                      fontSize: 10,
                      fontWeight: FontWeight.bold,
                      color: scheme.onSurfaceVariant,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                Text(
                  after.toString(),
                  key: const Key('adjust_stock_after_value'),
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 14,
                    color: after == 0 ? scheme.error : scheme.primary,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
