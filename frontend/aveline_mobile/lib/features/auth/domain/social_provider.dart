import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:flutter/foundation.dart';

/// A social sign-in provider the Clerk instance has enabled.
///
/// Built from the instance's own environment rather than hard-coded, because the
/// two instances disagree: production has Google only, while the development
/// instance also has Facebook. A fixed list, which is what the web renders,
/// offers a button that cannot complete a sign-in on whichever instance lacks it.
@immutable
class SocialProvider {
  /// Construct a [SocialProvider].
  const SocialProvider({
    required this.strategy,
    required this.name,
    this.logoUrl = '',
  });

  /// The Clerk strategy that starts this provider's consent flow
  /// (`Strategy.oauthGoogle`, `Strategy.oauthFacebook`, ...).
  final clerk.Strategy strategy;

  /// The provider's name as the instance spells it - "Google", "Facebook".
  final String name;

  /// The brand mark the instance serves for this provider. Decoration: the app
  /// draws the marks it knows locally and only falls back to the name when it
  /// does not recognise the provider.
  final String logoUrl;

  /// The provider's brand key - `google`, `facebook` - which is what the mark is
  /// chosen by, independent of how the instance capitalises [name].
  String get brand => strategy.provider ?? strategy.name;

  /// The providers in [connections] that can complete a sign-in.
  ///
  /// The instance's environment lists every connection it knows about, including
  /// disabled ones and ones Clerk has retired, so the filtering lives here rather
  /// than at each call site.
  static List<SocialProvider> listFrom(
    Iterable<clerk.SocialConnection> connections,
  ) => [
    for (final connection in connections)
      if (connection.isEnabled &&
          connection.authenticatable &&
          connection.deprecated == false &&
          connection.notSelectable == false)
        SocialProvider(
          strategy: connection.strategy,
          name: connection.name,
          logoUrl: connection.logoUrl,
        ),
  ];

  @override
  bool operator ==(Object other) =>
      other is SocialProvider &&
      other.strategy == strategy &&
      other.name == name &&
      other.logoUrl == logoUrl;

  @override
  int get hashCode => Object.hash(strategy, name, logoUrl);

  @override
  String toString() => 'SocialProvider($brand, name: $name)';
}
