import '../domain/blossom_usage.dart';

/// Reads the shop's Blossom position.
///
/// One method, because the card needs one number: the balance projection. The
/// endpoint is deliberately self-service (`billing:view:self`), so every org
/// role can call it.
abstract interface class BlossomRepository {
  /// The balance for [organizationId].
  ///
  /// Throws [BlossomBalanceUnavailable] when the server refuses; a refusal is
  /// not a zero balance.
  Future<BlossomUsage> fetchBalance({required String organizationId});
}

/// The balance could not be read.
///
/// Modelled so the card can say "not available" rather than rendering `0`, which
/// would be a different and false statement about the shop.
class BlossomBalanceUnavailable implements Exception {
  const BlossomBalanceUnavailable(this.message, {this.statusCode});

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}
