import 'package:flutter/material.dart';

/// The shape a chat composer's field wears.
///
/// The app's forms are pills: `inputDecorationTheme` rounds them fully, which suits a
/// one-line field beside a label. A chat composer is a different thing — it sits at
/// the bottom of a surface, it grows to four lines, and a fully rounded box at that
/// width reads as a button rather than as somewhere to type. It is drawn as a rounded
/// rectangle instead, with the padding a single line of text actually needs.
const double chatInputRadius = 18;

/// The decoration for a chat composer's field.
InputDecoration chatInputDecoration(
  BuildContext context, {
  required String hintText,
}) {
  final scheme = Theme.of(context).colorScheme;
  final border = OutlineInputBorder(
    borderRadius: BorderRadius.circular(chatInputRadius),
    borderSide: BorderSide(color: scheme.outlineVariant),
  );

  return InputDecoration(
    hintText: hintText,
    isDense: true,
    filled: true,
    fillColor: scheme.surfaceContainerLow,
    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
    border: border,
    enabledBorder: border,
    disabledBorder: border,
    focusedBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(chatInputRadius),
      borderSide: BorderSide(color: scheme.primary, width: 1.5),
    ),
  );
}

/// The type a chat composer's field sets, at the size the transcript reads at.
TextStyle? chatInputStyle(BuildContext context) =>
    Theme.of(context).textTheme.bodyMedium?.copyWith(
          fontSize: 14,
          height: 1.45,
        );
