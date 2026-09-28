/// The tenant purchase vocabulary (payment-gateway plan §9.2).
///
/// Two facts this file exists to keep straight:
///
/// 1. **A redirect is not settlement.** A [TopUpCheckout] is the handoff — where the customer is
///    sent — and its `status` is the provider's state at creation time. The terminal state is a
///    [PaymentIntent] read back from the server, which is why [isTerminalPaymentStatus] is the only
///    thing that decides whether a purchase is finished.
/// 2. **A status this build does not know is not a success.** An unrecognised status keeps the
///    client asking; only the five terminal states end the poll.
library;

/// A purchasable top-up pack, priced by the server's own price book.
class TopUpPack {
  const TopUpPack({
    required this.skuCode,
    required this.blossomQuantity,
    required this.priceLkr,
    required this.currency,
  });

  final String skuCode;
  final double blossomQuantity;
  final double priceLkr;
  final String currency;
}

/// The provider handoff a checkout returns. `checkoutUrl` is where the customer goes; it says
/// nothing about whether money moved.
class TopUpCheckout {
  const TopUpCheckout({
    required this.paymentIntentId,
    required this.provider,
    required this.status,
    required this.skuCode,
    required this.blossomQuantity,
    required this.amountLkr,
    required this.currency,
    required this.checkoutUrl,
    this.expiresAt,
  });

  final String paymentIntentId;
  final String provider;
  final String status;
  final String skuCode;
  final double blossomQuantity;
  final double amountLkr;
  final String currency;
  final String? checkoutUrl;
  final DateTime? expiresAt;
}

/// One read of an intent: the server's own terminal-state answer.
class PaymentIntent {
  const PaymentIntent({
    required this.paymentIntentId,
    required this.provider,
    required this.providerIntentId,
    required this.purpose,
    required this.status,
    required this.amountLkr,
    required this.currency,
    required this.checkoutUrl,
    required this.failureCode,
    required this.failureMessage,
    required this.createdAt,
    required this.settledAt,
    required this.expiresAt,
  });

  final String paymentIntentId;
  final String provider;
  final String providerIntentId;
  final String purpose;
  final String status;
  final double amountLkr;
  final String currency;
  final String? checkoutUrl;
  final String? failureCode;
  final String? failureMessage;
  final DateTime createdAt;
  final DateTime? settledAt;
  final DateTime? expiresAt;

  /// True only for a state the server has stopped working on.
  bool get isTerminal => isTerminalPaymentStatus(status);
}

/// The states that end a poll. `RequiresAction` and `Processing` are deliberately absent: the
/// first is a hosted page the customer may still close, the second a charge still in flight.
const Set<String> terminalPaymentStatuses = {
  'Succeeded',
  'Failed',
  'Cancelled',
  'Expired',
  'Refunded',
};

/// True only for a state the server can stop asking about. An unknown status is **not** terminal.
bool isTerminalPaymentStatus(String status) => terminalPaymentStatuses.contains(status);

/// How a finished (or still-running) purchase should read.
enum PaymentOutcomeTone { success, error, info }

/// The copy for an intent's server-reported state. Nothing here reads a redirect.
class PaymentOutcome {
  const PaymentOutcome(this.tone, this.title, this.detail);

  final PaymentOutcomeTone tone;
  final String title;
  final String detail;
}

PaymentOutcome paymentOutcome(PaymentIntent intent) {
  final amount = '${intent.currency} ${intent.amountLkr.toStringAsFixed(2)}';

  switch (intent.status) {
    case 'Succeeded':
      return PaymentOutcome(
        PaymentOutcomeTone.success,
        'Top-up complete',
        'Payment of $amount confirmed by ${intent.provider}. '
            'Your Blossom balance has been updated.',
      );
    case 'Failed':
      return PaymentOutcome(
        PaymentOutcomeTone.error,
        'Payment failed',
        intent.failureMessage == null
            ? 'The provider did not complete the charge. Nothing was granted.'
            : 'The provider declined the charge (${intent.failureMessage}). '
                'Nothing was granted.',
      );
    case 'Cancelled':
      return const PaymentOutcome(
        PaymentOutcomeTone.info,
        'Payment cancelled',
        'The checkout was abandoned. No money moved and your balance is unchanged.',
      );
    case 'Expired':
      return const PaymentOutcome(
        PaymentOutcomeTone.info,
        'Checkout expired',
        'The checkout window closed before payment completed. Nothing was charged.',
      );
    case 'Refunded':
      return const PaymentOutcome(
        PaymentOutcomeTone.info,
        'Payment refunded',
        'The charge was refunded, so the granted Blossoms were reversed.',
      );
    default:
      return const PaymentOutcome(
        PaymentOutcomeTone.info,
        'Waiting for the provider…',
        'Finish the payment in the checkout window. This screen confirms only when the '
            'provider reports the result back to Aveline.',
      );
  }
}
