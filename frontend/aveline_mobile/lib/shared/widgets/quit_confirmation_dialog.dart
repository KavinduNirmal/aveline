import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// Confirmation shown before the app closes itself.
///
/// The shell shows this when the Android system back button is pressed while
/// the user is already on the root screen, so an edge swipe meant for the
/// navigation drawer can never close the app by accident.
///
/// The dialog is deliberately not barrier-dismissible and blocks the back
/// button: it is a confirmation, so it only closes through Cancel or Quit.
///
/// Returns `true` when the user confirms, and `false` when they cancel or when
/// there is no [Navigator] to host the dialog. A `null` result from a dismissed
/// dialog is normalised to `false`.
Future<bool> showQuitConfirmationDialog(
  BuildContext context, {
  String title = 'Leave Aveline?',
  String message =
      'Your session stays signed in, so you can pick up exactly where you '
      'left off.',
  String confirmLabel = 'Quit',
  String cancelLabel = 'Stay',
}) async {
  // `showDialog` needs an ancestor Navigator. Callers that render the shell
  // outside a MaterialApp fall back to quitting outright rather than throwing.
  final navigator = Navigator.maybeOf(context);
  if (navigator == null) {
    return true;
  }

  final confirmed = await showDialog<bool>(
    context: context,
    barrierDismissible: false,
    builder: (dialogContext) {
      final scheme = Theme.of(dialogContext).colorScheme;

      return PopScope(
        // The confirmation must be answered explicitly.
        canPop: false,
        child: AlertDialog(
          backgroundColor: scheme.surfaceContainerLowest,
          shape: const RoundedRectangleBorder(
            borderRadius: BorderRadius.all(Radius.circular(24)),
          ),
          icon: Icon(
            Icons.waving_hand_rounded,
            color: scheme.primary,
            size: 28,
          ),
          title: Text(title),
          content: Text(message),
          actions: [
            TextButton(
              key: const Key('quit_dialog_cancel'),
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: Text(cancelLabel),
            ),
            FilledButton(
              key: const Key('quit_dialog_confirm'),
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: Text(confirmLabel),
            ),
          ],
        ),
      );
    },
  );

  return confirmed ?? false;
}

/// Closes the app after the user has confirmed.
///
/// Wrapped so a platform that refuses to close (or a test binding with no
/// platform channel) surfaces as a no-op instead of an unhandled exception.
Future<void> quitAveline() async {
  try {
    await SystemNavigator.pop();
  } catch (error) {
    debugPrint('[quit] could not close the app: $error');
  }
}
