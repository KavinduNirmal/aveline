import 'dart:math';

/// A version-4 UUID, for the ids this device generates.
///
/// The send's idempotency key is the one that matters: the API's route and query
/// values are UUIDs, and the key must be stable across a retry while being unique
/// per composed message, which is exactly what a v4 random id is. `Random.secure`
/// rather than the default generator, because two devices composing at the same
/// instant must not collide.
String uuidV4([Random? random]) {
  final rng = random ?? Random.secure();
  final bytes = List<int>.generate(16, (_) => rng.nextInt(256));

  // Version 4 and the RFC 4122 variant, so the value is a well-formed v4 UUID.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  final hex = [
    for (final byte in bytes) byte.toRadixString(16).padLeft(2, '0'),
  ].join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
      '${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}
