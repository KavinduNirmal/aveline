import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/router/route_guards.dart';
import '../../domain/entities/order.dart';
import '../../domain/repositories/commerce_repository.dart';
import '../controllers/orders_controller.dart';

class OrdersListScreen extends StatefulWidget {
  const OrdersListScreen({
    super.key,
    required this.repository,
  });

  final CommerceRepository repository;

  @override
  State<OrdersListScreen> createState() => _OrdersListScreenState();
}

class _OrdersListScreenState extends State<OrdersListScreen> {
  late final OrdersController _controller;

  static const List<Map<String, String>> _statusFilters = [
    {'label': 'All', 'value': 'all'},
    {'label': 'Pending Approval', 'value': 'pending_approval'},
    {'label': 'Confirmed', 'value': 'confirmed'},
    {'label': 'Processing', 'value': 'processing'},
    {'label': 'Delivered', 'value': 'delivered'},
  ];

  @override
  void initState() {
    super.initState();
    _controller = OrdersController(repository: widget.repository);
    _controller.addListener(_onControllerChange);
    _controller.fetchOrders();
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

  Color _getStatusColor(String status, ColorScheme scheme) {
    switch (status.toLowerCase()) {
      case 'confirmed':
        return const Color(0xFF28A745);
      case 'pending_approval':
        return const Color(0xFFD39E00);
      case 'delivered':
        return scheme.primary;
      case 'cancelled':
        return scheme.error;
      default:
        return scheme.onSurfaceVariant;
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
          'Commerce Orders',
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
            onPressed: () => _controller.fetchOrders(),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.push(AppRoutes.createOrder),
        backgroundColor: scheme.primary,
        foregroundColor: scheme.onPrimary,
        icon: const Icon(Icons.add_rounded),
        label: const Text('New Order'),
      ),
      body: SafeArea(
        child: Column(
          children: [
            // Status filters
            SizedBox(
              height: 48,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 6),
                itemCount: _statusFilters.length,
                separatorBuilder: (_, __) => const SizedBox(width: 8),
                itemBuilder: (context, index) {
                  final filter = _statusFilters[index];
                  final isSelected = _controller.selectedStatus == filter['value'];
                  return FilterChip(
                    label: Text(filter['label']!),
                    selected: isSelected,
                    onSelected: (_) => _controller.setFilter(filter['value']!),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(16),
                    ),
                  );
                },
              ),
            ),
            const SizedBox(height: 8),

            // Orders list
            Expanded(
              child: RefreshIndicator(
                onRefresh: () => _controller.fetchOrders(),
                child: _controller.isLoading
                    ? const Center(child: CircularProgressIndicator())
                    : _controller.errorMessage != null
                        ? Center(
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Text(_controller.errorMessage!, style: TextStyle(color: scheme.error)),
                                const SizedBox(height: 12),
                                OutlinedButton(
                                  onPressed: () => _controller.fetchOrders(),
                                  child: const Text('Retry'),
                                ),
                              ],
                            ),
                          )
                        : _controller.orders.isEmpty
                            ? Center(
                                child: Column(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    Icon(Icons.receipt_long_outlined, size: 48, color: scheme.outline),
                                    const SizedBox(height: 12),
                                    Text(
                                      'No orders found matching this filter.',
                                      style: theme.textTheme.bodyMedium?.copyWith(color: scheme.outline),
                                    ),
                                  ],
                                ),
                              )
                            : ListView.separated(
                                padding: const EdgeInsets.fromLTRB(20, 8, 20, 80),
                                itemCount: _controller.orders.length,
                                separatorBuilder: (_, __) => const SizedBox(height: 12),
                                itemBuilder: (context, index) {
                                  final order = _controller.orders[index];
                                  final statusColor = _getStatusColor(order.status, scheme);
                                  return InkWell(
                                    onTap: () => context.push(AppRoutes.orderDetail(order.id)),
                                    borderRadius: BorderRadius.circular(20),
                                    child: Container(
                                      padding: const EdgeInsets.all(16),
                                      decoration: BoxDecoration(
                                        color: scheme.surfaceContainerLowest,
                                        borderRadius: BorderRadius.circular(20),
                                        border: Border.all(color: scheme.outlineVariant),
                                        boxShadow: [
                                          BoxShadow(
                                            color: const Color(0xFF8B2E42).withValues(alpha: 0.05),
                                            blurRadius: 16,
                                            offset: const Offset(0, 4),
                                          ),
                                        ],
                                      ),
                                      child: Column(
                                        crossAxisAlignment: CrossAxisAlignment.start,
                                        children: [
                                          Row(
                                            mainAxisAlignment: MainAxisAlignment.between,
                                            children: [
                                              Text(
                                                order.customerName,
                                                style: theme.textTheme.titleMedium?.copyWith(
                                                  fontWeight: FontWeight.bold,
                                                ),
                                              ),
                                              Container(
                                                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                                                decoration: BoxDecoration(
                                                  color: statusColor.withValues(alpha: 0.12),
                                                  borderRadius: BorderRadius.circular(10),
                                                ),
                                                child: Text(
                                                  order.status.replaceAll('_', ' ').toUpperCase(),
                                                  style: theme.textTheme.labelSmall?.copyWith(
                                                    color: statusColor,
                                                    fontWeight: FontWeight.bold,
                                                    fontSize: 10,
                                                  ),
                                                ),
                                              ),
                                            ],
                                          ),
                                          const SizedBox(height: 8),
                                          Row(
                                            mainAxisAlignment: MainAxisAlignment.between,
                                            children: [
                                              Text(
                                                '${order.orderType == 'whatsapp' ? 'WhatsApp' : 'In-Store'} · ${order.items.length} items',
                                                style: theme.textTheme.bodySmall?.copyWith(
                                                  color: scheme.onSurfaceVariant,
                                                ),
                                              ),
                                              Text(
                                                'LKR ${order.total.toStringAsFixed(0)}',
                                                style: theme.textTheme.titleMedium?.copyWith(
                                                  fontWeight: FontWeight.bold,
                                                  color: scheme.primary,
                                                ),
                                              ),
                                            ],
                                          ),
                                        ],
                                      ),
                                    ),
                                  );
                                },
                              ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
