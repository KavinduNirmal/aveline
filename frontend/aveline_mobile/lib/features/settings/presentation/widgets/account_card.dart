import 'package:flutter/material.dart';

import '../../../../core/auth/app_roles.dart';
import '../../../auth/domain/aveline_user.dart';
import '../../../../shared/widgets/blossom.dart';

/// The account, as the settings page shows it.
///
/// This is the retired profile screen's content, moved into Settings rather than
/// duplicated beside it: the same identity, the same roles, and the one action
/// that changes any of it. It reads a [user] rather than a provider so the card
/// can be laid out and read on its own.
class SettingsAccountCard extends StatelessWidget {
  const SettingsAccountCard({
    super.key,
    required this.user,
    required this.onEditDetails,
    this.isSaving = false,
  });

  /// The signed-in associate.
  final AvelineUser user;

  /// Opens the sheet that changes the fields the associate owns.
  final VoidCallback onEditDetails;

  /// Whether a change is already on its way, which closes the action until it
  /// lands.
  final bool isSaving;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final imageUrl = user.profileImageUrl;
    final phone = user.phoneNumber;
    final teamRole = user.userRole;
    final storeRole = user.organizationRole;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 20),
          child: Column(
            children: [
              CircleAvatar(
                radius: 34,
                backgroundColor: scheme.primaryContainer.withValues(alpha: 0.15),
                backgroundImage:
                    imageUrl != null && imageUrl.isNotEmpty
                        ? NetworkImage(imageUrl)
                        : null,
                child: imageUrl == null || imageUrl.isEmpty
                    ? Blossom(size: 34, color: scheme.primaryContainer)
                    : null,
              ),
              const SizedBox(height: 14),
              Text(
                user.nameForDisplay(),
                key: const Key('settings_display_name'),
                style: theme.textTheme.titleLarge,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 4),
              Text(
                user.email,
                key: const Key('settings_email'),
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              // Only when there is one on file: an empty line saying nothing is
              // worse than no line, and the edit action is right below it.
              if (phone != null && phone.isNotEmpty) ...[
                const SizedBox(height: 4),
                Text(
                  phone,
                  key: const Key('settings_phone'),
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
              ],
              if (teamRole.isNotEmpty || storeRole.isNotEmpty) ...[
                const SizedBox(height: 14),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  alignment: WrapAlignment.center,
                  children: [
                    // Named rather than pasted: the claims carry `org:boutique_owner`
                    // and that is not what an associate should read about themselves.
                    if (teamRole.isNotEmpty)
                      _RoleChip(label: 'Team role: ${AppRoles.labelFor(teamRole)}'),
                    if (storeRole.isNotEmpty)
                      _RoleChip(label: 'Store role: ${AppRoles.labelFor(storeRole)}'),
                  ],
                ),
              ],
            ],
          ),
        ),
        const Divider(height: 1, indent: 16, endIndent: 16),
        Padding(
          padding: const EdgeInsets.fromLTRB(8, 4, 8, 4),
          child: Align(
            alignment: Alignment.centerLeft,
            child: TextButton.icon(
              key: const Key('settings_edit_details'),
              onPressed: isSaving ? null : onEditDetails,
              icon: const Icon(Icons.edit_outlined, size: 18),
              label: const Text('Edit details'),
            ),
          ),
        ),
      ],
    );
  }
}

/// A role the associate holds, in the brand's low-contrast chip treatment.
class _RoleChip extends StatelessWidget {
  const _RoleChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelMedium?.copyWith(color: scheme.primary),
      ),
    );
  }
}
