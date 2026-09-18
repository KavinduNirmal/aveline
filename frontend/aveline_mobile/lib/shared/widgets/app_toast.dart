import 'package:flutter/material.dart';

/// Shows a lightweight toast-style [SnackBar] via the nearest [ScaffoldMessenger].
///
/// [message] is the text to show; when [error] is true it uses the theme's error
/// palette so failures stand out.
///
/// [actionLabel] and [onAction] add an action to the right of the message - the
/// Undo an inbox offers after a delete. Both are needed: a label with nothing
/// behind it is a dead end, and a callback with no label is invisible.
abstract final class AppToast {
  static void show(
    BuildContext context,
    String message, {
    bool error = false,
    String? actionLabel,
    VoidCallback? onAction,
    Duration duration = const Duration(seconds: 3),
  }) {
    final messenger = ScaffoldMessenger.maybeOf(context);
    if (messenger == null) {
      return;
    }
    final scheme = Theme.of(context).colorScheme;
    final foreground = error ? scheme.onErrorContainer : scheme.onInverseSurface;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(message, style: TextStyle(color: foreground)),
          behavior: SnackBarBehavior.floating,
          backgroundColor: error ? scheme.errorContainer : scheme.inverseSurface,
          duration: duration,
          action: actionLabel != null && onAction != null
              ? SnackBarAction(
                  label: actionLabel,
                  // The action has to out-read the message it sits beside, so it
                  // wears the brand's light rose rather than the default link blue.
                  textColor: scheme.inversePrimary,
                  onPressed: onAction,
                )
              : null,
        ),
      );
  }
}
