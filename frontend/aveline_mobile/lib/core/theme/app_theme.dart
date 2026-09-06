import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Aveline — "Serene Concierge" Material 3 theme.
///
/// Colors and typography follow the brand tokens in
/// `.agents/brain/DESIGN.md`; layout spacing is intentionally not applied.
abstract final class AppTheme {
  /// Brand wine-rose used for key actions and active states.
  static const Color seedColor = Color(0xFF8B2E42);

  static ColorScheme get colorScheme => const ColorScheme(
        brightness: Brightness.light,
        primary: Color(0xFF8B2E42),
        onPrimary: Color(0xFFFFFFFF),
        primaryContainer: Color(0xFFA84056),
        onPrimaryContainer: Color(0xFFFFBBC6),
        secondary: Color(0xFF625D5D),
        onSecondary: Color(0xFFFFFFFF),
        secondaryContainer: Color(0xFFE6DEDD),
        onSecondaryContainer: Color(0xFF666161),
        tertiary: Color(0xFF3B3030),
        onTertiary: Color(0xFFFFFFFF),
        tertiaryContainer: Color(0xFF534646),
        onTertiaryContainer: Color(0xFFC6B4B4),
        error: Color(0xFFBA1A1A),
        onError: Color(0xFFFFFFFF),
        errorContainer: Color(0xFFFFDAD6),
        onErrorContainer: Color(0xFF93000A),
        surface: Color(0xFFFFF8F7),
        onSurface: Color(0xFF1E1B1B),
        surfaceDim: Color(0xFFE1D8D8),
        surfaceBright: Color(0xFFFFF8F7),
        surfaceContainerLowest: Color(0xFFFFFFFF),
        surfaceContainerLow: Color(0xFFFBF2F1),
        surfaceContainer: Color(0xFFF5ECEB),
        surfaceContainerHigh: Color(0xFFEFE6E6),
        surfaceContainerHighest: Color(0xFFE9E0E0),
        onSurfaceVariant: Color(0xFF534244),
        outline: Color(0xFF867274),
        outlineVariant: Color(0xFFD9C1C3),
        inverseSurface: Color(0xFF342F2F),
        onInverseSurface: Color(0xFFF8EFEE),
        inversePrimary: Color(0xFFFFCDD5),
        surfaceTint: Color(0xFFB3556A),
      );

  /// Brand typography: Playfair Display for display/headlines,
  /// DM Sans for UI and body.
  static TextTheme get textTheme => TextTheme(
        displayLarge: GoogleFonts.playfairDisplay(
          fontSize: 40,
          fontWeight: FontWeight.w500,
          height: 1.2,
          letterSpacing: -0.5,
        ),
        displaySmall: GoogleFonts.playfairDisplay(
          fontSize: 32,
          fontWeight: FontWeight.w500,
          height: 1.2,
        ),
        headlineMedium: GoogleFonts.playfairDisplay(
          fontSize: 28,
          fontWeight: FontWeight.w500,
          height: 1.3,
        ),
        headlineSmall: GoogleFonts.playfairDisplay(
          fontSize: 24,
          fontWeight: FontWeight.w500,
          height: 1.3,
        ),
        titleLarge: GoogleFonts.dmSans(
          fontSize: 20,
          fontWeight: FontWeight.w600,
          height: 1.4,
        ),
        titleMedium: GoogleFonts.dmSans(
          fontSize: 16,
          fontWeight: FontWeight.w600,
          height: 1.4,
        ),
        bodyLarge: GoogleFonts.dmSans(
          fontSize: 18,
          fontWeight: FontWeight.w400,
          height: 1.6,
        ),
        bodyMedium: GoogleFonts.dmSans(
          fontSize: 16,
          fontWeight: FontWeight.w400,
          height: 1.6,
        ),
        labelMedium: GoogleFonts.dmSans(
          fontSize: 14,
          fontWeight: FontWeight.w500,
          height: 1.2,
          letterSpacing: 0.7,
        ),
        labelSmall: GoogleFonts.dmSans(
          fontSize: 12,
          fontWeight: FontWeight.w600,
          height: 1.2,
        ),
      );

  static ThemeData get light {
    final scheme = colorScheme;
    const radius = 12.0;
    const inputShape = RoundedRectangleBorder(
      borderRadius: BorderRadius.all(Radius.circular(radius)),
    );

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      textTheme: textTheme,
      scaffoldBackgroundColor: scheme.surface,
      appBarTheme: AppBarTheme(
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        elevation: 0,
      ),
      cardTheme: CardThemeData(
        color: scheme.surfaceContainerLow,
        elevation: 0,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(16)),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: scheme.primaryContainer,
          foregroundColor: scheme.onPrimary,
          shape: inputShape,
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: scheme.surfaceContainerLow,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radius),
          borderSide: BorderSide(color: scheme.outlineVariant),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radius),
          borderSide: BorderSide(color: scheme.outlineVariant),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(radius),
          borderSide: BorderSide(color: scheme.primary, width: 1.5),
        ),
      ),
    );
  }
}
