import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../catalog/data/catalog_product_repository.dart';
import '../../domain/repositories/commerce_repository.dart';
import '../controllers/order_creation_controller.dart';
import '../widgets/catalog_item_picker_sheet.dart';

class CreateOrderScreen extends StatefulWidget {
  const CreateOrderScreen({
    super.key,
    required this.commerceRepository,
    required this.catalogRepository,
  });

  final CommerceRepository commerceRepository;
  final CatalogProductRepository catalogRepository;

  @override
  State<CreateOrderScreen> createState() => _CreateOrderScreenState();
}

class _CreateOrderScreenState extends State<CreateOrderScreen> {
  late final OrderCreationController _controller;
  final TextEditingController _customerController = TextEditingController();
  final TextEditingController _discountController = TextEditingController();
  final TextEditingController _notesController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _controller = OrderCreationController(repository: widget.commerceRepository);
    _controller.addListener(_onControllerChange);
  }

  void _onControllerChange() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _controller.removeListener(_onControllerChange);
    _controller.dispose();
    _customerController.dispose();
    _discountController.dispose();
    _notesController.dispose();
    super.dispose();
  }

  Future<void> _handleSubmit() async {
    try {
      final order = await _controller.submitOrder();
      if (!mounted) return;

      if (order.isPendingApproval) {
        AppToast.show(context, 'Order submitted — Sent to owner for approval');
      } else {
        AppToast.show(context, 'Order confirmed successfully');
      }

      context.pushReplacement(AppRoutes.orderDetail(order.id));
    } catch (e) {
      if (mounted) {
        AppToast.show(context, 'Failed to create order: $e', error: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      backgroundColor: scheme.surface,
      appBar: AppBar(
        title: Text(
          'New Commerce Order',
          style: theme.textTheme.titleLarge?.copyWith(
            fontFamily: 'Playfair Display',
            fontWeight: FontWeight.w600,
          ),
        ),
        backgroundColor: scheme.surface,
        elevation: 0,
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // Channel selector
              Text('Order Channel', style: theme.textTheme.labelMedium),
              const SizedBox(height: 8),
              SegmentedButton<String>(
                segments: const [
                  ButtonSegment(
                    value: 'in_store',
                    label: Text('In-Store Counter'),
                    icon: Icon(Icons.storefront_outlined),
                  ),
                  ButtonSegment(
                    value: 'whatsapp',
                    label: Text('WhatsApp Concierge'),
                    icon: Icon(Icons.chat_outlined),
                  ),
                ],
                selected: {_controller.orderType},
                onSelectionChanged: (val) {
                  _controller.setOrderType(val.first);
                },
                style: SegmentedButton.styleFrom(
                  selectedBackgroundColor: scheme.primaryContainer,
                  selectedForegroundColor: scheme.onPrimaryContainer,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(16),
                  ),
                ),
              ),
              const SizedBox(height: 20),

              // Customer info
              Text('Customer', style: theme.textTheme.labelMedium),
              const SizedBox(height: 8),
              TextField(
                controller: _customerController,
                decoration: InputDecoration(
                  labelText: 'Customer Full Name',
                  hintText: 'e.g. Ruwani Jayawardena',
                  prefixIcon: const Icon(Icons.person_outline_rounded),
                  filled: true,
                  fillColor: scheme.surfaceContainerLow,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(16),
                    borderSide: BorderSide.none,
                  ),
                ),
                onChanged: _controller.setCustomerName,
              ),
              const SizedBox(height: 20),

              // Line Items header
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    'Order Items (${_controller.items.length})',
                    style: theme.textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  TextButton.icon(
                    onPressed: () {
                      CatalogItemPickerSheet.show(
                        context,
                        catalogRepository: widget.catalogRepository,
                        onItemSelected: _controller.addItem,
                      );
                    },
                    icon: const Icon(Icons.add_rounded, size: 18),
                    label: const Text('Add Piece'),
                  ),
                ],
              ),
              const SizedBox(height: 8),

              // Line items list
              if (_controller.items.isEmpty)
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.symmetric(vertical: 28),
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerLow,
                    borderRadius: BorderRadius.circular(18),
                    border: Border.all(
                      color: scheme.outlineVariant.withValues(alpha: 0.6),
                      style: BorderStyle.solid,
                    ),
                  ),
                  child: Column(
                    children: [
                      Icon(Icons.checkroom_outlined, size: 36, color: scheme.outline),
                      const SizedBox(height: 8),
                      Text(
                        'No catalog items added yet.',
                        style: theme.textTheme.bodyMedium?.copyWith(color: scheme.outline),
                      ),
                      const SizedBox(height: 12),
                      FilledButton.tonalIcon(
                        onPressed: () {
                          CatalogItemPickerSheet.show(
                            context,
                            catalogRepository: widget.catalogRepository,
                            onItemSelected: _controller.addItem,
                          );
                        },
                        icon: const Icon(Icons.search_rounded, size: 18),
                        label: const Text('Browse Catalog'),
                      ),
                    ],
                  ),
                )
              else
                ListView.separated(
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  itemCount: _controller.items.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final item = _controller.items[index];
                    return Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerLow,
                        borderRadius: BorderRadius.circular(16),
                      ),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  item.itemName,
                                  style: theme.textTheme.titleSmall?.copyWith(
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                                const SizedBox(height: 4),
                                Text(
                                  'LKR ${item.unitPrice.toStringAsFixed(0)} each',
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color: scheme.onSurfaceVariant,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          Row(
                            children: [
                              IconButton(
                                icon: const Icon(Icons.remove_circle_outline_rounded, size: 20),
                                onPressed: () {
                                  _controller.updateQuantity(item.itemId, item.quantity - 1);
                                },
                              ),
                              Text(
                                '${item.quantity}',
                                style: theme.textTheme.titleMedium?.copyWith(
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                              IconButton(
                                icon: const Icon(Icons.add_circle_outline_rounded, size: 20),
                                onPressed: () {
                                  _controller.updateQuantity(item.itemId, item.quantity + 1);
                                },
                              ),
                            ],
                          ),
                          IconButton(
                            icon: const Icon(Icons.delete_outline_rounded, size: 20),
                            color: scheme.error,
                            onPressed: () => _controller.removeItem(item.itemId),
                          ),
                        ],
                      ),
                    );
                  },
                ),
              const SizedBox(height: 20),

              // Discount & Notes
              TextField(
                controller: _discountController,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(
                  labelText: 'Custom Discount (LKR)',
                  hintText: '0',
                  prefixText: 'LKR ',
                  prefixIcon: const Icon(Icons.percent_rounded),
                  filled: true,
                  fillColor: scheme.surfaceContainerLow,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(16),
                    borderSide: BorderSide.none,
                  ),
                ),
                onChanged: (val) {
                  final parsed = double.tryParse(val) ?? 0.0;
                  _controller.setDiscount(parsed);
                },
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _notesController,
                maxLines: 2,
                decoration: InputDecoration(
                  labelText: 'Order Notes (Optional)',
                  hintText: 'e.g. Client requested delivery by Friday',
                  filled: true,
                  fillColor: scheme.surfaceContainerLow,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(16),
                    borderSide: BorderSide.none,
                  ),
                ),
                onChanged: _controller.setNotes,
              ),
              const SizedBox(height: 20),

              // Realtime Threshold alert
              if (_controller.triggersApprovalWarning)
                Container(
                  margin: const EdgeInsets.only(bottom: 16),
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFFFF3CD),
                    borderRadius: BorderRadius.circular(14),
                    border: Border.all(color: const Color(0xFF856404).withValues(alpha: 0.3)),
                  ),
                  child: Row(
                    children: [
                      const Icon(Icons.warning_amber_rounded, color: Color(0xFF856404), size: 20),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          'Requires Owner Approval: Discount exceeds 15% or profit margin falls below threshold.',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: const Color(0xFF856404),
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),

              // Summary Card
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: scheme.outlineVariant),
                  boxShadow: [
                    BoxShadow(
                      color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
                      blurRadius: 18,
                      offset: const Offset(0, 6),
                    ),
                  ],
                ),
                child: Column(
                  children: [
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text('Subtotal', style: theme.textTheme.bodyMedium),
                        Text(
                          'LKR ${_controller.subtotal.toStringAsFixed(0)}',
                          style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
                        ),
                      ],
                    ),
                    if (_controller.discount > 0) ...[
                      const SizedBox(height: 6),
                      Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          Text('Discount', style: TextStyle(color: scheme.error)),
                          Text(
                            '- LKR ${_controller.discount.toStringAsFixed(0)}',
                            style: TextStyle(color: scheme.error, fontWeight: FontWeight.w600),
                          ),
                        ],
                      ),
                    ],
                    const Divider(height: 20),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text(
                          'Total',
                          style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.bold),
                        ),
                        Text(
                          'LKR ${_controller.total.toStringAsFixed(0)}',
                          style: theme.textTheme.titleLarge?.copyWith(
                            fontWeight: FontWeight.bold,
                            color: scheme.primary,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text('Est. Margin', style: theme.textTheme.bodySmall?.copyWith(color: scheme.outline)),
                        Text(
                          '${_controller.marginPercent.toStringAsFixed(1)}% (LKR ${_controller.margin.toStringAsFixed(0)})',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: _controller.marginPercent < 20 ? scheme.error : const Color(0xFF28A745),
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 24),

              // Create button
              SizedBox(
                width: double.infinity,
                child: FilledButton(
                  onPressed: _controller.canSubmit ? _handleSubmit : null,
                  style: FilledButton.styleFrom(
                    padding: const EdgeInsets.symmetric(vertical: 16),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(16),
                    ),
                  ),
                  child: _controller.isSubmitting
                      ? const SizedBox(
                          width: 22,
                          height: 22,
                          child: CircularProgressIndicator(
                            strokeWidth: 2.5,
                            color: Colors.white,
                          ),
                        )
                      : const Text(
                          'Create Order',
                          style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
                        ),
                ),
              ),
              const SizedBox(height: 20),
            ],
          ),
        ),
      ),
    );
  }
}
