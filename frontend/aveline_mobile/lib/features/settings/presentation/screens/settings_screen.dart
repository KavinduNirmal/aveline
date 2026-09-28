import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../core/auth/app_roles.dart';
import '../../../../core/auth/permissions.dart';
import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/providers/user_provider.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/blossom.dart';
import '../../../../shared/widgets/brand_section_title.dart';
import '../../../auth/domain/auth_repository.dart';
import '../../../auth/domain/aveline_user.dart';
import '../../../auth/domain/contact_preference.dart';
import '../widgets/account_card.dart';
import '../widgets/contact_preference_sheet.dart';
import '../widgets/edit_profile_sheet.dart';
import '../widgets/settings_row.dart';
import '../widgets/settings_section.dart';
import '../widgets/settings_switch_row.dart';

/// Settings dock tab: the associate's own account, how Aveline reaches them, and
/// - for the roles that may manage the shop - the shop's own block.
///
/// This is where the retired Profile screen lives now. Merging the two was the
/// point: an account is one of the things an associate sets, and a dock tab per
/// setting would have made the panel a list of nouns rather than of places.
///
/// The screen owns no account state. Every control reads the record
/// [UserProvider] holds and writes back through it, so a change the API refuses
/// puts itself back with nothing to undo here. Only the in-flight save is local,
/// because it belongs to this page rather than to the account.
class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key, this.boutiqueName, this.onSignOut});

  /// Overrides the boutique name, for tests and previews. When `null`, the name
  /// is read from [BoutiqueProvider], falling back to the brand.
  final String? boutiqueName;

  /// Overrides what signing out does. When `null`, the auth repository is asked.
  final VoidCallback? onSignOut;

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

/// Which change is on its way, so the control that started it can refuse a second
/// one until the answer lands.
enum _Saving { details, pushNotifications, contactPreference }

class _SettingsScreenState extends State<SettingsScreen> {
  /// What the title reads before a boutique name is known.
  static const String _fallbackName = 'Aveline';

  _Saving? _saving;

  /// The signed-in account, or `null` when no provider is above the screen or the
  /// profile has not loaded yet.
  AvelineUser? _userOrNull(BuildContext context) {
    try {
      return context.watch<UserProvider>().user;
    } catch (_) {
      return null;
    }
  }

  /// The boutique name, or `null` when no provider is above the screen.
  String? _boutiqueNameOrNull(BuildContext context) {
    try {
      return context.watch<BoutiqueProvider>().name;
    } catch (_) {
      return null;
    }
  }

  /// The boutique role, or `null` when no provider is above the screen.
  String? _boutiqueRoleOrNull(BuildContext context) {
    try {
      return context.watch<BoutiqueProvider>().boutiqueRole;
    } catch (_) {
      return null;
    }
  }

  /// Whether this account may manage the shop's own settings.
  ///
  /// The server requires `settings:manage` on the active membership's role
  /// (org:boutique_owner). If an active boutique role is available from
  /// BoutiqueProvider, it takes precedence over JWT claims.
  bool _canManageShop(AvelineUser user, String? boutiqueRole) {
    final effectiveRole = boutiqueRole ?? user.organizationRole;
    return Permissions.anyGranted(
      [effectiveRole],
      Permissions.settingsManage,
    );
  }

  /// Writes a change through the account the app holds, and says so when the API
  /// refuses it.
  ///
  /// The whole of a save funnels through here so that "one change at a time" and
  /// "report a refusal" are decided once rather than at each control.
  Future<void> _save(
    _Saving field,
    Future<bool> Function(Dio dio, UserProvider users) change,
  ) async {
    // One change at a time: a second one would race the first, and the answer
    // that arrived last would decide what the account holds.
    if (_saving != null) {
      return;
    }

    Dio? dio;
    try {
      dio = context.read<Dio>();
    } catch (_) {
      dio = null;
    }
    if (dio == null) {
      AppToast.show(context, 'Your settings could not be saved.', error: true);
      return;
    }

    final users = context.read<UserProvider>();
    setState(() => _saving = field);
    final saved = await change(dio, users);
    if (!mounted) {
      return;
    }

    if (!saved) {
      final reason = users.updateErrorMessage;
      users.clearUpdateError();
      AppToast.show(
        context,
        reason ?? 'Your settings were not saved.',
        error: true,
      );
    }
    setState(() => _saving = null);
  }

  Future<void> _setPushNotifications(bool value) => _save(
    _Saving.pushNotifications,
    (dio, users) => users.updateProfile(dio, pushNotificationsEnabled: value),
  );

  /// Asks which contact preference to keep, then stores the answer.
  Future<void> _pickContactPreference(ContactPreference current) async {
    final chosen = await showContactPreferenceSheet(context, current: current);
    // A dismissed sheet means the associate changed their mind, not that they
    // chose `None`, so nothing is written.
    if (!mounted || chosen == null || chosen == current) {
      return;
    }
    await _save(
      _Saving.contactPreference,
      (dio, users) => users.updateProfile(dio, contactPreference: chosen),
    );
  }

  /// Asks for the details the associate owns, then stores them.
  Future<void> _editDetails(AvelineUser user) async {
    final details = await showEditProfileSheet(
      context,
      displayName: user.displayName ?? '',
      phoneNumber: user.phoneNumber ?? '',
    );
    if (!mounted || details == null) {
      return;
    }
    final unchanged =
        details.displayName == (user.displayName ?? '') &&
        details.phoneNumber == (user.phoneNumber ?? '');
    if (unchanged) {
      return;
    }
    await _save(
      _Saving.details,
      (dio, users) => users.updateProfile(
        dio,
        displayName: details.displayName,
        phoneNumber: details.phoneNumber,
      ),
    );
  }

  void _signOut() {
    final override = widget.onSignOut;
    if (override != null) {
      override();
      return;
    }
    try {
      context.read<AuthRepository>().signOut();
    } catch (_) {
      // Without an auth repository there is nothing to sign out of; the shell
      // always supplies one, so this is only reachable from a bare test mount.
    }
  }

  @override
  Widget build(BuildContext context) {
    final boutiqueName =
        widget.boutiqueName ?? _boutiqueNameOrNull(context) ?? _fallbackName;
    final user = _userOrNull(context);

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        CustomScrollView(
          key: const Key('settings_scroll'),
          // Always scrollable, so the shell's pull-to-refresh still arms on a
          // page shorter than the viewport.
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(child: _header(boutiqueName)),
            SliverPadding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              sliver: SliverList.list(children: _sections(user, boutiqueName)),
            ),
            // The animated Blossom floats over the bottom of the shell, so the
            // last row keeps enough room to scroll clear of it.
            const SliverToBoxAdapter(child: SizedBox(height: 96)),
          ],
        ),
      ],
    );
  }

  Widget _header(String boutiqueName) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          BrandSectionTitle(
            boutiqueName: boutiqueName,
            section: 'Settings',
            titleKey: const Key('settings_title'),
          ),
          const SizedBox(height: 10),
          Text(
            'Your account, your shop, and how Aveline reaches you.',
            key: const Key('settings_summary'),
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

  /// The page's groups, in the order an associate meets them: themselves first,
  /// then the shop they belong to, then the way out.
  List<Widget> _sections(AvelineUser? user, String boutiqueName) {
    final boutiqueRole = _boutiqueRoleOrNull(context);
    final effectiveStoreRole = boutiqueRole ?? user?.organizationRole ?? '';

    return [
    SettingsSection(
      key: const Key('settings_section_account'),
      title: 'Account',
      note: user == null
          ? null
          : 'Your email and username come from your sign-in and cannot be changed here.',
      children: [
        if (user == null)
          const _AccountPending()
        else
          SettingsAccountCard(
            key: const Key('settings_account_card'),
            user: user,
            isSaving: _saving == _Saving.details,
            onEditDetails: () => _editDetails(user),
          ),
      ],
    ),
    // Nothing to read a switch off until the account is known, so the
    // preference groups wait rather than showing controls that would write a
    // guess.
    if (user != null)
      SettingsSection(
        key: const Key('settings_section_notifications'),
        title: 'Notifications & contact',
        note: 'Push carries client messages, approvals, payments and agent '
            'finds. With it off they still wait in the inbox.',
        children: [
          SettingsSwitchRow(
            key: const Key('settings_push_notifications'),
            icon: Icons.notifications_none_rounded,
            label: 'Push notifications',
            value: user.pushNotificationsEnabled,
            onChanged: _setPushNotifications,
          ),
          SettingsRow(
            key: const Key('settings_contact_preference'),
            icon: Icons.forum_outlined,
            label: 'Preferred contact',
            value: user.preferredContact.label,
            onTap: () => _pickContactPreference(user.preferredContact),
          ),
        ],
      ),
    if (user != null && _canManageShop(user, boutiqueRole))
      SettingsSection(
        key: const Key('settings_section_boutique'),
        title: 'Boutique',
        note: 'Brand voice, business rules, team and billing arrive with the '
            'shop-wide settings slice.',
        children: [
          SettingsRow(
            key: const Key('settings_boutique_name'),
            icon: Icons.storefront_outlined,
            label: 'Boutique',
            value: boutiqueName,
          ),
          SettingsRow(
            key: const Key('settings_store_role'),
            icon: Icons.badge_outlined,
            label: 'Your store role',
            value: AppRoles.labelFor(effectiveStoreRole),
          ),
        ],
      ),
    SettingsSection(
      key: const Key('settings_section_session'),
      title: 'Session',
      note: 'You will need your email and password to come back.',
      children: [
        SettingsRow(
          key: const Key('settings_sign_out'),
          icon: Icons.logout_rounded,
          label: 'Sign out',
          destructive: true,
          onTap: _signOut,
        ),
      ],
    ),
  ];
  }
}

/// What the account group shows while the profile is still on its way.
///
/// Deliberately still rather than a spinner: nothing is being fetched from here,
/// and a turning dial on a page of dockets reads as a page that is working.
class _AccountPending extends StatelessWidget {
  const _AccountPending();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      key: const Key('settings_account_loading'),
      padding: const EdgeInsets.all(24),
      child: Row(
        children: [
          Blossom(size: 28, color: scheme.primaryContainer),
          const SizedBox(width: 14),
          Expanded(
            child: Text(
              'Your account is still loading.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
