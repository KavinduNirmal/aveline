import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/catalog_product.dart';
import '../../domain/item_qr_payload.dart';

/// Luxury bottom sheet displaying the designer physical floor tag preview and QR code formatting options.
class ItemQrSheet extends StatefulWidget {
  const ItemQrSheet({
    super.key,
    required this.piece,
    this.organizationId,
    this.organizationSlug,
  });

  final CatalogProduct piece;
  final String? organizationId;
  final String? organizationSlug;

  static Future<void> show(
    BuildContext context, {
    required CatalogProduct piece,
    String? organizationId,
    String? organizationSlug,
  }) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (sheetContext) => ItemQrSheet(
        piece: piece,
        organizationId: organizationId,
        organizationSlug: organizationSlug,
      ),
    );
  }

  @override
  State<ItemQrSheet> createState() => _ItemQrSheetState();
}

class _ItemQrSheetState extends State<ItemQrSheet> {
  QrFormatType _format = QrFormatType.json;
  bool _copied = false;

  String get _currentPayload => ItemQrPayloadBuilder.resolvePayload(
        format: _format,
        product: widget.piece,
        organizationId: widget.organizationId,
        organizationSlug: widget.organizationSlug,
      );

  Future<void> _handleCopy() async {
    await Clipboard.setData(ClipboardData(text: _currentPayload));
    if (!mounted) return;
    setState(() => _copied = true);
    AppToast.show(context, 'QR payload copied (${_format.label}).');
    Future.delayed(const Duration(seconds: 2), () {
      if (mounted) setState(() => _copied = false);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final maxSheetHeight = MediaQuery.of(context).size.height * 0.90;

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
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Handle Bar
              Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: scheme.outlineVariant,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
              const SizedBox(height: 14),

              // Title Row
              Row(
                children: [
                  Container(
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      color: scheme.primary.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Icon(Icons.qr_code_2_rounded, size: 20, color: scheme.primary),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Floor Tag QR Code',
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w600,
                            fontFamily: 'PlayfairDisplay',
                          ),
                        ),
                        Text(
                          'Physical tag barcode for boutique displays, POS, and fitting rooms',
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 18),

              // Physical Atelier Floor Tag Preview Card
              _FloorTagPreviewCard(
                piece: widget.piece,
                payload: _currentPayload,
                scheme: scheme,
              ),
              const SizedBox(height: 18),

              // Format Selector Segment
              Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  'PAYLOAD ENCODING',
                  style: TextStyle(
                    fontSize: 10.5,
                    fontWeight: FontWeight.bold,
                    letterSpacing: 0.8,
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ),
              const SizedBox(height: 8),
              Container(
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerHighest.withValues(alpha: 0.4),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
                ),
                child: Row(
                  children: [
                    for (final f in QrFormatType.values)
                      Expanded(
                        child: InkWell(
                          key: Key('qr_format_${f.name}'),
                          onTap: () => setState(() => _format = f),
                          borderRadius: BorderRadius.circular(11),
                          child: Container(
                            padding: const EdgeInsets.symmetric(vertical: 10),
                            decoration: BoxDecoration(
                              color: _format == f ? scheme.surface : Colors.transparent,
                              borderRadius: BorderRadius.circular(11),
                              boxShadow: _format == f
                                  ? [
                                      BoxShadow(
                                        color: Colors.black.withValues(alpha: 0.04),
                                        blurRadius: 4,
                                        offset: const Offset(0, 1),
                                      ),
                                    ]
                                  : null,
                            ),
                            child: Text(
                              f.label,
                              textAlign: TextAlign.center,
                              style: TextStyle(
                                fontSize: 11.5,
                                fontWeight: _format == f ? FontWeight.bold : FontWeight.w500,
                                color: _format == f ? scheme.primary : scheme.onSurfaceVariant,
                              ),
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(height: 6),
              Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  _format.hint,
                  style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                ),
              ),
              const SizedBox(height: 14),

              // Encoded Payload Box
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text(
                          'ENCODED DATA',
                          style: TextStyle(
                            fontSize: 10,
                            fontWeight: FontWeight.bold,
                            letterSpacing: 0.5,
                            color: scheme.onSurfaceVariant,
                          ),
                        ),
                        InkWell(
                          key: const Key('qr_copy_payload_btn'),
                          onTap: _handleCopy,
                          child: Row(
                            children: [
                              Icon(
                                _copied ? Icons.check_circle_rounded : Icons.copy_rounded,
                                size: 14,
                                color: _copied ? const Color(0xFF10B981) : scheme.primary,
                              ),
                              const SizedBox(width: 4),
                              Text(
                                _copied ? 'Copied' : 'Copy',
                                style: TextStyle(
                                  fontSize: 11,
                                  fontWeight: FontWeight.bold,
                                  color: _copied ? const Color(0xFF10B981) : scheme.primary,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(
                      _currentPayload,
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontFamily: 'RobotoMono',
                        fontSize: 11,
                        height: 1.4,
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 18),

              // Action buttons
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      key: const Key('qr_copy_action_btn'),
                      onPressed: _handleCopy,
                      icon: Icon(_copied ? Icons.check : Icons.copy, size: 16),
                      label: Text(_copied ? 'Copied to Clipboard' : 'Copy Payload'),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: ElevatedButton.icon(
                      key: const Key('qr_close_sheet_btn'),
                      onPressed: () => Navigator.of(context).pop(),
                      icon: const Icon(Icons.check_rounded, size: 16),
                      label: const Text('Done'),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: scheme.primary,
                        foregroundColor: scheme.onPrimary,
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FloorTagPreviewCard extends StatelessWidget {
  const _FloorTagPreviewCard({
    required this.piece,
    required this.payload,
    required this.scheme,
  });

  final CatalogProduct piece;
  final String payload;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 260,
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: scheme.outlineVariant, width: 1.5),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.08),
            blurRadius: 16,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        children: [
          // Top Accent Gold Bar
          Container(
            height: 4,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  scheme.primary.withValues(alpha: 0.8),
                  scheme.primary,
                  scheme.primary.withValues(alpha: 0.8),
                ],
              ),
              borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 14),
            child: Column(
              children: [
                Text(
                  'AVELINE ATELIER',
                  style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.w900,
                    letterSpacing: 2.0,
                    color: scheme.onSurface,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  '${piece.category.toUpperCase()} · FLOOR COLLECTION',
                  style: TextStyle(
                    fontSize: 8.5,
                    fontWeight: FontWeight.w600,
                    letterSpacing: 0.8,
                    color: scheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 10),

                // Vector QR matrix simulated display container
                Container(
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    color: Colors.white,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: Colors.black12),
                  ),
                  child: CustomPaint(
                    size: const Size(130, 130),
                    painter: _QrMatrixPainter(payload: payload),
                  ),
                ),
                const SizedBox(height: 10),

                Text(
                  piece.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 12),
                ),
                const SizedBox(height: 2),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerHighest,
                    borderRadius: BorderRadius.circular(4),
                  ),
                  child: Text(
                    piece.skuLabel,
                    style: TextStyle(
                      fontFamily: 'RobotoMono',
                      fontSize: 10,
                      fontWeight: FontWeight.bold,
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                ),
                if (piece.color.isNotEmpty || (piece.fabric != null && piece.fabric!.isNotEmpty)) ...[
                  const SizedBox(height: 4),
                  Text(
                    [piece.color, piece.fabric].where((s) => s != null && s.isNotEmpty).join(' · '),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 9.5, color: scheme.onSurfaceVariant),
                  ),
                ],
                const SizedBox(height: 8),
                const Divider(height: 1, color: Colors.black12),
                const SizedBox(height: 6),

                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(
                      'RETAIL',
                      style: TextStyle(
                        fontSize: 9,
                        letterSpacing: 1.0,
                        fontWeight: FontWeight.bold,
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                    Text(
                      piece.priceLabel,
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
          ),
        ],
      ),
    );
  }
}

/// Vector CustomPainter rendering a structured high-contrast barcode/QR matrix visualization.
class _QrMatrixPainter extends CustomPainter {
  const _QrMatrixPainter({required this.payload});

  final String payload;

  @override
  void paint(Canvas canvas, Size size) {
    final paintBlack = Paint()
      ..color = Colors.black
      ..style = PaintingStyle.fill;

    const moduleCount = 21;
    final moduleSize = size.width / moduleCount;

    // Corner Finder Patterns
    void drawFinderPattern(double startX, double startY) {
      canvas.drawRect(Rect.fromLTWH(startX, startY, moduleSize * 7, moduleSize * 7), paintBlack);
      canvas.drawRect(
        Rect.fromLTWH(startX + moduleSize, startY + moduleSize, moduleSize * 5, moduleSize * 5),
        Paint()..color = Colors.white,
      );
      canvas.drawRect(
        Rect.fromLTWH(startX + moduleSize * 2, startY + moduleSize * 2, moduleSize * 3, moduleSize * 3),
        paintBlack,
      );
    }

    // Top-Left, Top-Right, Bottom-Left Finder Patterns
    drawFinderPattern(0, 0);
    drawFinderPattern(size.width - moduleSize * 7, 0);
    drawFinderPattern(0, size.height - moduleSize * 7);

    // Deterministic pseudo-random matrix derived from payload hash
    final hash = payload.hashCode.abs();
    for (int r = 0; r < moduleCount; r++) {
      for (int c = 0; c < moduleCount; c++) {
        // Skip finder pattern zones
        final inTopLeft = r < 8 && c < 8;
        final inTopRight = r < 8 && c >= moduleCount - 8;
        final inBottomLeft = r >= moduleCount - 8 && c < 8;
        if (inTopLeft || inTopRight || inBottomLeft) continue;

        final val = (hash ^ (r * 31 + c * 17) ^ (r * c)) % 3;
        if (val == 0 || (r == 6 || c == 6)) {
          canvas.drawRect(
            Rect.fromLTWH(c * moduleSize, r * moduleSize, moduleSize * 0.9, moduleSize * 0.9),
            paintBlack,
          );
        }
      }
    }
  }

  @override
  bool shouldRepaint(covariant _QrMatrixPainter oldDelegate) => oldDelegate.payload != payload;
}
