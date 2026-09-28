import 'package:flutter/material.dart';

import '../../data/catalog_product_repository.dart';
import '../../data/demo_catalog_product_repository.dart';
import '../../domain/sourcing_payloads.dart';
import '../../domain/sourcing_request.dart';
import '../../domain/sourcing_status.dart';
import '../../domain/supplier.dart';
import '../sourcing_controller.dart';

/// Luxury form screen for registering a bespoke client sourcing commission.
class CreateSourcingTicketScreen extends StatefulWidget {
  const CreateSourcingTicketScreen({
    super.key,
    this.repository,
    this.controller,
  });

  final CatalogProductRepository? repository;
  final SourcingController? controller;

  @override
  State<CreateSourcingTicketScreen> createState() => _CreateSourcingTicketScreenState();
}

class _CreateSourcingTicketScreenState extends State<CreateSourcingTicketScreen> {
  final _formKey = GlobalKey<FormState>();

  final _clientController = TextEditingController();
  final _colorController = TextEditingController();
  final _descriptionController = TextEditingController();
  final _targetPriceController = TextEditingController();
  final _estimatedCostController = TextEditingController();
  final _imageController = TextEditingController();
  final _notesController = TextEditingController();

  static const List<String> _categories = [
    'Silks & Satin',
    'Tailoring & Suits',
    'Knitwear & Cashmere',
    'Eveningwear & Gowns',
    'Leather Goods',
    'Fine Jewelry',
    'Accessories',
    'Footwear',
  ];

  String _category = 'Silks & Satin';
  SourcingStatus _stage = SourcingStatus.pending;
  String? _supplierId;
  String _supplierName = 'Maison de Soie';
  List<Supplier> _suppliers = const [];
  bool _isLoadingSuppliers = false;
  bool _isSaving = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _targetPriceController.addListener(_onPriceChanged);
    _estimatedCostController.addListener(_onPriceChanged);
    _loadSuppliers();
  }

  @override
  void dispose() {
    _targetPriceController.removeListener(_onPriceChanged);
    _estimatedCostController.removeListener(_onPriceChanged);
    _clientController.dispose();
    _colorController.dispose();
    _descriptionController.dispose();
    _targetPriceController.dispose();
    _estimatedCostController.dispose();
    _imageController.dispose();
    _notesController.dispose();
    super.dispose();
  }

  void _onPriceChanged() {
    setState(() {});
  }

  Future<void> _loadSuppliers() async {
    if (widget.controller != null && widget.controller!.suppliers.isNotEmpty) {
      setState(() {
        _suppliers = widget.controller!.suppliers;
        if (_suppliers.isNotEmpty) {
          _supplierId = _suppliers.first.id;
          _supplierName = _suppliers.first.name;
        }
      });
      return;
    }

    setState(() => _isLoadingSuppliers = true);
    try {
      final repo = widget.repository ?? widget.controller?.repository ?? DemoCatalogProductRepository();
      final loaded = await repo.getSuppliers();
      if (mounted) {
        setState(() {
          _suppliers = loaded;
          if (_suppliers.isNotEmpty) {
            _supplierId = _suppliers.first.id;
            _supplierName = _suppliers.first.name;
          }
          _isLoadingSuppliers = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() => _isLoadingSuppliers = false);
      }
    }
  }

  double get _parsedTargetPrice => double.tryParse(_targetPriceController.text.trim()) ?? 0.0;
  double get _parsedCost => double.tryParse(_estimatedCostController.text.trim()) ?? 0.0;
  double get _liveMarginAmount => _parsedTargetPrice - _parsedCost;
  double get _liveMarginPercentage => _parsedCost > 0 ? (_liveMarginAmount / _parsedCost) * 100 : 0.0;

  Future<void> _handleSubmit() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() {
      _isSaving = true;
      _errorMessage = null;
    });

    final payload = CreateSourcingRequestPayload(
      clientName: _clientController.text.trim(),
      category: _category,
      color: _colorController.text.trim(),
      description: _descriptionController.text.trim(),
      targetPrice: _parsedTargetPrice,
      estimatedCost: _parsedCost,
      supplierId: _supplierId ?? '',
      supplierName: _supplierName,
      status: _stage,
      referenceImageUrl: _imageController.text.trim(),
      notes: _notesController.text.trim(),
    );

    try {
      SourcingRequest? created;
      if (widget.controller != null) {
        created = await widget.controller!.createTicket(payload);
      } else {
        final repo = widget.repository ?? DemoCatalogProductRepository();
        created = await repo.createSourcingRequest(payload);
      }

      if (created != null && mounted) {
        Navigator.of(context).pop(created);
      } else if (mounted) {
        setState(() {
          _isSaving = false;
          _errorMessage = 'Unable to create ticket. Please check inputs.';
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSaving = false;
          _errorMessage = 'Error creating ticket: $e';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isDark = theme.brightness == Brightness.dark;
    final isPositiveMargin = _liveMarginAmount >= 0;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'Bespoke Sourcing Ticket',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.bold,
            fontFamily: 'PlayfairDisplay',
          ),
        ),
        elevation: 0,
        backgroundColor: Colors.transparent,
      ),
      body: SafeArea(
        child: Form(
          key: _formKey,
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 100),
            children: [
              if (_errorMessage != null) ...[
                Container(
                  padding: const EdgeInsets.all(12),
                  margin: const EdgeInsets.only(bottom: 16),
                  decoration: BoxDecoration(
                    color: scheme.errorContainer,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Text(
                    _errorMessage!,
                    style: TextStyle(
                      color: scheme.onErrorContainer,
                      fontSize: 13,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ),
              ],

              // Client Name
              Text(
                'Client Name',
                style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 6),
              TextFormField(
                key: const Key('sourcing_client_input'),
                controller: _clientController,
                decoration: InputDecoration(
                  hintText: 'e.g. Lady Genevieve Vance',
                  prefixIcon: const Icon(Icons.person_outline, size: 20),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                ),
                validator: (val) {
                  if (val == null || val.trim().isEmpty) {
                    return 'Please enter client name';
                  }
                  return null;
                },
              ),
              const SizedBox(height: 16),

              // Category & Color
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Category',
                          style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 6),
                        DropdownButtonFormField<String>(
                          key: const Key('sourcing_category_select'),
                          initialValue: _category,
                          isExpanded: true,
                          decoration: InputDecoration(
                            border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
                          ),
                          items: _categories.map((c) {
                            return DropdownMenuItem(value: c, child: Text(c, style: const TextStyle(fontSize: 13)));
                          }).toList(),
                          onChanged: (val) {
                            if (val != null) setState(() => _category = val);
                          },
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Color Specification',
                          style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 6),
                        TextFormField(
                          key: const Key('sourcing_color_input'),
                          controller: _colorController,
                          decoration: InputDecoration(
                            hintText: 'e.g. Ivory Champagne',
                            border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),

              // Description
              Text(
                'Item Description & Silhouettes',
                style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 6),
              TextFormField(
                key: const Key('sourcing_desc_input'),
                controller: _descriptionController,
                maxLines: 3,
                decoration: InputDecoration(
                  hintText: 'Describe garment silhouette, specific fabric requirements, or design cues...',
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                ),
                validator: (val) {
                  if (val == null || val.trim().isEmpty) {
                    return 'Please enter item description';
                  }
                  return null;
                },
              ),
              const SizedBox(height: 16),

              // Financials: Target Retail & Atelier Cost
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Target Retail (\$) *',
                          style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 6),
                        TextFormField(
                          key: const Key('sourcing_target_price_input'),
                          controller: _targetPriceController,
                          keyboardType: const TextInputType.numberWithOptions(decimal: true),
                          decoration: InputDecoration(
                            hintText: '0.00',
                            prefixText: '\$ ',
                            border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                          ),
                          validator: (val) {
                            if (val == null || val.trim().isEmpty) {
                              return 'Required';
                            }
                            final n = double.tryParse(val.trim());
                            if (n == null || n <= 0) return 'Invalid price';
                            return null;
                          },
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Atelier Cost (\$) *',
                          style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 6),
                        TextFormField(
                          key: const Key('sourcing_cost_input'),
                          controller: _estimatedCostController,
                          keyboardType: const TextInputType.numberWithOptions(decimal: true),
                          decoration: InputDecoration(
                            hintText: '0.00',
                            prefixText: '\$ ',
                            border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                          ),
                          validator: (val) {
                            if (val == null || val.trim().isEmpty) {
                              return 'Required';
                            }
                            final n = double.tryParse(val.trim());
                            if (n == null || n < 0) return 'Invalid cost';
                            return null;
                          },
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 14),

              // Live Margin Indicator Gauge
              Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: isPositiveMargin
                      ? (isDark ? const Color(0xFF064E3B).withValues(alpha: 0.4) : const Color(0xFFECFDF5))
                      : (isDark ? const Color(0xFF7F1D1D).withValues(alpha: 0.4) : const Color(0xFFFEF2F2)),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(
                    color: isPositiveMargin ? const Color(0xFF10B981) : const Color(0xFFEF4444),
                    width: 1,
                  ),
                ),
                child: Row(
                  children: [
                    Icon(
                      isPositiveMargin ? Icons.trending_up : Icons.trending_down,
                      color: isPositiveMargin ? const Color(0xFF10B981) : const Color(0xFFEF4444),
                      size: 20,
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'ESTIMATED MARGIN GAUGE',
                            style: TextStyle(
                              fontSize: 10,
                              fontWeight: FontWeight.bold,
                              letterSpacing: 0.6,
                              color: isPositiveMargin ? const Color(0xFF059669) : const Color(0xFFDC2626),
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            '${isPositiveMargin ? '+' : ''}${_liveMarginPercentage.toStringAsFixed(1)}% profit (\$${_liveMarginAmount.toStringAsFixed(2)})',
                            style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.bold,
                              color: isPositiveMargin ? const Color(0xFF047857) : const Color(0xFFB91C1C),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),

              // Sourced Atelier / Supplier & Initial Stage
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Partner Atelier',
                          style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 6),
                        _isLoadingSuppliers
                            ? const SizedBox(height: 48, child: Center(child: CircularProgressIndicator()))
                            : DropdownButtonFormField<String>(
                                key: const Key('sourcing_supplier_select'),
                                initialValue: _supplierId ?? (_suppliers.isNotEmpty ? _suppliers.first.id : null),
                                isExpanded: true,
                                decoration: InputDecoration(
                                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                                  contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
                                ),
                                items: _suppliers.map((s) {
                                  return DropdownMenuItem(
                                    value: s.id,
                                    child: Text(
                                      s.name,
                                      style: const TextStyle(fontSize: 12.5),
                                      overflow: TextOverflow.ellipsis,
                                    ),
                                  );
                                }).toList(),
                                onChanged: (val) {
                                  if (val != null) {
                                    final found = _suppliers.firstWhere((s) => s.id == val);
                                    setState(() {
                                      _supplierId = val;
                                      _supplierName = found.name;
                                    });
                                  }
                                },
                              ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Initial Stage',
                          style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 6),
                        DropdownButtonFormField<SourcingStatus>(
                          key: const Key('sourcing_stage_select'),
                          initialValue: _stage,
                          isExpanded: true,
                          decoration: InputDecoration(
                            border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                            contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
                          ),
                          items: const [
                            DropdownMenuItem(
                              value: SourcingStatus.pending,
                              child: Text('Pending Quote', style: TextStyle(fontSize: 12.5)),
                            ),
                            DropdownMenuItem(
                              value: SourcingStatus.quoted,
                              child: Text('Quoted', style: TextStyle(fontSize: 12.5)),
                            ),
                            DropdownMenuItem(
                              value: SourcingStatus.approved,
                              child: Text('Approved', style: TextStyle(fontSize: 12.5)),
                            ),
                          ],
                          onChanged: (val) {
                            if (val != null) setState(() => _stage = val);
                          },
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),

              // Reference Image URL
              Text(
                'Reference Moodboard Image URL (Optional)',
                style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 6),
              TextFormField(
                key: const Key('sourcing_image_input'),
                controller: _imageController,
                decoration: InputDecoration(
                  hintText: 'https://images.unsplash.com/...',
                  prefixIcon: const Icon(Icons.link, size: 20),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                ),
              ),
              const SizedBox(height: 16),

              // Stylist Notes
              Text(
                'Stylist & Tailor Notes (Optional)',
                style: theme.textTheme.labelMedium?.copyWith(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 6),
              TextFormField(
                key: const Key('sourcing_notes_input'),
                controller: _notesController,
                maxLines: 2,
                decoration: InputDecoration(
                  hintText: 'Special lead times, tailoring measurements, or fittings schedule...',
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                ),
              ),
            ],
          ),
        ),
      ),
      bottomSheet: Container(
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 20),
        decoration: BoxDecoration(
          color: scheme.surface,
          border: Border(top: BorderSide(color: scheme.outlineVariant.withValues(alpha: 0.6))),
        ),
        child: SizedBox(
          width: double.infinity,
          height: 48,
          child: FilledButton.icon(
            key: const Key('sourcing_submit_ticket_btn'),
            onPressed: _isSaving ? null : _handleSubmit,
            icon: _isSaving
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                  )
                : const Icon(Icons.checkroom, size: 20),
            label: Text(
              _isSaving ? 'Registering Commission...' : 'Create Sourcing Ticket',
              style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15),
            ),
          ),
        ),
      ),
    );
  }
}
