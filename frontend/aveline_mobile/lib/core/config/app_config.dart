/// Compile-time configuration read from `--dart-define`.
class AppConfig {
  const AppConfig({
    required this.clerkPublishableKey,
    required this.apiBaseUrl,
    required this.jwtTemplateName,
  });

  /// Clerk publishable key (`pk_...`).
  ///
  /// Supplied via `--dart-define=CLERK_PUBLISHABLE_KEY=...`.
  final String clerkPublishableKey;

  /// Base URL of the Aveline API.
  ///
  /// Supplied via `--dart-define=API_BASE_URL=...`.
  final String apiBaseUrl;

  /// Clerk JWT template that mints tokens carrying the Aveline claims
  /// (`user_role`, `org_role`, `org_id`, `org_slug`).
  final String jwtTemplateName;

  /// Default API base URL: the host machine's local API as seen from an
  /// Android emulator (`10.0.2.2`).
  static const String _defaultApiBaseUrl = 'http://10.0.2.2:5091';

  static const String _defaultJwtTemplateName = 'jwt-aveline-v1';

  /// Reads the app configuration from the compile-time environment.
  factory AppConfig.fromEnvironment() {
    const clerkPublishableKey = String.fromEnvironment('CLERK_PUBLISHABLE_KEY');
    if (clerkPublishableKey.isEmpty) {
      throw StateError(
        'CLERK_PUBLISHABLE_KEY is not set. Run the app with:\n'
        'flutter run --dart-define=CLERK_PUBLISHABLE_KEY=pk_test_... '
        '--dart-define=API_BASE_URL=http://10.0.2.2:5091',
      );
    }

    return AppConfig(
      clerkPublishableKey: clerkPublishableKey,
      apiBaseUrl: String.fromEnvironment(
        'API_BASE_URL',
        defaultValue: _defaultApiBaseUrl,
      ),
      jwtTemplateName: String.fromEnvironment(
        'JWT_TEMPLATE_NAME',
        defaultValue: _defaultJwtTemplateName,
      ),
    );
  }
}
