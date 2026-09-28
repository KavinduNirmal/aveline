import 'package:flutter/material.dart';

import '../../domain/entities/approval_entry.dart';
import '../../domain/entities/order.dart';

class ApprovalStatusBanner extends StatelessWidget {
  const ApprovalStatusBanner({
    super.key,
    required this.order,
    this.approval,
    this.isRealtimeConnected = false,
  });

  final Order order;
  final ApprovalEntry? approval;
  final bool isRealtimeConnected;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (!order.isPendingApproval && approval == null) {
      return const SizedBox.shrink();
    }

    final isPending = order.isPendingApproval || (approval?.isPending ?? false);
    final isApproved = approval?.isApproved ?? order.isConfirmed;
    final isRejected = approval?.isRejected ?? order.isCancelled;

    Color bannerColor;
    Color iconColor;
    IconData icon;
    String title;
    String subtitle;

    if (isPending) {
      bannerColor = const Color(0xFFFFF3CD);
      iconColor = const Color(0xFF856404);
      icon = Icons.hourglass_top_rounded;
      title = 'Pending Boutique Owner Approval';
      subtitle = approval?.reason.isNotEmpty == true
          ? approval!.reason
          : 'High order value or custom discount requires management sign-off.';
    } else if (isApproved) {
      bannerColor = const Color(0xFFD4EDDA);
      iconColor = const Color(0xFF155724);
      icon = Icons.check_circle_rounded;
      title = 'Order Approved by Owner';
      subtitle = approval?.decisionComment?.isNotEmpty == true
          ? 'Note: "${approval!.decisionComment}"'
          : 'Approved for processing and payment generation.';
    } else if (isRejected) {
      bannerColor = const Color(0xFFF8D7DA);
      iconColor = const Color(0xFF721C24);
      icon = Icons.cancel_rounded;
      title = 'Order Rejected';
      subtitle = approval?.decisionComment?.isNotEmpty == true
          ? 'Reason: "${approval!.decisionComment}"'
          : 'The order discount or terms were declined.';
    } else {
      bannerColor = scheme.surfaceContainerHigh;
      iconColor = scheme.onSurfaceVariant;
      icon = Icons.info_outline_rounded;
      title = 'Approval Status: ${approval?.status ?? order.status}';
      subtitle = approval?.reason ?? '';
    }

    return Container(
      margin: const EdgeInsets.symmetric(vertical: 8),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: bannerColor,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: iconColor.withValues(alpha: 0.25)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(icon, size: 20, color: iconColor),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  title,
                  style: theme.textTheme.titleSmall?.copyWith(
                    fontWeight: FontWeight.w600,
                    color: iconColor,
                  ),
                ),
              ),
              if (isPending && isRealtimeConnected)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: iconColor.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Container(
                        width: 6,
                        height: 6,
                        decoration: const BoxDecoration(
                          color: Color(0xFF28A745),
                          shape: BoxShape.circle,
                        ),
                      ),
                      const SizedBox(width: 4),
                      Text(
                        'LIVE',
                        style: theme.textTheme.labelSmall?.copyWith(
                          fontSize: 9,
                          fontWeight: FontWeight.bold,
                          color: iconColor,
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            subtitle,
            style: theme.textTheme.bodySmall?.copyWith(
              color: iconColor.withValues(alpha: 0.9),
            ),
          ),
        ],
      ),
    );
  }
}
