import 'package:flutter/material.dart';

/// Shows a lightweight toast-style [SnackBar] via the nearest [ScaffoldMessenger].
///
/// [message] is the text to show; when [error] is true it uses the theme's error
/// palette so failures stand out.
///
/// [title] adds a short headline above [message]. A failure needs to say what
/// happened *and* what to do about it, and one line cannot carry both, so errors
/// are shown as a title plus a sentence.
///
/// [actionLabel] and [onAction] add an action to the right of the message - the
/// Undo an inbox offers after a delete. Both are needed: a label with nothing
/// behind it is a dead end, and a callback with no label is invisible.
abstract final class AppToast {
  static void show(
    BuildContext context,
    String message, {
    String? title,
    bool error = false,
    String? actionLabel,
    VoidCallback? onAction,
    Duration? duration,
  }) {
    final messenger = ScaffoldMessenger.maybeOf(context);
    if (messenger == null) {
      return;
    }
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final foreground = error ? scheme.onErrorContainer : scheme.onInverseSurface;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: title == null
              ? Text(message, style: TextStyle(color: foreground))
              : Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      style: theme.textTheme.titleSmall?.copyWith(
                        color: foreground,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      message,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: foreground,
                      ),
                    ),
                  ],
                ),
          behavior: SnackBarBehavior.floating,
          backgroundColor: error ? scheme.errorContainer : scheme.inverseSurface,
          // An error carries more to read than a confirmation, so it stays up
          // long enough to be read rather than being dismissed mid-sentence.
          duration: duration ??
              (error ? const Duration(seconds: 6) : const Duration(seconds: 3)),
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
