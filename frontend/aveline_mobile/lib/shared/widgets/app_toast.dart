import 'package:flutter/material.dart';

/// Shows a lightweight toast-style [SnackBar] via the nearest [ScaffoldMessenger].
///
/// [message] is the text to show; when [error] is true it uses the theme's error
/// palette so failures stand out.
abstract final class AppToast {
  static void show(
    BuildContext context,
    String message, {
    bool error = false,
  }) {
    final messenger = ScaffoldMessenger.maybeOf(context);
    if (messenger == null) {
      return;
    }
    final scheme = Theme.of(context).colorScheme;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(
            message,
            style: TextStyle(
              color: error ? scheme.onErrorContainer : scheme.onInverseSurface,
            ),
          ),
          behavior: SnackBarBehavior.floating,
          backgroundColor: error ? scheme.errorContainer : scheme.inverseSurface,
          duration: const Duration(seconds: 3),
        ),
      );
  }
}
