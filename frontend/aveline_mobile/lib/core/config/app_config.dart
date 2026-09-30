import 'package:flutter/foundation.dart';

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

  /// Default API base URL: platform-aware localhost mapping.
  /// Android emulator uses `10.0.2.2`, while Windows/macOS/Linux/Web/iOS use `localhost`.
  static String get defaultApiBaseUrl {
    if (kIsWeb) return 'http://localhost:5091';
    switch (defaultTargetPlatform) {
      case TargetPlatform.android:
        return 'http://10.0.2.2:5091';
      case TargetPlatform.iOS:
      case TargetPlatform.macOS:
      case TargetPlatform.windows:
      case TargetPlatform.linux:
      case TargetPlatform.fuchsia:
        return 'http://localhost:5091';
    }
  }

  static const String _defaultJwtTemplateName = 'jwt-aveline-v1';

  /// Default Clerk test publishable key for local development.
  static const String _defaultClerkKey =
      'pk_test_aW5zcGlyZWQtd2FydGhvZy04MjA4LmNsZXJrLmFjY291bnRzLmRldiQ';

  /// Public origin of the web app, used to turn a handbook citation's path (`/docs/team`) into a
  /// link the mobile app can open (ADR-025).
  ///
  /// Supplied via `--dart-define=AVELINE_WEB_BASE_URL=https://...`.
  ///
  /// Empty by default on purpose: a guessed host would send staff to a page that may not exist, so
  /// with no origin configured the citation is still shown and tapping tells the reader where it
  /// lives instead of opening an address nobody confirmed.
  static const String webBaseUrl = String.fromEnvironment('AVELINE_WEB_BASE_URL');

  /// Reads the app configuration from the compile-time environment.
  factory AppConfig.fromEnvironment() {
    final clerkPublishableKey = const String.fromEnvironment(
      'CLERK_PUBLISHABLE_KEY',
      defaultValue: _defaultClerkKey,
    );

    if (clerkPublishableKey.isEmpty) {
      throw StateError(
        'CLERK_PUBLISHABLE_KEY is not set. Run the app with:\n'
        'flutter run --dart-define=CLERK_PUBLISHABLE_KEY=pk_test_... '
        '--dart-define=API_BASE_URL=http://localhost:5091',
      );
    }

    final envApiUrl = const String.fromEnvironment('API_BASE_URL');

    return AppConfig(
      clerkPublishableKey: clerkPublishableKey,
      apiBaseUrl: envApiUrl.isNotEmpty ? envApiUrl : defaultApiBaseUrl,
      jwtTemplateName: const String.fromEnvironment(
        'JWT_TEMPLATE_NAME',
        defaultValue: _defaultJwtTemplateName,
      ),
    );
  }
}
