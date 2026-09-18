import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/auth/app_roles.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/utils/greeting.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/blossom_refresh.dart';
import '../../../auth/domain/auth_repository.dart';
import '../../../customers/data/customer_repository.dart';
import '../../../customers/data/demo_customer_repository.dart';
import '../../data/demo_blossom_usage.dart';
import '../../data/demo_client_highlights.dart';
import '../../data/demo_focus_tasks.dart';
import '../../domain/client_highlight.dart';
import '../../domain/focus_task.dart';
import '../widgets/blossom_usage_card.dart';
import '../widgets/client_link_section.dart';
import '../widgets/focus_deck.dart';
import '../widgets/log_visit_sheet.dart';
import '../widgets/more_actions_sheet.dart';
import '../widgets/quick_actions_row.dart';
import '../widgets/today_strip.dart';

/// Home dock tab: the time-of-day greeting, the floor tools, the floor at a
/// glance, today's focus deck, and the direct client link.
///
/// The old placeholder cards (Blossoms, Approvals) are gone; they reported
/// numbers rather than telling the associate what to do next. The numbers that
/// remain are ones this screen can back: how many dockets are left, how many
/// clients are waiting on a reply, and what the next commitment is.
class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
    this.now,
    this.focusTasks,
    this.clients,
    this.customerRepository,
  });

  /// Optional clock override so tests can pin the time-of-day salutation.
  /// Defaults to the device's local time.
  final DateTime? now;

  /// Optional focus deck, replacing the role-aware demo tasks.
  final List<FocusTask>? focusTasks;

  /// Optional client row, replacing the demo clients.
  final List<ClientHighlight>? clients;

  /// The book "Log a visit" picks a client from.
  ///
  /// The same repository the Customers tab is built on, because the id it hands
  /// back is what the profile screen is addressed by: a client this screen can
  /// name has to be a client the profile can open.
  final CustomerRepository? customerRepository;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  /// Home owns the deck, so the header count, the pile and the strip all read the
  /// same list: signing a docket off has to move all three at once.
  late List<FocusTask> _tasks;

  /// The role the deck was seeded for, so it is only reseeded when the signed-in
  /// user actually changes — an ordinary rebuild leaves the pile where the
  /// associate left it.
  late String _seededRole;

  /// The refresh revision the deck was seeded at.
  int _seededRevision = 0;

  @override
  void initState() {
    super.initState();
    _seededRole = _role;
    _tasks = _seedTasks();
  }

  List<FocusTask> _seedTasks() => List.of(
    widget.focusTasks ?? demoFocusTasks(isOwner: AppRoles.isOwnerRole(_role)),
  );

  /// The signed-in user's role, from either claim, as the route guards read it.
  String get _role {
    final user = context.read<AuthRepository>().currentUser;
    final userRole = user?.userRole;
    return (userRole != null && userRole.isNotEmpty)
        ? userRole
        : (user?.orgRole ?? '');
  }

  @override
  void didUpdateWidget(covariant HomeScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    final role = _role;
    if (role != _seededRole ||
        !listEquals(oldWidget.focusTasks, widget.focusTasks)) {
      _seededRole = role;
      _tasks = _seedTasks();
    }
  }

  void _complete(FocusTask task) {
    setState(() => _tasks.removeWhere((candidate) => candidate.id == task.id));
    AppToast.show(context, task.doneMessage);
  }

  @override
  Widget build(BuildContext context) {
    // A completed pull-to-refresh re-seeds the deck. This screen's data is demo
    // data, so re-fetching it means seeding it again; an inherited widget has no
    // `didUpdateWidget` counterpart, so the comparison lives here.
    final revision = RefreshScope.revisionOf(context);
    if (revision != _seededRevision) {
      _seededRevision = revision;
      _tasks = _seedTasks();
    }

    final user = context.read<AuthRepository>().currentUser;
    final clients = widget.clients ?? demoClientHighlights();

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        ListView(
          // The animated Blossom floats over the bottom of the shell, so the
          // deck keeps enough room to scroll clear of it.
          padding: const EdgeInsets.only(top: 20, bottom: 96),
          // Always scrollable, so the pull-to-refresh in the shell still arms on
          // a screen whose content is shorter than the viewport.
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: _GreetingHeading(
                firstName: user?.firstName,
                now: widget.now,
              ),
            ),
            // The row is the greeting's toolbar rather than a section of its own:
            // it sits closer to the greeting than the sections below it do, and
            // carries no overline, which would compete with the greeting for the
            // role of heading.
            const SizedBox(height: 22),
            QuickActionsRow(
              onSelected: (action) => _handleQuickAction(context, action),
            ),
            // `DESIGN.md` asks for a strict 32dp gap between major sections.
            const SizedBox(height: 32),
            TodayStrip(tasks: _tasks),
            const SizedBox(height: 32),
            FocusSection(tasks: _tasks, onComplete: _complete),
            const SizedBox(height: 32),
            ClientLinkSection(clients: clients),
            const SizedBox(height: 32),
            const BlossomUsageCard(usage: demoBlossomUsage),
          ],
        ),
      ],
    );
  }

  void _handleQuickAction(BuildContext context, QuickAction action) {
    if (action == QuickAction.logVisit) {
      showLogVisitSheet(
        context,
        repository: widget.customerRepository ?? DemoCustomerRepository(),
        // The sheet closes itself before this runs, so the profile arrives over
        // the page it was opened from rather than under a sheet.
        onClientSelected: (customerId) =>
            _push(context, AppRoutes.customer(customerId)),
      );
      return;
    }
    if (action == QuickAction.catalog) {
      _push(context, AppRoutes.catalog);
      return;
    }
    if (action == QuickAction.customers) {
      _push(context, AppRoutes.customers);
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

/// Personalized, time-aware greeting heading: `Good morning, Nadia.`
///
/// The salutation stays in the brand serif, while the user's first name picks
/// up the accent colour in italics, echoing the quiet-luxury welcome on the
/// web Overview.
///
/// `DESIGN.md` gives `display-lg` to personalized greetings by name, and this
/// is the one moment on the page that earns it.
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
              style: theme.textTheme.displayLarge?.copyWith(
                color: scheme.primary,
                fontStyle: FontStyle.italic,
              ),
            ),
        ],
      ),
      key: const Key('home_greeting'),
      style: theme.textTheme.displayLarge,
      semanticsLabel: Greeting.forName(salutation, name),
    );
  }
}
