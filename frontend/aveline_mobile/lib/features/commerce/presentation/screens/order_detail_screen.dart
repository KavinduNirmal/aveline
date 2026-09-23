import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../core/notifications/notification_provider.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../domain/repositories/commerce_repository.dart';
import '../controllers/approvals_realtime_controller.dart';
import '../widgets/approval_status_banner.dart';
import '../widgets/payment_qr_modal.dart';

class OrderDetailScreen extends StatefulWidget {
  const OrderDetailScreen({
    super.key,
    required this.orderId,
    required this.repository,
  });

  final String orderId;
  final CommerceRepository repository;

  @override
  State<OrderDetailScreen> createState() => _OrderDetailScreenState();
}

class _OrderDetailScreenState extends State<OrderDetailScreen> {
  late final ApprovalsRealtimeController _controller;
  bool _isGeneratingPayment = false;

  @override
  void initState() {
    super.initState();
    NotificationProvider? notificationProvider;
    try {
      notificationProvider = context.read<NotificationProvider>();
    } catch (_) {
      notificationProvider = null;
    }

    _controller = ApprovalsRealtimeController(
      repository: widget.repository,
      notificationProvider: notificationProvider,
    );
    _controller.addListener(_onControllerChange);
    _controller.load(widget.orderId);
  }

  void _onControllerChange() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _controller.removeListener(_onControllerChange);
    _controller.dispose();
    super.dispose();
  }

  Future<void> _handleGeneratePayment() async {
    final order = _controller.order;
    if (order == null) return;

    setState(() => _isGeneratingPayment = true);
    try {
      final payment = _controller.payment ?? await _controller.generatePayment();
      if (!mounted) return;
      setState(() => _isGeneratingPayment = false);

      await PaymentQrModal.show(
        context,
        order: order,
        payment: payment,
        controller: _controller,
      );
    } catch (e) {
      if (mounted) {
        setState(() => _isGeneratingPayment = false);
        AppToast.show(context, 'Failed to generate payment: $e', error: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final order = _controller.order;

    if (_controller.isLoading) {
      return Scaffold(
        backgroundColor: scheme.surface,
        appBar: AppBar(
          title: const Text('Order Details'),
          backgroundColor: scheme.surface,
        ),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (order == null) {
      return Scaffold(
        backgroundColor: scheme.surface,
        appBar: AppBar(
          title: const Text('Order Details'),
          backgroundColor: scheme.surface,
        ),
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                _controller.errorMessage ?? 'Order not found',
                style: TextStyle(color: scheme.error),
              ),
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: () => _controller.load(widget.orderId),
                child: const Text('Try Again'),
              ),
            ],
          ),
        ),
      );
    }

    final isPaid = _controller.payment?.isConfirmed ?? false;
    final canPay = !order.isPendingApproval && !order.isCancelled;

    return Scaffold(
      backgroundColor: scheme.surface,
      appBar: AppBar(
        title: Text(
          'Order #${order.id.length > 8 ? order.id.substring(0, 8) : order.id}',
          style: theme.textTheme.titleLarge?.copyWith(
            fontFamily: 'Playfair Display',
            fontWeight: FontWeight.w600,
          ),
        ),
        backgroundColor: scheme.surface,
        elevation: 0,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            tooltip: 'Refresh Order',
            onPressed: () => _controller.refresh(),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => _controller.refresh(),
        child: SingleChildScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // Realtime Approval Banner
              ApprovalStatusBanner(
                order: order,
                approval: _controller.approval,
                isRealtimeConnected: _controller.isRealtimeActive,
              ),

              // Header Card
              Container(
                margin: const EdgeInsets.symmetric(vertical: 8),
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: scheme.outlineVariant),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text(
                          order.customerName,
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                          decoration: BoxDecoration(
                            color: scheme.primaryContainer,
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Text(
                            order.orderType == 'whatsapp' ? 'WhatsApp' : 'In-Store',
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: scheme.onPrimaryContainer,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(
                      'Created: ${order.createdAt.toLocal().toString().split('.').first}',
                      style: theme.textTheme.bodySmall?.copyWith(color: scheme.outline),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 12),

              // Line Items
              Text(
                'Line Items (${order.items.length})',
                style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 8),
              Container(
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: scheme.outlineVariant),
                ),
                child: ListView.separated(
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  itemCount: order.items.length,
                  separatorBuilder: (_, __) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final item = order.items[index];
                    return Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  item.itemName,
                                  style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w500),
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  'Qty: ${item.quantity} × LKR ${item.unitPrice.toStringAsFixed(0)}',
                                  style: theme.textTheme.bodySmall?.copyWith(color: scheme.onSurfaceVariant),
                                ),
                              ],
                            ),
                          ),
                          Text(
                            'LKR ${item.totalPrice.toStringAsFixed(0)}',
                            style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600),
                          ),
                        ],
                      ),
                    );
                  },
                ),
              ),
              const SizedBox(height: 16),

              // Financial Breakdown Card
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: scheme.outlineVariant),
                ),
                child: Column(
                  children: [
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text('Subtotal', style: theme.textTheme.bodyMedium),
                        Text('LKR ${order.subtotal.toStringAsFixed(0)}', style: theme.textTheme.bodyMedium),
                      ],
                    ),
                    if (order.discount > 0) ...[
                      const SizedBox(height: 6),
                      Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          Text('Discount', style: TextStyle(color: scheme.error)),
                          Text(
                            '- LKR ${order.discount.toStringAsFixed(0)}',
                            style: TextStyle(color: scheme.error, fontWeight: FontWeight.w600),
                          ),
                        ],
                      ),
                    ],
                    const Divider(height: 18),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Text('Order Total', style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.bold)),
                        Text(
                          'LKR ${order.total.toStringAsFixed(0)}',
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
                        Text('Margin', style: theme.textTheme.bodySmall?.copyWith(color: scheme.outline)),
                        Text(
                          '${order.marginPercentage.toStringAsFixed(1)}% (LKR ${order.margin.toStringAsFixed(0)})',
                          style: theme.textTheme.bodySmall?.copyWith(
                            fontWeight: FontWeight.w600,
                            color: const Color(0xFF28A745),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 24),

              // Payment Action Section
              if (isPaid)
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: const Color(0xFFD4EDDA),
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: const Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.check_circle_rounded, color: Color(0xFF155724), size: 22),
                      SizedBox(width: 8),
                      Text(
                        'Payment Collected & Verified',
                        style: TextStyle(color: Color(0xFF155724), fontWeight: FontWeight.bold),
                      ),
                    ],
                  ),
                )
              else if (canPay)
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: _isGeneratingPayment ? null : _handleGeneratePayment,
                    icon: _isGeneratingPayment
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                          )
                        : const Icon(Icons.qr_code_rounded),
                    label: const Text(
                      'Customer Payment QR & Link',
                      style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
                    ),
                    style: FilledButton.styleFrom(
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                    ),
                  ),
                )
              else if (order.isPendingApproval)
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerHigh,
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.lock_clock_rounded, size: 20, color: scheme.outline),
                      const SizedBox(width: 8),
                      Text(
                        'Payment locked pending owner approval',
                        style: theme.textTheme.bodyMedium?.copyWith(color: scheme.outline),
                      ),
                    ],
                  ),
                ),
              const SizedBox(height: 24),
            ],
          ),
        ),
      ),
    );
  }
}
