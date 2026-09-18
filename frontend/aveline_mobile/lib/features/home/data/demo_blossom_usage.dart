import '../domain/blossom_usage.dart';

/// Stand-in Blossom meter until Home is wired to the usage endpoint.
///
/// Deliberately past the low-water mark, so the request action is the honest
/// thing to offer rather than decoration.
const BlossomUsage demoBlossomUsage = BlossomUsage(
  used: 168,
  allowance: 200,
  renewsOn: '1 October',
);
