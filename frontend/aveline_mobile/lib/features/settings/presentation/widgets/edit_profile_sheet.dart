import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// The details the associate can change about themselves.
///
/// A record rather than a small class: the two fields travel together, and naming
/// them at the call site is the whole contract.
typedef ProfileDetails = ({String displayName, String phoneNumber});

/// Asks for the details the API lets a user change about their own profile.
///
/// Returns `null` when the sheet was dismissed, so the caller can tell "left it
/// alone" from "cleared it".
Future<ProfileDetails?> showEditProfileSheet(
  BuildContext context, {
  required String displayName,
  required String phoneNumber,
}) {
  final scheme = Theme.of(context).colorScheme;

  return showModalBottomSheet<ProfileDetails>(
    context: context,
    // The fields need the room the keyboard takes, so the sheet is not capped at
    // half the screen and lifts with the inset instead.
    isScrollControlled: true,
    backgroundColor: scheme.surfaceContainerLowest,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
    ),
    builder: (context) => _EditProfileSheet(
      displayName: displayName,
      phoneNumber: phoneNumber,
    ),
  );
}

class _EditProfileSheet extends StatefulWidget {
  const _EditProfileSheet({
    required this.displayName,
    required this.phoneNumber,
  });

  final String displayName;
  final String phoneNumber;

  @override
  State<_EditProfileSheet> createState() => _EditProfileSheetState();
}

class _EditProfileSheetState extends State<_EditProfileSheet> {
  /// The lengths `UpdateProfileRequest` accepts, enforced here so the field
  /// cannot hand the API something it will answer with a 400.
  static const int _nameLimit = 200;
  static const int _phoneLimit = 20;

  late final TextEditingController _displayName;
  late final TextEditingController _phoneNumber;

  @override
  void initState() {
    super.initState();
    _displayName = TextEditingController(text: widget.displayName);
    _phoneNumber = TextEditingController(text: widget.phoneNumber);
  }

  @override
  void dispose() {
    _displayName.dispose();
    _phoneNumber.dispose();
    super.dispose();
  }

  void _save() {
    Navigator.of(context).pop((
      displayName: _displayName.text.trim(),
      phoneNumber: _phoneNumber.text.trim(),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return SafeArea(
      key: const Key('edit_profile_sheet'),
      child: Padding(
        // Lifts the fields clear of the keyboard rather than under it.
        padding: EdgeInsets.only(
          left: 20,
          right: 20,
          top: 20,
          bottom: 20 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // The fields scroll when the keyboard and a large text scale leave
            // no room, while the two actions stay where they are.
            Flexible(
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Your details', style: theme.textTheme.headlineSmall),
                    const SizedBox(height: 6),
                    Text(
                      'This is how the boutique sees you on the floor.',
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: 20),
                    TextField(
                      key: const Key('edit_profile_display_name'),
                      controller: _displayName,
                      textCapitalization: TextCapitalization.words,
                      textInputAction: TextInputAction.next,
                      maxLength: _nameLimit,
                      // The limit is a guard rail rather than information worth a
                      // permanent `0/200` on a two-field sheet.
                      buildCounter: _hideCounter,
                      decoration: const InputDecoration(
                        labelText: 'Display name',
                        hintText: 'How you are introduced to clients',
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      key: const Key('edit_profile_phone'),
                      controller: _phoneNumber,
                      keyboardType: TextInputType.phone,
                      textInputAction: TextInputAction.done,
                      maxLength: _phoneLimit,
                      buildCounter: _hideCounter,
                      inputFormatters: [
                        LengthLimitingTextInputFormatter(_phoneLimit),
                      ],
                      onSubmitted: (_) => _save(),
                      decoration: const InputDecoration(
                        labelText: 'Phone number',
                        hintText: '+94 77 123 4567',
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 16),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                TextButton(
                  key: const Key('edit_profile_cancel'),
                  onPressed: () => Navigator.of(context).pop(),
                  child: const Text('Cancel'),
                ),
                const SizedBox(width: 8),
                FilledButton(
                  key: const Key('edit_profile_save'),
                  onPressed: _save,
                  child: const Text('Save'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  /// Draws nothing where the counter would be.
  Widget? _hideCounter(
    BuildContext context, {
    required int currentLength,
    required bool isFocused,
    required int? maxLength,
  }) => null;
}
