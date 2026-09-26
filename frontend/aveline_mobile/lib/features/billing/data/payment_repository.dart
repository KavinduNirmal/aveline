import '../domain/top_up.dart';

/// The tenant purchase path's data source.
///
/// Four calls, one per route the payment module exposes (plan §9.2). The interface exists so a
/// widget test can drive the sheet without a server, exactly as [BlossomRepository] does for the
/// balance card.
abstract interface class PaymentRepository {
  /// The purchasable packs, from the server's own price book.
  ///
  /// Requires `billing:manage`: a caller who may not purchase is refused here rather than shown a
  /// catalogue it cannot use.
  Future<List<TopUpPack>> fetchTopUpPacks({required String organizationId});

  /// Creates the intent and returns the provider handoff.
  ///
  /// [idempotencyKey] belongs to the *operation*, not the request: a retry of the same purchase
  /// sends the same key.
  Future<TopUpCheckout> createTopUpCheckout({
    required String organizationId,
    required String skuCode,
    required String idempotencyKey,
  });

  /// The server's current state for one intent. The client polls this until [PaymentIntent.isTerminal].
  Future<PaymentIntent> fetchPaymentIntent({
    required String organizationId,
    required String paymentIntentId,
  });

  /// Abandons an unsettled intent. Its key is its own: reusing the checkout's key would be a
  /// different operation on the same key, which the server refuses.
  Future<PaymentIntent> cancelPaymentIntent({
    required String organizationId,
    required String paymentIntentId,
    required String idempotencyKey,
    String? reason,
  });
}

/// A payment call the app could not complete.
///
/// A refusal is modelled rather than returned as an empty list: "you may not buy Blossoms" and
/// "this boutique has no packs" are different facts, and only one of them is fixed by retrying.
class PaymentUnavailable implements Exception {
  const PaymentUnavailable(this.message, {this.statusCode});

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}
