import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/auth/app_roles.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/utils/greeting.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../auth/domain/auth_repository.dart';
import '../../data/demo_client_highlights.dart';
import '../../data/demo_focus_tasks.dart';
import '../../domain/client_highlight.dart';
import '../../domain/focus_task.dart';
import '../widgets/client_link_section.dart';
import '../widgets/focus_deck.dart';
import '../widgets/more_actions_sheet.dart';
import '../widgets/quick_actions_row.dart';

/// Home dock tab: the time-of-day greeting, the floor tools, today's focus
/// deck, and the direct client link.
///
/// The old placeholder cards (Blossoms, Approvals) are gone; they reported
/// numbers rather than telling the associate what to do next.
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, this.now, this.focusTasks, this.clients});

  /// Optional clock override so tests can pin the time-of-day salutation.
  /// Defaults to the device's local time.
  final DateTime? now;

  /// Optional focus deck, replacing the role-aware demo tasks.
  final List<FocusTask>? focusTasks;

  /// Optional client row, replacing the demo clients.
  final List<ClientHighlight>? clients;

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthRepository>();
    final user = auth.currentUser;

    final userRole = user?.userRole;
    final role = (userRole != null && userRole.isNotEmpty)
        ? userRole
        : (user?.orgRole ?? '');
    final tasks = focusTasks ?? demoFocusTasks(isOwner: AppRoles.isOwnerRole(role));

    return Stack(
      children: [
        const Positioned.fill(child: _HomeBackdrop()),
        ListView(
          // The animated Blossom floats over the bottom of the shell, so the
          // deck keeps enough room to scroll clear of it.
          padding: const EdgeInsets.only(top: 20, bottom: 96),
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: _GreetingHeading(firstName: user?.firstName, now: now),
            ),
            // The row is the greeting's toolbar rather than a section of its own:
            // it sits closer to the greeting than the sections below it do, and
            // carries no overline, which would compete with the greeting for the
            // role of heading.
            const SizedBox(height: 22),
            QuickActionsRow(
              onSelected: (action) => _handleQuickAction(context, action),
            ),
            const SizedBox(height: 30),
            FocusSection(tasks: tasks),
            const SizedBox(height: 28),
            ClientLinkSection(clients: clients ?? demoClientHighlights()),
          ],
        ),
      ],
    );
  }

  void _handleQuickAction(BuildContext context, QuickAction action) {
    if (action == QuickAction.catalog) {
      _push(context, AppRoutes.catalog);
      return;
    }
    if (action == QuickAction.messages) {
      _push(context, AppRoutes.conversations);
      return;
    }
    if (action == QuickAction.more) {
      showMoreActionsSheet(context);
      return;
    }
    AppToast.show(context, action.unavailableMessage ?? 'Not on mobile yet.');
  }

  /// The shell always mounts a GoRouter; a bare Home in a widget test simply
  /// does not navigate.
  void _push(BuildContext context, String route) {
    GoRouter.maybeOf(context)?.push(route);
  }
}

/// The Home backdrop: a whisper of warmth down the page, plus the brand
/// atmosphere faded out below the greeting.
///
/// It sits behind the scrolling column, so the atmosphere stays put while the
/// dockets move over it.
class _HomeBackdrop extends StatelessWidget {
  const _HomeBackdrop();

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    // Derived from the tokens rather than a magic hex: a 4.5% rose wash over
    // the surface, enough to stop the page reading as one flat field.
    final wash = Color.alphaBlend(
      scheme.primary.withValues(alpha: 0.045),
      scheme.surface,
    );

    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [scheme.surface, scheme.surface, wash],
          stops: const [0, 0.42, 1],
        ),
      ),
      child: const AuroraVeil(),
    );
  }
}

/// Personalized, time-aware greeting heading: `Good morning, Nadia.`
///
/// The salutation stays in the brand serif, while the user's first name picks
/// up the accent colour in italics, echoing the quiet-luxury welcome on the
/// web Overview.
class _GreetingHeading extends StatelessWidget {
  const _GreetingHeading({required this.firstName, this.now});

  final String? firstName;
  final DateTime? now;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final salutation = Greeting.salutationFor(now ?? DateTime.now());
    final name = firstName?.trim();
    final hasName = name != null && name.isNotEmpty;

    return Text.rich(
      TextSpan(
        children: [
          TextSpan(text: hasName ? '$salutation,' : '$salutation.'),
          if (hasName)
            TextSpan(
              text: ' $name.',
              style: theme.textTheme.headlineMedium?.copyWith(
                color: scheme.primary,
                fontStyle: FontStyle.italic,
              ),
            ),
        ],
      ),
      key: const Key('home_greeting'),
      style: theme.textTheme.headlineMedium,
      semanticsLabel: Greeting.forName(salutation, name),
    );
  }
}
