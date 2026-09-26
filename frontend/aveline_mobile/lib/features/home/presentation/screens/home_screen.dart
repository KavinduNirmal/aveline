import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/auth/app_roles.dart';
import '../../../../core/auth/permission_guard.dart';
import '../../../../core/auth/permissions.dart';
import '../../../../core/config/app_config.dart';
import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/providers/user_provider.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/utils/greeting.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/blossom_refresh.dart';
import '../../../auth/domain/auth_repository.dart';
import '../../../billing/data/api_payment_repository.dart';
import '../../../billing/data/payment_repository.dart';
import '../../../billing/presentation/top_up_sheet.dart';
import '../../../customers/data/customer_repository.dart';
import '../../../customers/data/demo_customer_repository.dart';
import '../../domain/client_highlight.dart';
import '../../domain/focus_task.dart';
import '../home_controller.dart';
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
    this.paymentRepository,
  });

  /// Optional clock override so tests can pin the time-of-day salutation.
  /// Defaults to the device's local time.
  final DateTime? now;

  /// Optional focus deck, replacing what the [HomeController] holds.
  ///
  /// An explicit injection for tests; production passes nothing and reads the
  /// controller, which has no demo fallback.
  final List<FocusTask>? focusTasks;

  /// Optional client row, replacing what the [HomeController] holds.
  final List<ClientHighlight>? clients;

  /// The book "Log a visit" picks a client from.
  ///
  /// The same repository the Customers tab is built on, because the id it hands
  /// back is what the profile screen is addressed by: a client this screen can
  /// name has to be a client the profile can open.
  final CustomerBookSource? customerRepository;

  /// The purchase path behind "Request additional blossoms".
  ///
  /// An explicit injection for tests; production passes nothing and the screen
  /// reads the shared `Dio` the rest of the app is built on.
  final PaymentRepository? paymentRepository;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  /// The role the deck was last loaded for, so it is only reloaded when the
  /// signed-in user actually changes — an ordinary rebuild leaves the pile where
  /// the associate left it.
  late String _seededRole;

  /// The refresh revision the data was loaded at.
  int _seededRevision = 0;

  /// The organization id the last load was started for, so the screen reloads
  /// once `/orgs/my` has answered.
  String? _seededOrganizationId;

  @override
  void initState() {
    super.initState();
    _seededRole = _role;
    // Deferred to the end of the frame: the controller notifies its listeners
    // synchronously, and the provider above this screen is already listening
    // while `initState` runs. The first build reads the not-yet-loaded state, so
    // nothing is lost.
    _scheduleLoad();
  }

  /// The signed-in user's role, from either claim, as the route guards read it.
  String get _role {
    final user = context.read<AuthRepository>().currentUser;
    final userRole = user?.userRole;
    return (userRole != null && userRole.isNotEmpty)
        ? userRole
        : (user?.orgRole ?? '');
  }

  /// The screen's data source, or `null` when no controller is above this screen.
  ///
  /// A bare Home in a widget test has no provider; it degrades to an empty,
  /// non-throwing screen rather than reaching for a demo fallback. Production
  /// always provides one at `app.dart`.
  HomeController? get _controller {
    try {
      return context.read<HomeController>();
    } catch (_) {
      return null;
    }
  }

  void _load() {
    final controller = _controller;
    if (controller == null) {
      return;
    }
    unawaited(controller.load(ownerDeck: AppRoles.isOwnerRole(_seededRole)));
  }

  /// Starts a load after the current frame.
  ///
  /// The controller notifies synchronously, and both `initState` and
  /// `didUpdateWidget` run inside a build, so starting a load from either would
  /// mark the provider dirty mid-build.
  void _scheduleLoad() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        _load();
      }
    });
  }

  @override
  void didUpdateWidget(covariant HomeScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    final role = _role;
    if (role != _seededRole ||
        !listEquals(oldWidget.focusTasks, widget.focusTasks) ||
        !listEquals(oldWidget.clients, widget.clients)) {
      _seededRole = role;
      _scheduleLoad();
    }
  }

  /// Records the associate's decision, then reports it.
  ///
  /// The docket leaves the pile immediately and comes back if the server
  /// refuses; the toast is the server's answer, not the tap's.
  Future<void> _complete(FocusTask task) async {
    final controller = _controller;
    if (controller == null) {
      return;
    }

    final accepted = await controller.completeTask(task);
    if (!mounted) {
      return;
    }

    if (accepted) {
      AppToast.show(context, task.doneMessage);
    } else {
      AppToast.show(
        context,
        controller.actionError ?? 'That did not go through. Try again.',
      );
      controller.clearActionError();
    }
  }

  @override
  Widget build(BuildContext context) {
    // A completed pull-to-refresh re-reads the screen. An inherited widget has
    // no `didUpdateWidget` counterpart, so the comparison lives here; the reload
    // is deferred to the next frame because the controller notifies its
    // listeners, and notifying during a build is not allowed.
    final revision = RefreshScope.revisionOf(context);
    if (revision != _seededRevision) {
      _seededRevision = revision;
      _scheduleLoad();
    }

    final user = context.read<AuthRepository>().currentUser;

    HomeController? controller;
    try {
      controller = context.watch<HomeController>();
    } catch (_) {
      controller = null;
    }

    // Every Home route is org-scoped, and the canonical id comes from
    // `/orgs/my`. Until it arrives the screen keeps loading; when it does, the
    // load is retried. A bare Home in a test has no provider and does not care.
    BoutiqueProvider? boutique;
    try {
      boutique = context.watch<BoutiqueProvider>();
    } catch (_) {
      boutique = null;
    }
    final organizationId = boutique?.organizationId;
    if (organizationId != null && organizationId != _seededOrganizationId) {
      _seededOrganizationId = organizationId;
      _scheduleLoad();
    }

    // Explicit injections win, so a test can pin the screen's content without
    // standing up a controller; production injects nothing and reads the source.
    final injected = widget.focusTasks != null || widget.clients != null;
    final tasks = widget.focusTasks ?? controller?.tasks ?? const <FocusTask>[];
    final clients =
        widget.clients ?? controller?.clients ?? const <ClientHighlight>[];
    final balance = controller?.balance;

    // The top-up action: the same shared Dio the rest of the app uses in production, and an
    // explicit injection for a test. Absent either, the card's button stays disabled.
    final payments = widget.paymentRepository ?? _providedPayments(context);
    String? apiBaseUrl;
    try {
      apiBaseUrl = context.read<AppConfig>().apiBaseUrl;
    } catch (_) {
      apiBaseUrl = null;
    }
    final canBuyBlossoms =
        payments != null && organizationId != null && _canBuyBlossoms(context);

    final showLoading =
        !injected && controller != null && !controller.hasLoadedOnce;
    final errorMessage = injected ? null : controller?.errorMessage;

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
            if (showLoading)
              const HomeStatusCard.loading()
            else if (errorMessage != null)
              HomeStatusCard.error(message: errorMessage, onRetry: _load)
            else ...[
              TodayStrip(tasks: tasks),
              const SizedBox(height: 32),
              FocusSection(tasks: tasks, onComplete: _complete),
              const SizedBox(height: 32),
              // The row needs `customers:view`. A plain staff account does not
              // hold it, so the honest answer is no section rather than one that
              // can only answer 403. The trailing gap lives inside the guard so a
              // hidden section does not leave a double space above the meter.
              PermissionGuard(
                permission: Permissions.customersView,
                child: Padding(
                  padding: const EdgeInsets.only(bottom: 32),
                  child: ClientLinkSection(
                    clients: clients,
                    onCreateWalkIn: controller?.createWalkIn,
                  ),
                ),
              ),
              // The meter reads the self-service balance grant, which every org
              // role holds; a team role without it (or an account with no
              // membership) sees no meter rather than a 403.
              if (balance != null)
                PermissionGuard(
                  permission: Permissions.billingViewSelf,
                  child: BlossomUsageCard(
                    usage: balance,
                    // Only a role that may actually buy gets the action: the
                    // catalogue read behind it requires `billing:manage`, and a
                    // button that can only produce a 403 is worse than none.
                    onRequestMore: canBuyBlossoms
                        ? () => _openTopUp(
                              context,
                              organizationId,
                              payments,
                              apiBaseUrl,
                            )
                        : null,
                  ),
                ),
            ],
          ],
        ),
      ],
    );
  }

  /// The shared client, when this screen is mounted under the app's provider tree. A bare Home in
  /// a test has no provider, and then the caller must inject a repository or the button stays
  /// disabled.
  PaymentRepository? _providedPayments(BuildContext context) {
    try {
      return ApiPaymentRepository(context.read<Dio>());
    } catch (_) {
      return null;
    }
  }

  /// True when the signed-in role may buy Blossoms (`billing:manage`).
  bool _canBuyBlossoms(BuildContext context) {
    try {
      final user = context.read<UserProvider>().user;
      if (user == null) {
        return false;
      }
      return Permissions.anyGranted(
        [user.userRole, user.organizationRole],
        Permissions.billingManage,
      );
    } catch (_) {
      return false;
    }
  }

  /// Opens the purchase sheet and refreshes the meter when the server reports a settled top-up.
  void _openTopUp(
    BuildContext context,
    String organizationId,
    PaymentRepository repository,
    String? apiBaseUrl,
  ) {
    unawaited(
      showTopUpSheet(
        context,
        repository: repository,
        organizationId: organizationId,
        checkoutBaseUrl: apiBaseUrl,
        // The grant lands on the server, so the meter is re-read: showing the balance the server
        // reported before the purchase would understate what the shop now owns.
        onSettled: () {
          try {
            context.read<HomeController>().refresh(
              ownerDeck: AppRoles.isOwnerRole(_seededRole),
            );
          } catch (_) {
            /* the meter keeps its last measured value */
          }
        },
      ),
    );
  }

  void _handleQuickAction(BuildContext context, QuickAction action) {
    if (action == QuickAction.logVisit) {
      showLogVisitSheet(
        context,
        repository: widget.customerRepository ?? DemoCustomerRepository(),
        // The sheet closes itself before this runs, so the profile arrives over
        // the page it was opened from rather than under a sheet. The visit is
        // recorded first: "Log a visit" must not open a profile and log nothing.
        onClientSelected: (customerId) => _logVisit(customerId),
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

  /// Records the counter visit, then opens the profile.
  ///
  /// The receipt is the server's answer, not the tap's: a visit that could not be
  /// recorded says so instead of opening a profile that claims it happened.
  Future<void> _logVisit(String customerId) async {
    final controller = _controller;
    if (controller == null) {
      _push(context, AppRoutes.customer(customerId));
      return;
    }

    final recorded = await controller.recordVisit(customerId);
    if (!mounted) {
      return;
    }

    if (!recorded) {
      AppToast.show(
        context,
        controller.actionError ?? 'The visit could not be logged. Try again.',
      );
      controller.clearActionError();
      return;
    }

    AppToast.show(context, 'Visit logged.');
    _push(context, AppRoutes.customer(customerId));
  }

  /// The shell always mounts a GoRouter; a bare Home in a widget test simply
  /// does not navigate.
  void _push(BuildContext context, String route) {
    GoRouter.maybeOf(context)?.push(route);
  }
}

/// The screen's state while its data is on its way, or when it could not be
/// read.
///
/// Home has no demo fallback, so a failed feed is shown as a failure: an error
/// card with a retry, never a plausible-looking screen of fiction.
class HomeStatusCard extends StatelessWidget {
  const HomeStatusCard.loading({super.key})
      : message = null,
        onRetry = null,
        isLoading = true;

  const HomeStatusCard.error({
    super.key,
    required this.message,
    required this.onRetry,
  }) : isLoading = false;

  final String? message;
  final VoidCallback? onRetry;
  final bool isLoading;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Container(
        key: Key(isLoading ? 'home_loading' : 'home_error'),
        width: double.infinity,
        padding: const EdgeInsets.all(24),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: BorderRadius.circular(24),
          border: Border.all(color: scheme.outlineVariant),
        ),
        child: isLoading
            ? Row(
                children: [
                  const SizedBox(
                    width: 22,
                    height: 22,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Text(
                      "Loading the floor's work...",
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ),
                ],
              )
            : Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'The floor could not be read.',
                    style: theme.textTheme.titleMedium,
                  ),
                  const SizedBox(height: 6),
                  Text(
                    message ?? 'Try again in a moment.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 16),
                  OutlinedButton.icon(
                    onPressed: onRetry,
                    icon: const Icon(Icons.refresh_rounded, size: 18),
                    label: const Text('Try again'),
                  ),
                ],
              ),
      ),
    );
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
