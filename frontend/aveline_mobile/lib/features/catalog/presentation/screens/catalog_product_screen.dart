import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../data/catalog_product_repository.dart';
import '../../data/demo_catalog_product_repository.dart';
import '../../domain/catalog_product.dart';
import '../catalog_colors.dart';
import '../widgets/catalog_product_image.dart';

/// A single piece, opened by tapping its card in the grid.
///
/// The id is the only thing the route carries. An earlier version pushed the
/// whole piece as route `extra` for an instant first frame, but the router
/// re-parses its location whenever the auth or profile listenable fires and the
/// `extra` does not survive that, which left the screen with nothing to draw.
/// Resolving from the id is a pure function of the location, so a refresh is
/// invisible.
class CatalogProductScreen extends StatefulWidget {
  const CatalogProductScreen({
    super.key,
    this.product,
    required this.productId,
    this.repository,
  });

  /// Seeds the screen without a fetch, for previews and tests. Production
  /// routing does not use it.
  final CatalogProduct? product;

  /// The id the route was addressed by.
  final String productId;

  /// Overrides the source the piece is resolved from, for tests and previews.
  final CatalogProductRepository? repository;

  @override
  State<CatalogProductScreen> createState() => _CatalogProductScreenState();
}

class _CatalogProductScreenState extends State<CatalogProductScreen> {
  late final CatalogProductRepository _repository =
      widget.repository ?? DemoCatalogProductRepository();

  CatalogProduct? _product;
  bool _isLoading = false;

  /// Set once a supply request has been logged, so the action reads as done.
  bool _supplyRequested = false;

  /// Set once Aveline has been asked to look at the piece.
  bool _mentionedToAveline = false;

  @override
  void initState() {
    super.initState();
    _product = widget.product;
    if (_product == null) {
      _resolve(widget.productId);
    }
  }

  @override
  void didUpdateWidget(covariant CatalogProductScreen oldWidget) {
    super.didUpdateWidget(oldWidget);

    if (widget.product case final seeded?) {
      _product = seeded;
      return;
    }

    // A rebuild with a new id resolves the new piece. A rebuild that somehow
    // loses the id keeps whatever is already on screen rather than blanking it.
    if (widget.productId != oldWidget.productId) {
      _resolve(widget.productId);
    }
  }

  Future<void> _resolve(String id) async {
    if (id.isEmpty) {
      return;
    }

    setState(() => _isLoading = true);

    CatalogProduct? found;
    try {
      found = await _repository.fetchProduct(id);
    } catch (error) {
      debugPrint('[catalog] could not resolve $id: $error');
      found = null;
    }

    // A later id, or a screen that is gone, makes this reply irrelevant.
    if (!mounted || widget.productId != id) {
      return;
    }

    setState(() {
      _isLoading = false;
      _supplyRequested = false;
      _mentionedToAveline = false;
      if (found != null) {
        _product = found;
      }
    });
  }

  /// Applies a status change locally and says so.
  ///
  /// The inventory API has no mutation wired on mobile yet; this keeps the
  /// screen honest about what it did and leaves one place to swap in the call.
  void _applyStatus(CatalogItemStatus status, String message) {
    final piece = _product;
    if (piece == null) {
      return;
    }

    setState(() {
      _product = piece.copyWith(
        status: status,
        isAvailable: status == CatalogItemStatus.available,
      );
    });
    AppToast.show(context, message);
  }

  void _requestSupply() {
    final piece = _product;
    if (piece == null) {
      return;
    }

    setState(() => _supplyRequested = true);
    AppToast.show(context, 'Supply request logged for ${piece.name}.');
  }

  void _mentionToAveline() {
    final piece = _product;
    if (piece == null) {
      return;
    }

    setState(() => _mentionedToAveline = true);
    AppToast.show(context, 'Aveline will take a look at ${piece.name}.');
  }

  @override
  Widget build(BuildContext context) {
    final piece = _product;

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          key: const Key('catalog_product_back'),
          icon: const Icon(Icons.arrow_back_rounded),
          tooltip: 'Back',
          onPressed: () => Navigator.of(context).maybePop(),
        ),
      ),
      body: Stack(
        children: [
          const Positioned.fill(child: BrandBackdrop()),
          Positioned.fill(child: _body(piece)),
        ],
      ),
    );
  }

  Widget _body(CatalogProduct? piece) {
    if (piece != null) {
      return _Detail(
        piece: piece,
        onStatus: _applyStatus,
        onSupply: _requestSupply,
        supplyRequested: _supplyRequested,
        onMention: _mentionToAveline,
        mentionedToAveline: _mentionedToAveline,
      );
    }

    if (_isLoading) {
      return const Center(
        child: SizedBox(
          width: 24,
          height: 24,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 24, 20, 48),
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        _SectionCard(
          title: 'Catalog',
          children: [
            Text(
              'Piece not found',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 8),
            Text(
              widget.productId.isEmpty
                  ? 'We could not open this piece. It may have been archived.'
                  : 'We could not open ${widget.productId}. It may have been '
                        'archived, or the catalog could not be reached.',
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('catalog_product_retry'),
                onPressed: () => _resolve(widget.productId),
                icon: const Icon(Icons.refresh_rounded, size: 16),
                label: const Text('Try again'),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

/// The piece itself, laid out as a stack of cards.
class _Detail extends StatelessWidget {
  const _Detail({
    required this.piece,
    required this.onStatus,
    required this.onSupply,
    required this.supplyRequested,
    required this.onMention,
    required this.mentionedToAveline,
  });

  final CatalogProduct piece;
  final void Function(CatalogItemStatus status, String message) onStatus;
  final VoidCallback onSupply;
  final bool supplyRequested;
  final VoidCallback onMention;
  final bool mentionedToAveline;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 8, 20, 48),
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        // Hero: the piece at a glance.
        Stack(
          children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(20),
              child: AspectRatio(
                aspectRatio: 4 / 5,
                child: CatalogProductImage(product: piece, iconSize: 64),
              ),
            ),
            Positioned(
              top: 12,
              left: 12,
              child: _StatusPill(status: piece.status),
            ),
          ],
        ),
        const SizedBox(height: 20),
        Text(piece.name, style: theme.textTheme.headlineMedium),
        const SizedBox(height: 6),
        Text(
          '${piece.category} · ${piece.styleLabel}',
          style: theme.textTheme.labelMedium?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 20),

        _PriceCard(piece: piece),
        const SizedBox(height: 16),

        _SectionCard(
          title: 'Actions',
          children: [
            // Stacked, one to a row: the actions used to sit two-up in a grid,
            // which squeezed their labels and made different decisions look
            // like one block.
            _ActionButton(
              key: const Key('catalog_action_hold'),
              icon: Icons.bookmark_add_outlined,
              label: 'Create a hold',
              tone: _ActionTone.primary,
              onPressed: piece.status.isHoldable
                  ? () => onStatus(
                      CatalogItemStatus.onHold,
                      'Hold created for ${piece.name}.',
                    )
                  : null,
            ),
            const SizedBox(height: 10),
            _ActionButton(
              key: const Key('catalog_action_unavailable'),
              icon: Icons.visibility_off_outlined,
              label: 'Mark unavailable',
              tone: _ActionTone.neutral,
              onPressed: piece.status.isSellable
                  ? () => onStatus(
                      CatalogItemStatus.unavailable,
                      '${piece.name} marked unavailable.',
                    )
                  : null,
            ),
            const SizedBox(height: 10),
            _ActionButton(
              key: const Key('catalog_action_sold_out'),
              icon: Icons.sell_outlined,
              label: 'Mark sold out',
              tone: _ActionTone.danger,
              onPressed: piece.status.isSellable
                  ? () => onStatus(
                      CatalogItemStatus.soldOut,
                      '${piece.name} marked sold out.',
                    )
                  : null,
            ),
            const SizedBox(height: 10),
            _ActionButton(
              key: const Key('catalog_action_supply'),
              icon: Icons.local_shipping_outlined,
              label: supplyRequested ? 'Supply requested' : 'Request a supply',
              tone: _ActionTone.tonal,
              onPressed: supplyRequested ? null : onSupply,
            ),
            const SizedBox(height: 10),
            // The one action that leaves the catalog: it asks the agent to take
            // the piece on, so it wears the Aveline accent rather than one of
            // the floor's own colours.
            _ActionButton(
              key: const Key('catalog_action_mention'),
              icon: Icons.auto_awesome_outlined,
              label: mentionedToAveline
                  ? 'Mentioned to Aveline'
                  : 'Mention to Aveline',
              tone: _ActionTone.accent,
              onPressed: mentionedToAveline ? null : onMention,
            ),
          ],
        ),
        const SizedBox(height: 16),

        _SectionCard(
          title: 'Item',
          children: [
            _SpecTable(
              rows: [
                _Spec('SKU', piece.skuLabel),
                _Spec('Category', piece.category),
                _Spec(
                  'Colour',
                  piece.color,
                  leading: CatalogColorDot(colorName: piece.color),
                ),
                _Spec('Fabric', piece.fabricLabel),
                _Spec('Style', piece.styleLabel),
                _Spec('Sizes', piece.sizesLabel),
                _Spec('Quantity', '${piece.quantity}'),
                _Spec('Status', piece.status.label),
                _Spec('Available', piece.availabilityLabel),
                _Spec('Sourced from', piece.sourceLabel),
                _Spec('Added', piece.createdLabel),
                _Spec('Boutique', piece.organizationId),
                if (piece.deletedAt != null)
                  _Spec('Removed', piece.deletedAt.toString()),
              ],
            ),
          ],
        ),

        if (piece.metadataEntries.isNotEmpty) ...[
          const SizedBox(height: 16),
          _SectionCard(
            title: 'Boutique notes',
            children: [
              _SpecTable(
                rows: [
                  for (final entry in piece.metadataEntries)
                    _Spec(entry.key, entry.value),
                ],
              ),
            ],
          ),
        ],

        if (piece.description case final description?
            when description.trim().isNotEmpty) ...[
          const SizedBox(height: 16),
          _SectionCard(
            title: 'Description',
            children: [
              Text(
                description,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ],
      ],
    );
  }
}

/// What the piece is worth: the tag price at display size, then the numbers the
/// boutique actually decides on.
class _PriceCard extends StatelessWidget {
  const _PriceCard({required this.piece});

  final CatalogProduct piece;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return _SectionCard(
      title: 'Pricing',
      children: [
        Text(
          'Retail price',
          style: theme.textTheme.labelSmall?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 2),
        Text(
          piece.priceLabel,
          key: const Key('catalog_detail_price'),
          style: theme.textTheme.displayLarge?.copyWith(color: scheme.primary),
        ),
        const SizedBox(height: 4),
        Text(
          piece.stockLabel,
          style: theme.textTheme.labelMedium?.copyWith(
            color: piece.isLowStock ? scheme.primary : scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 16),
        _SpecTable(
          rows: [
            _Spec('Cost', piece.costLabel),
            _Spec('Margin', piece.marginLabel),
            _Spec('Discount', piece.discountRangeLabel),
            _Spec('Floor price', piece.floorPriceLabel),
          ],
        ),
      ],
    );
  }
}

/// A titled card: the catalog's one container shape.
class _SectionCard extends StatelessWidget {
  const _SectionCard({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
            blurRadius: 20,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionOverline(title),
          const SizedBox(height: 14),
          ...children,
        ],
      ),
    );
  }
}

/// One label/value line in a card.
class _Spec {
  const _Spec(this.label, this.value, {this.leading});

  final String label;
  final String value;

  /// An optional swatch or icon shown before the value.
  final Widget? leading;
}

/// The card's inset table: labels down a fixed gutter, values beside them.
class _SpecTable extends StatelessWidget {
  const _SpecTable({required this.rows});

  final List<_Spec> rows;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      children: [
        for (var i = 0; i < rows.length; i++) ...[
          if (i > 0)
            Divider(
              height: 1,
              thickness: 0.5,
              color: scheme.outlineVariant.withValues(alpha: 0.6),
            ),
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 10),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  width: 112,
                  child: Text(
                    rows[i].label,
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      if (rows[i].leading case final leading?) ...[
                        Padding(
                          padding: const EdgeInsets.only(top: 2, right: 8),
                          child: leading,
                        ),
                      ],
                      Expanded(
                        child: Text(
                          rows[i].value,
                          style: theme.textTheme.titleSmall,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ],
    );
  }
}

/// How loud an action is, and what it means.
///
/// The actions on a piece are different decisions, so they carry different
/// treatments rather than one button style repeated: the wine of a commitment,
/// a lighter wine for a request, the agent's own accent for asking Aveline,
/// quiet neutral for taking a piece off the floor, and the error palette for
/// writing it off.
enum _ActionTone { primary, tonal, accent, neutral, danger }

/// One action on the piece, full width and stacked with its siblings.
class _ActionButton extends StatelessWidget {
  const _ActionButton({
    super.key,
    required this.icon,
    required this.label,
    required this.tone,
    required this.onPressed,
  });

  final IconData icon;
  final String label;
  final _ActionTone tone;

  /// `null` renders the action inert, which is how a state that no longer
  /// applies reads.
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isEnabled = onPressed != null;

    // The agent's action wears the brand's own atmosphere instead of a flat
    // fill: the same drifting blobs and blossoms as the Blossom card, clipped
    // to the pill.
    final carriesWash = tone == _ActionTone.accent && isEnabled;

    final (background, foreground, border) = isEnabled
        ? switch (tone) {
            _ActionTone.primary => (
              scheme.primary,
              scheme.onPrimary,
              Colors.transparent,
            ),
            _ActionTone.tonal => (
              scheme.primary.withValues(alpha: 0.10),
              scheme.primary,
              scheme.primary.withValues(alpha: 0.28),
            ),
            _ActionTone.accent => (
              scheme.surfaceContainerLowest,
              scheme.primary,
              scheme.primary.withValues(alpha: 0.28),
            ),
            _ActionTone.neutral => (
              scheme.surfaceContainerLow,
              scheme.onSurfaceVariant,
              scheme.outlineVariant,
            ),
            _ActionTone.danger => (
              scheme.errorContainer,
              scheme.onErrorContainer,
              Colors.transparent,
            ),
          }
        : (
            scheme.surfaceContainerLow,
            scheme.onSurfaceVariant.withValues(alpha: 0.38),
            scheme.outlineVariant.withValues(alpha: 0.5),
          );

    final shape = StadiumBorder(side: BorderSide(color: border));

    return Semantics(
      button: true,
      enabled: isEnabled,
      label: label,
      child: SizedBox(
        height: 52,
        child: Material(
          // Transparent while the wash shows, so the blobs paint the fill.
          color: carriesWash ? Colors.transparent : background,
          shape: shape,
          clipBehavior: Clip.antiAlias,
          child: Stack(
            fit: StackFit.expand,
            children: [
              if (carriesWash) ...[
                const BlossomWash(),
                // A veil of surface over the wash, so the label and icon keep
                // their contrast wherever a blob happens to be drifting.
                ColoredBox(
                  color: scheme.surfaceContainerLowest.withValues(alpha: 0.38),
                ),
              ],
              InkWell(
                onTap: onPressed,
                customBorder: shape,
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 18),
                  child: Row(
                    children: [
                      Icon(icon, size: 20, color: foreground),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Text(
                          label,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.labelLarge?.copyWith(
                            color: foreground,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The piece's workflow state, worn on the photograph.
class _StatusPill extends StatelessWidget {
  const _StatusPill({required this.status});

  final CatalogItemStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final (background, foreground) = switch (status) {
      CatalogItemStatus.available => (
        scheme.surfaceContainerLowest,
        scheme.primary,
      ),
      CatalogItemStatus.onHold => (scheme.primary, scheme.onPrimary),
      CatalogItemStatus.soldOut => (scheme.error, scheme.onError),
      CatalogItemStatus.unavailable || CatalogItemStatus.archived => (
        scheme.surfaceContainerHighest,
        scheme.onSurfaceVariant,
      ),
    };

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        status.label,
        style: theme.textTheme.labelSmall?.copyWith(color: foreground),
      ),
    );
  }
}
