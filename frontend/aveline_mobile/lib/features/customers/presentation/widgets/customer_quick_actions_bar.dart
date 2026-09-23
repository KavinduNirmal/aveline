import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../domain/customer.dart';
import '../customer_palette.dart';

/// Quick communication and AI concierge launch bar for a client.
class CustomerQuickActionsBar extends StatelessWidget {
  const CustomerQuickActionsBar({
    super.key,
    required this.customer,
  });

  final Customer customer;

  Future<void> _launchUrlOrToast(
    BuildContext context,
    Uri uri, {
    required String fallbackMessage,
  }) async {
    try {
      final canLaunch = await canLaunchUrl(uri);
      if (canLaunch) {
        await launchUrl(uri, mode: LaunchMode.externalApplication);
      } else {
        if (context.mounted) {
          AppToast.show(context, fallbackMessage);
        }
      }
    } catch (_) {
      if (context.mounted) {
        AppToast.show(context, fallbackMessage);
      }
    }
  }

  void _call(BuildContext context) {
    final phone = customer.phoneNumber.replaceAll(RegExp(r'\s+'), '');
    if (phone.isEmpty) {
      AppToast.show(context, 'No phone number on file for ${customer.displayName}.');
      return;
    }
    _launchUrlOrToast(
      context,
      Uri.parse('tel:$phone'),
      fallbackMessage: 'Calling $phone',
    );
  }

  void _whatsapp(BuildContext context) {
    final phone = customer.phoneNumber.replaceAll(RegExp(r'[^\d+]'), '');
    if (phone.isEmpty) {
      AppToast.show(context, 'No phone number on file for ${customer.displayName}.');
      return;
    }
    _launchUrlOrToast(
      context,
      Uri.parse('https://wa.me/$phone'),
      fallbackMessage: 'Opening WhatsApp for ${customer.displayName}...',
    );
  }

  void _email(BuildContext context) {
    final email = customer.email;
    if (email == null || email.isEmpty) {
      AppToast.show(context, 'No email on file for ${customer.displayName}.');
      return;
    }
    _launchUrlOrToast(
      context,
      Uri.parse('mailto:$email'),
      fallbackMessage: 'Opening email for $email',
    );
  }

  void _consultSalon(BuildContext context) {
    AppToast.show(context, 'Opening Salon with ${customer.displayName}’s taste profile...');
    // If salon route or modal is available, route to salon
    context.push(AppRoutes.conversations);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Row(
      children: [
        Expanded(
          child: _QuickActionButton(
            icon: Icons.call_outlined,
            label: 'Call',
            tone: customerSlate,
            onPressed: () => _call(context),
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _QuickActionButton(
            icon: Icons.chat_bubble_outline,
            label: 'WhatsApp',
            tone: customerEmerald,
            onPressed: () => _whatsapp(context),
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _QuickActionButton(
            icon: Icons.mail_outline_rounded,
            label: 'Email',
            tone: customerLilac,
            onPressed: () => _email(context),
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: _QuickActionButton(
            icon: Icons.auto_awesome_outlined,
            label: 'Salon AI',
            tone: scheme.primary,
            isPrimary: true,
            onPressed: () => _consultSalon(context),
          ),
        ),
      ],
    );
  }
}

class _QuickActionButton extends StatelessWidget {
  const _QuickActionButton({
    required this.icon,
    required this.label,
    required this.tone,
    required this.onPressed,
    this.isPrimary = false,
  });

  final IconData icon;
  final String label;
  final Color tone;
  final VoidCallback onPressed;
  final bool isPrimary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      color: isPrimary
          ? scheme.primary.withValues(alpha: 0.12)
          : scheme.surfaceContainerLowest,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(14),
        side: BorderSide(
          color: isPrimary
              ? scheme.primary.withValues(alpha: 0.35)
              : scheme.outlineVariant.withValues(alpha: 0.35),
        ),
      ),
      elevation: 0,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onPressed,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 4),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 32,
                height: 32,
                decoration: BoxDecoration(
                  color: tone.withValues(alpha: 0.10),
                  shape: BoxShape.circle,
                ),
                child: Icon(icon, size: 16, color: tone),
              ),
              const SizedBox(height: 5),
              Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.labelSmall?.copyWith(
                  color: isPrimary ? scheme.primary : scheme.onSurface,
                  fontWeight: FontWeight.w600,
                  fontSize: 10.5,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
