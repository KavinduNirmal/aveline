import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/entities/order.dart';
import '../../domain/entities/payment.dart';
import '../controllers/approvals_realtime_controller.dart';

class PaymentQrModal extends StatefulWidget {
  const PaymentQrModal({
    super.key,
    required this.order,
    required this.payment,
    required this.controller,
  });

  final Order order;
  final Payment payment;
  final ApprovalsRealtimeController controller;

  static Future<void> show(
    BuildContext context, {
    required Order order,
    required Payment payment,
    required ApprovalsRealtimeController controller,
  }) {
    return showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => PaymentQrModal(
        order: order,
        payment: payment,
        controller: controller,
      ),
    );
  }

  @override
  State<PaymentQrModal> createState() => _PaymentQrModalState();
}

class _PaymentQrModalState extends State<PaymentQrModal> {
  Uint8List? _qrBytes;
  bool _isLoadingQr = true;
  bool _isConfirming = false;
  String? _qrError;

  @override
  void initState() {
    super.initState();
    _loadQrCode();
  }

  Future<void> _loadQrCode() async {
    final link = widget.payment.paymentLink;
    if (link == null || link.isEmpty) {
      setState(() {
        _isLoadingQr = false;
        _qrError = 'Payment link is not available.';
      });
      return;
    }

    try {
      final bytes = await widget.controller.getPaymentQrBytes(link, size: 320);
      if (mounted) {
        setState(() {
          _qrBytes = Uint8List.fromList(bytes);
          _isLoadingQr = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _qrError = 'Failed to load QR code image.';
          _isLoadingQr = false;
        });
      }
    }
  }

  void _copyPaymentLink() {
    final link = widget.payment.paymentLink;
    if (link != null && link.isNotEmpty) {
      Clipboard.setData(ClipboardData(text: link));
      AppToast.show(context, 'Payment link copied to clipboard.');
    }
  }

  void _shareViaWhatsApp() {
    final link = widget.payment.paymentLink ?? '';
    final customer = widget.order.customerName;
    final amount = widget.payment.amount.toStringAsFixed(0);
    final text = 'Dear $customer, here is your payment link for Aveline Order #${widget.order.id.substring(0, 8)}: $link (Amount: LKR $amount)';

    Clipboard.setData(ClipboardData(text: text));
    AppToast.show(context, 'WhatsApp message copied to clipboard. Ready to paste.');
  }

  Future<void> _confirmManualPayment() async {
    setState(() => _isConfirming = true);
    try {
      await widget.controller.confirmPayment(
        widget.payment.id,
        // The counter says how the money arrived. Without a method the payment keeps the
        // generation-time default (`online`), and the takings ledger then calls a cash sale online.
        paymentMethod: 'cash',
      );
      if (mounted) {
        AppToast.show(context, 'Payment marked as confirmed!');
        Navigator.of(context).pop();
      }
    } catch (e) {
      if (mounted) {
        setState(() => _isConfirming = false);
        AppToast.show(context, 'Could not confirm payment: $e', error: true);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isPaid = widget.payment.isConfirmed;

    return Container(
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(28)),
      ),
      padding: const EdgeInsets.fromLTRB(24, 16, 24, 24),
      child: SafeArea(
        top: false,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: scheme.outlineVariant,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
              const SizedBox(height: 18),
              Text(
                'Customer Payment QR',
                style: theme.textTheme.headlineSmall?.copyWith(
                  fontFamily: 'Playfair Display',
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 6),
              Text(
                '${widget.order.customerName} · LKR ${widget.payment.amount.toStringAsFixed(0)}',
                style: theme.textTheme.titleMedium?.copyWith(
                  color: scheme.primary,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 20),

              // QR Code container
              Container(
                width: 240,
                height: 240,
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: scheme.outlineVariant),
                  boxShadow: [
                    BoxShadow(
                      color: scheme.primary.withValues(alpha: 0.08),
                      blurRadius: 20,
                      offset: const Offset(0, 8),
                    ),
                  ],
                ),
                child: _isLoadingQr
                    ? const Center(child: CircularProgressIndicator())
                    : _qrError != null
                        ? Center(
                            child: Text(
                              _qrError!,
                              textAlign: TextAlign.center,
                              style: TextStyle(color: scheme.error, fontSize: 12),
                            ),
                          )
                        : _qrBytes != null
                            ? Image.memory(
                                _qrBytes!,
                                fit: BoxFit.contain,
                                errorBuilder: (context, error, stackTrace) =>
                                    const Center(
                                  child: Icon(Icons.qr_code_2_rounded, size: 80),
                                ),
                              )
                            : const Icon(Icons.qr_code_2_rounded, size: 80),
              ),
              const SizedBox(height: 12),
              Text(
                'Scan with phone camera or banking app',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: scheme.outline,
                ),
              ),
              const SizedBox(height: 24),

              // Payment Link actions
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: _copyPaymentLink,
                      icon: const Icon(Icons.copy_rounded, size: 18),
                      label: const Text('Copy Link'),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(14),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: FilledButton.tonalIcon(
                      onPressed: _shareViaWhatsApp,
                      icon: const Icon(Icons.share_rounded, size: 18),
                      label: const Text('WhatsApp'),
                      style: FilledButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(14),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),

              // Manual on-site confirmation button
              if (!isPaid)
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: _isConfirming ? null : _confirmManualPayment,
                    icon: _isConfirming
                        ? const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.check_circle_outline_rounded, size: 18),
                    label: const Text('Mark Paid (Cash / Card Terminal)'),
                    style: FilledButton.styleFrom(
                      padding: const EdgeInsets.symmetric(vertical: 13),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(14),
                      ),
                    ),
                  ),
                )
              else
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFD4EDDA),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: const Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.check_circle_rounded, color: Color(0xFF155724), size: 18),
                      SizedBox(width: 8),
                      Text(
                        'Payment Confirmed',
                        style: TextStyle(
                          color: Color(0xFF155724),
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
