import 'package:flutter/material.dart';

import '../../../../shared/utils/currency_formatter.dart';
import '../../data/catalog_product_repository.dart';
import '../../domain/catalog_product.dart';
import '../../domain/sale_payloads.dart';
import '../../domain/sale_receipt.dart';
import 'catalog_product_image.dart';

/// Modal bottom sheet for recording an in-store counter sale.
///
/// Decrements on-hand inventory and commits a sale record to the takings journal.
class RecordSaleSheet extends StatefulWidget {
  const RecordSaleSheet({
    super.key,
    required this.piece,
    required this.repository,
  });

  final CatalogProduct piece;
  final CatalogProductRepository repository;

  /// Convenience helper to display the sheet modally.
  static Future<CatalogSaleReceipt?> show(
    BuildContext context, {
    required CatalogProduct piece,
    required CatalogProductRepository repository,
  }) {
    return showModalBottomSheet<CatalogSaleReceipt>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => RecordSaleSheet(piece: piece, repository: repository),
    );
  }

  @override
  State<RecordSaleSheet> createState() => _RecordSaleSheetState();
}

class _RecordSaleSheetState extends State<RecordSaleSheet> {
  late final TextEditingController _quantityController;
  late final TextEditingController _unitPriceController;
  late final TextEditingController _noteController;

  bool _isSaving = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _quantityController = TextEditingController(text: '1');
    _unitPriceController = TextEditingController(
      text: widget.piece.price > 0 ? widget.piece.price.toStringAsFixed(0) : '0',
    );
    _noteController = TextEditingController();

    _quantityController.addListener(() => setState(() {}));
    _unitPriceController.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _quantityController.dispose();
    _unitPriceController.dispose();
    _noteController.dispose();
    super.dispose();
  }

  int get _parsedQuantity => int.tryParse(_quantityController.text.trim()) ?? 0;
  double get _parsedUnitPrice =>
      double.tryParse(_unitPriceController.text.trim()) ?? 0.0;

  bool get _quantityValid =>
      _parsedQuantity > 0 && _parsedQuantity <= widget.piece.quantity;

  bool get _priceValid => _parsedUnitPrice > 0;

  bool get _canSubmit => _quantityValid && _priceValid && !_isSaving;

  double get _totalAmount =>
      _quantityValid && _priceValid ? _parsedQuantity * _parsedUnitPrice : 0.0;

  int get _remainingStock => _quantityValid
      ? (widget.piece.quantity - _parsedQuantity).clamp(0, widget.piece.quantity)
      : widget.piece.quantity;

  void _incrementQuantity() {
    final current = _parsedQuantity;
    if (current < widget.piece.quantity) {
      _quantityController.text = (current + 1).toString();
    }
  }

  void _decrementQuantity() {
    final current = _parsedQuantity;
    if (current > 1) {
      _quantityController.text = (current - 1).toString();
    }
  }

  void _applyDiscount(double percentage) {
    final original = widget.piece.price;
    final discounted = (original * (1.0 - (percentage / 100.0))).roundToDouble();
    _unitPriceController.text = discounted.toStringAsFixed(0);
  }

  void _applyTagPrice() {
    _unitPriceController.text = widget.piece.price.toStringAsFixed(0);
  }

  Future<void> _submit() async {
    if (!_canSubmit) return;

    setState(() {
      _isSaving = true;
      _errorMessage = null;
    });

    try {
      final payload = RecordSalePayload(
        quantity: _parsedQuantity,
        unitPrice: _parsedUnitPrice,
        note: _noteController.text.trim(),
      );

      final receipt = await widget.repository.recordSale(
        itemId: widget.piece.id,
        payload: payload,
      );

      if (mounted) {
        Navigator.of(context).pop(receipt);
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSaving = false;
          _errorMessage = e.toString().contains('insufficient-stock')
              ? 'Insufficient stock on hand to record this sale.'
              : 'Could not record sale: $e';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final viewInsets = MediaQuery.of(context).viewInsets;

    return Container(
      constraints: BoxConstraints(
        maxHeight: MediaQuery.of(context).size.height * 0.88,
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
                      color: scheme.primary.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(
                        color: scheme.primary.withValues(alpha: 0.25),
                      ),
                    ),
                    child: Icon(
                      Icons.receipt_long_outlined,
                      size: 20,
                      color: scheme.primary,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Record Counter Sale',
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.bold,
                            fontFamily: 'PlayfairDisplay',
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        Text(
                          'Stock is decremented and takings journal is updated',
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

                  // Quantity Stepper
                  _buildQuantitySection(scheme, theme),
                  const SizedBox(height: 16),

                  // Unit Price Input & Quick Discount Pills
                  _buildPriceSection(scheme, theme),
                  const SizedBox(height: 16),

                  // Optional Note
                  _buildNoteSection(scheme, theme),
                  const SizedBox(height: 16),

                  // Live Calculation Summary
                  _buildCalculationBox(scheme, theme),

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
                      key: const Key('record_sale_confirm_button'),
                      onPressed: _canSubmit ? _submit : null,
                      style: FilledButton.styleFrom(
                        backgroundColor: const Color(0xFF8B2E42),
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
                        _isSaving ? 'Recording...' : 'Record Sale',
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
              width: 54,
              height: 54,
              child: CatalogProductImage(product: widget.piece, iconSize: 24),
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
                const SizedBox(height: 3),
                Row(
                  children: [
                    Text(
                      '${widget.piece.quantity} in stock',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: widget.piece.isLowStock
                            ? scheme.error
                            : scheme.primary,
                      ),
                    ),
                    Flexible(
                      child: Text(
                        ' · Tag: ${widget.piece.priceLabel}',
                        style: TextStyle(
                          fontSize: 11,
                          color: scheme.onSurfaceVariant,
                        ),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildQuantitySection(ColorScheme scheme, ThemeData theme) {
    final qty = _parsedQuantity;
    final maxQty = widget.piece.quantity;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Flexible(
              child: Text(
                'PIECES SOLD',
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
              key: const Key('record_sale_qty_decrement'),
              onPressed: qty > 1 ? _decrementQuantity : null,
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
                key: const Key('record_sale_quantity_input'),
                controller: _quantityController,
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
              key: const Key('record_sale_qty_increment'),
              onPressed: qty < maxQty ? _incrementQuantity : null,
              icon: const Icon(Icons.add, size: 18),
              style: IconButton.styleFrom(
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
              ),
            ),
          ],
        ),
        if (qty > maxQty)
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: Text(
              'Only $maxQty pieces are in stock.',
              style: TextStyle(color: scheme.error, fontSize: 11),
            ),
          ),
      ],
    );
  }

  Widget _buildPriceSection(ColorScheme scheme, ThemeData theme) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Flexible(
              child: Text(
                'UNIT PRICE (LKR)',
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
                'Editable for negotiations',
                style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        TextFormField(
          key: const Key('record_sale_price_input'),
          controller: _unitPriceController,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
          decoration: InputDecoration(
            prefixText: 'Rs. ',
            prefixStyle: TextStyle(
              color: scheme.primary,
              fontWeight: FontWeight.bold,
            ),
            contentPadding:
                const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(10),
            ),
            filled: true,
            fillColor: scheme.surfaceContainerHighest.withValues(alpha: 0.2),
          ),
        ),
        const SizedBox(height: 8),
        // Quick Discount Shortcut Pills
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            children: [
              _buildPriceShortcutChip('Tag price', _applyTagPrice, scheme),
              const SizedBox(width: 6),
              _buildPriceShortcutChip('-5%', () => _applyDiscount(5), scheme),
              const SizedBox(width: 6),
              _buildPriceShortcutChip('-10%', () => _applyDiscount(10), scheme),
              const SizedBox(width: 6),
              _buildPriceShortcutChip('-15%', () => _applyDiscount(15), scheme),
              if (widget.piece.hasDiscountRange) ...[
                const SizedBox(width: 6),
                _buildPriceShortcutChip(
                  'Floor (${widget.piece.discountMaxPercent}%)',
                  () => _applyDiscount(widget.piece.discountMaxPercent.toDouble()),
                  scheme,
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildPriceShortcutChip(
    String label,
    VoidCallback onTap,
    ColorScheme scheme,
  ) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(8),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
        decoration: BoxDecoration(
          color: scheme.primary.withValues(alpha: 0.08),
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: scheme.primary.withValues(alpha: 0.2)),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: scheme.primary,
          ),
        ),
      ),
    );
  }

  Widget _buildNoteSection(ColorScheme scheme, ThemeData theme) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'NOTE / CLIENT (OPTIONAL)',
          style: theme.textTheme.labelSmall?.copyWith(
            letterSpacing: 1.1,
            fontWeight: FontWeight.bold,
            color: scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 8),
        TextFormField(
          key: const Key('record_sale_note_input'),
          controller: _noteController,
          maxLines: 2,
          style: const TextStyle(fontSize: 13),
          decoration: InputDecoration(
            hintText: 'Client name, alterations, or register note',
            hintStyle: TextStyle(
              fontSize: 12,
              color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
            ),
            contentPadding: const EdgeInsets.all(12),
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(10),
            ),
            filled: true,
            fillColor: scheme.surfaceContainerHighest.withValues(alpha: 0.2),
          ),
        ),
      ],
    );
  }

  Widget _buildCalculationBox(ColorScheme scheme, ThemeData theme) {
    final remaining = _remainingStock;

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.35),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.5)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'SALE TOTAL',
                style: theme.textTheme.labelSmall?.copyWith(
                  letterSpacing: 1.1,
                  fontSize: 10,
                  fontWeight: FontWeight.bold,
                  color: scheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 2),
              Text(
                rupees(_totalAmount),
                key: const Key('record_sale_total_amount'),
                style: TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.bold,
                  fontFamily: 'PlayfairDisplay',
                  color: const Color(0xFF8B2E42),
                ),
              ),
            ],
          ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                'STOCK AFTER',
                style: theme.textTheme.labelSmall?.copyWith(
                  letterSpacing: 1.1,
                  fontSize: 10,
                  fontWeight: FontWeight.bold,
                  color: scheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 2),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    remaining.toString(),
                    key: const Key('record_sale_stock_after'),
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.bold,
                      color: remaining == 0 ? scheme.error : scheme.onSurface,
                    ),
                  ),
                  if (remaining == 0) ...[
                    const SizedBox(width: 4),
                    Text(
                      '(Sold Out)',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: scheme.error,
                      ),
                    ),
                  ],
                ],
              ),
            ],
          ),
        ],
      ),
    );
  }
}
