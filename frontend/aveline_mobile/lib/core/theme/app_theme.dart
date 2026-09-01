import 'package:flutter/material.dart';

/// App-wide Material 3 theme.
abstract final class AppTheme {
  /// Brand seed color.
  static const Color seedColor = Color(0xFF7B4B6F);

  static ThemeData get light {
    final scheme = ColorScheme.fromSeed(seedColor: seedColor);

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: scheme.surface,
      appBarTheme: AppBarTheme(
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        elevation: 0,
      ),
      inputDecorationTheme: InputDecorationTheme(
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
      ),
    );
  }
}
