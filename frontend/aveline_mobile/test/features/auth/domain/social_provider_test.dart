import 'package:aveline_mobile/features/auth/domain/social_provider.dart';
import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:flutter_test/flutter_test.dart';

clerk.SocialConnection _connection({
  clerk.Strategy strategy = clerk.Strategy.oauthGoogle,
  String name = 'Google',
  String logoUrl = 'https://img.clerk.com/static/google.png',
  bool enabled = true,
  bool authenticatable = true,
  bool deprecated = false,
  bool notSelectable = false,
}) => clerk.SocialConnection(
  strategy: strategy,
  name: name,
  logoUrl: logoUrl,
  isEnabled: enabled,
  authenticatable: authenticatable,
  deprecated: deprecated,
  notSelectable: notSelectable,
);

void main() {
  group('SocialProvider.listFrom', () {
    test('keeps the connections that can complete a sign-in', () {
      final providers = SocialProvider.listFrom([
        _connection(),
        _connection(
          strategy: clerk.Strategy.oauthFacebook,
          name: 'Facebook',
        ),
      ]);

      expect(providers, hasLength(2));
      expect(providers.first.name, 'Google');
      expect(providers.first.strategy, clerk.Strategy.oauthGoogle);
      expect(providers.first.brand, 'google');
      expect(providers.last.brand, 'facebook');
    });

    test('drops a disabled connection', () {
      // The production instance is exactly this: Facebook configured in the
      // dashboard but switched off, and a button for it could only fail.
      final providers = SocialProvider.listFrom([
        _connection(),
        _connection(
          strategy: clerk.Strategy.oauthFacebook,
          name: 'Facebook',
          enabled: false,
        ),
      ]);

      expect(providers.map((p) => p.name), ['Google']);
    });

    test('drops connections that cannot authenticate, are retired, or are '
        'not selectable', () {
      final providers = SocialProvider.listFrom([
        _connection(authenticatable: false),
        _connection(strategy: clerk.Strategy.oauthGithub, deprecated: true),
        _connection(strategy: clerk.Strategy.oauthApple, notSelectable: true),
      ]);

      expect(providers, isEmpty);
    });

    test('carries the name and mark the instance serves', () {
      final providers = SocialProvider.listFrom([_connection()]);

      expect(providers.single.logoUrl, 'https://img.clerk.com/static/google.png');
    });

    test('treats an unknown provider as its own brand', () {
      // Clerk can turn on a provider this app has no mark for; the brand key is
      // what the fallback mark is chosen by, so it must not be lost.
      final providers = SocialProvider.listFrom([
        _connection(strategy: clerk.Strategy.oauthNotion, name: 'Notion'),
      ]);

      expect(providers.single.brand, 'notion');
    });
  });

  group('SocialProvider equality', () {
    test('two providers with the same connection are equal', () {
      expect(
        SocialProvider.listFrom([_connection()]).single,
        SocialProvider.listFrom([_connection()]).single,
      );
    });

    test('a different provider is not equal', () {
      expect(
        SocialProvider.listFrom([_connection()]).single,
        isNot(
          SocialProvider.listFrom([
            _connection(strategy: clerk.Strategy.oauthFacebook),
          ]).single,
        ),
      );
    });
  });
}
