import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/filter_pill.dart';
import '../../data/customer_repository.dart';
import '../../data/demo_customer_repository.dart';
import '../../domain/customer.dart';
import '../../domain/customer_detail.dart';
import '../customer_palette.dart';
import '../widgets/customer_avatar.dart';

/// One client's information screen, opened by tapping their row in the book.
///
/// The id is the only thing the route carries. A push could hand the whole
/// profile over as `extra` for an instant first frame, but the router re-parses
/// a location whenever the auth or profile listenable fires and `extra` does not
/// survive that, which would leave the screen with nothing to draw. Resolving
/// from the id makes the screen a pure function of the location.
///
/// Everything on it comes from the concierge module: the profile
/// (`CustomerProfileDto`), the preferences and consent it carries, and the
/// memories, occasions and interactions the module keeps alongside it.
///
/// The page is a dossier rather than a stack of identical cards. It opens on one
/// sheet that states who the client is — their name, the key the shop files them
/// under, the grade they hold, whether the boutique may contact them, and the
/// three figures the floor reads first. Nothing above that sheet competes with
/// it. Below it, the next occasion is the only block allowed to be loud, because
/// it is the only thing here with a deadline, and each later section takes the
/// shape its content wants: a contact card for ways to reach them, tiles for
/// taste, pills for tags, a signed panel for memory, a rail for history.
///
/// Consent is shown, never offered: the API records it from the client's own
/// channel, so the floor has no control for it here.
class CustomerScreen extends StatefulWidget {
  const CustomerScreen({
    super.key,
    required this.customerId,
    this.customer,
    this.repository,
  });

  /// The id the route was addressed by.
  final String customerId;

  /// Seeds the screen without a fetch, for previews and tests. Production
  /// routing does not use it.
  final CustomerDetail? customer;

  /// Overrides the source the profile is resolved from, for tests and previews.
  final CustomerRepository? repository;

  @override
  State<CustomerScreen> createState() => _CustomerScreenState();
}

class _CustomerScreenState extends State<CustomerScreen> {
  late final CustomerRepository _repository =
      widget.repository ?? DemoCustomerRepository();

  final ScrollController _scrollController = ScrollController();

  /// How far the page has scrolled, for the app bar's own state. Read rather
  /// than rebuilt on: a `ValueListenableBuilder` inside the bar keeps the whole
  /// page from rebuilding on every frame of a drag.
  final ValueNotifier<double> _scrollOffset = ValueNotifier<double>(0);

  CustomerDetail? _detail;
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
    _detail = widget.customer;
    _scrollController.addListener(_onScroll);
    if (_detail == null) {
      _resolve(widget.customerId);
    }
  }

  @override
  void dispose() {
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    _scrollOffset.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (!_scrollController.hasClients) {
      return;
    }
    _scrollOffset.value = _scrollController.offset;
  }

  @override
  void didUpdateWidget(covariant CustomerScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    // A later build may carry a seed where there was none, which is what a
    // widget test pumping twice does. The profile already held is never blanked:
    // the router re-parsing a location must not empty the screen.
    if (widget.customer != null && widget.customer != oldWidget.customer) {
      _detail = widget.customer;
    }
    if (widget.customerId != oldWidget.customerId) {
      _resolve(widget.customerId);
    }
  }

  Future<void> _resolve(String id) async {
    setState(() {
      _isLoading = true;
    });

    CustomerDetail? found;
    try {
      found = await _repository.fetchCustomer(id);
    } catch (_) {
      found = null;
    }

    if (!mounted || widget.customerId != id) {
      return;
    }

    setState(() {
      _isLoading = false;
      if (found != null) {
        _detail = found;
      }
    });
  }

  /// Records that the client came in.
  ///
  /// Local until the concierge API is wired: the module's
  /// `POST /{id}/interactions` is not called yet, so the screen shows what it did
  /// rather than implying the boutique's record changed.
  void _logVisit() {
    final detail = _detail;
    if (detail == null) {
      return;
    }

    final at = DateTime.now().toUtc();
    final visit = CustomerInteraction(
      id: 'local-visit-${at.microsecondsSinceEpoch}',
      channel: InteractionChannel.inPerson,
      direction: InteractionDirection.inbound,
      messageContent: 'Walked in; logged at the counter.',
      createdAtUtc: at,
    );

    setState(() {
      _detail = detail.copyWith(
        interactions: <CustomerInteraction>[visit, ...detail.interactions],
        customer: detail.customer.copyWith(
          visitCount: detail.customer.visitCount + 1,
          lastVisitAtUtc: at,
        ),
      );
    });
    AppToast.show(context, 'Visit added to ${detail.customer.displayName}.');
  }

  /// Recomputes the tier from spend, visits and recency.
  ///
  /// The same rule the API runs in `CustomerLoyaltyService.RecommendStatus`, so
  /// the answer here is the answer the recompute endpoint would give.
  void _recomputeStatus() {
    final detail = _detail;
    if (detail == null) {
      return;
    }

    final status = CustomerStatus.recommend(
      totalSpent: detail.customer.totalSpent,
      visitCount: detail.customer.visitCount,
      lastVisitAtUtc: detail.customer.lastVisitAtUtc,
    );

    setState(() {
      _detail = detail.copyWith(
        customer: detail.customer.copyWith(status: status),
      );
    });
    AppToast.show(context, 'Tier recomputed: ${status.label}.');
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Stack(
        children: [
          const Positioned.fill(child: BrandBackdrop()),
          Positioned.fill(
            child: _body(_detail, _scrollController),
          ),
          // After the page, so the back control stays reachable at any scroll
          // depth and the app bar can name the client once her own sheet has
          // gone under it.
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: _ProfileAppBar(
              detail: _detail,
              scrollOffset: _scrollOffset,
              onBack: () => Navigator.of(context).maybePop(),
            ),
          ),
        ],
      ),
    );
  }

  Widget _body(CustomerDetail? detail, ScrollController controller) {
    if (detail != null) {
      return _Profile(
        detail: detail,
        controller: controller,
        onLogVisit: _logVisit,
        onRecomputeStatus: _recomputeStatus,
      );
    }

    if (_isLoading) {
      return const Center(
        child: SizedBox(
          width: 24,
          height: 24,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }

    return ListView(
      padding: EdgeInsets.fromLTRB(
        20,
        MediaQuery.paddingOf(context).top + kToolbarHeight + 16,
        20,
        48,
      ),
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        _Panel(
          children: [
            Text(
              'Client not found',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 8),
            Text(
              widget.customerId.isEmpty
                  ? 'We could not open this client. Their record may have been '
                        'removed.'
                  : 'We could not open ${widget.customerId}. Their record may '
                        'have been removed, or the concierge could not be '
                        'reached.',
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('customer_retry'),
                onPressed: () => _resolve(widget.customerId),
                icon: const Icon(Icons.refresh_rounded, size: 16),
                label: const Text('Try again'),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

/// The back control, riding over the page rather than sitting on a bar.
///
/// A filled app bar put a hard white band across the top of the client's own
/// atmosphere, which is the one place on the page the brand gets to breathe. So
/// there is no bar: the control floats on the blossom as a paper circle, and the
/// only thing drawn across the top is a scrim that fades the page out as it
/// scrolls up under the control, for as long as it needs to.
///
/// The name fades in once the client's sheet has gone under the control. Nothing
/// is pinned twice: while the sheet is on screen the name is printed at its
/// largest, so repeating it above would only spend the top of the page on the
/// same word.
class _ProfileAppBar extends StatelessWidget {
  const _ProfileAppBar({
    required this.detail,
    required this.scrollOffset,
    required this.onBack,
  });

  final CustomerDetail? detail;
  final ValueListenable<double> scrollOffset;
  final VoidCallback onBack;

  /// The scroll depth at which the name is fully faded in.
  static const double _fadeEnd = 96;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final topInset = MediaQuery.paddingOf(context).top;

    return SizedBox(
      height: topInset + kToolbarHeight,
      child: Stack(
        children: [
          // A wash of the page's own surface rather than an opaque panel: it is
          // widest where the content is deepest under the control and gone by
          // the bottom of the band, so the atmosphere still reads through it.
          const Positioned.fill(child: _HeaderScrim()),
          Align(
            alignment: Alignment.centerLeft,
            child: Padding(
              padding: EdgeInsets.only(top: topInset, left: 12),
              child: _BackButton(onPressed: onBack),
            ),
          ),
          Align(
            alignment: Alignment.centerRight,
            child: Padding(
              padding: EdgeInsets.only(top: topInset, right: 12),
              child: ValueListenableBuilder<double>(
                valueListenable: scrollOffset,
                builder: (context, offset, child) => Opacity(
                  opacity: (offset / _fadeEnd).clamp(0.0, 1.0),
                  child: child,
                ),
                child: detail == null
                    ? const SizedBox.shrink()
                    : Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            detail!.customer.displayName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.labelLarge?.copyWith(
                              color: theme.colorScheme.onSurface,
                            ),
                          ),
                          const SizedBox(width: 10),
                          CustomerAvatar(customer: detail!.customer, size: 28),
                        ],
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// The band that keeps the top of the page legible while it scrolls away.
class _HeaderScrim extends StatelessWidget {
  const _HeaderScrim();

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [
            scheme.surface.withValues(alpha: 0.92),
            scheme.surface.withValues(alpha: 0.55),
            scheme.surface.withValues(alpha: 0.0),
          ],
          stops: const [0.0, 0.55, 1.0],
        ),
      ),
    );
  }
}

/// The way back, as a circle of paper on the page rather than as a bar's icon.
///
/// The shadow is warm and low so the circle lifts off the blossom without
/// drawing a line across it, which is what the app bar this replaced did.
class _BackButton extends StatelessWidget {
  const _BackButton({required this.onPressed});

  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final shape = CircleBorder(
      side: BorderSide(color: scheme.outlineVariant.withValues(alpha: 0.5)),
    );

    return Material(
      color: scheme.surfaceContainerLowest,
      shape: shape,
      elevation: 2,
      shadowColor: scheme.primary.withValues(alpha: 0.30),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        key: const Key('customer_back'),
        onTap: onPressed,
        customBorder: shape,
        child: Tooltip(
          message: 'Back',
          child: SizedBox(
            width: 42,
            height: 42,
            child: Icon(
              Icons.arrow_back_rounded,
              size: 20,
              color: scheme.onSurface,
            ),
          ),
        ),
      ),
    );
  }
}

/// The client, from the sheet that names them down to what the floor can do.
class _Profile extends StatelessWidget {
  const _Profile({
    required this.detail,
    required this.controller,
    required this.onLogVisit,
    required this.onRecomputeStatus,
  });

  final CustomerDetail detail;
  final ScrollController controller;
  final VoidCallback onLogVisit;
  final VoidCallback onRecomputeStatus;

  @override
  Widget build(BuildContext context) {
    // One clock for the page, so a countdown and an activity stamp cannot
    // disagree about what day it is.
    final now = DateTime.now();
    final customer = detail.customer;
    final upcoming = detail.upcomingEvents(now: now);
    final past = detail.pastEvents(now: now);

    return ListView(
      key: const Key('customer_scroll'),
      controller: controller,
      padding: EdgeInsets.only(
        top: MediaQuery.paddingOf(context).top + kToolbarHeight + 6,
        bottom: 96,
      ),
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: _ClientSheet(detail: detail, now: now),
        ),
        // The deadline is the one thing on this page that expires, so it is the
        // one block allowed to be loud, and it sits above everything read for
        // interest rather than for work.
        if (upcoming.isNotEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 18, 20, 0),
            child: _OccasionBand(event: upcoming.first, now: now),
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 30, 20, 0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const _SectionHeading('Reaching them'),
              _ContactCard(detail: detail),
              const SizedBox(height: 30),
              _SectionHeading(
                'Their taste',
                count: detail.preferences.length,
              ),
              _PreferenceGrid(preferences: detail.preferences),
              const SizedBox(height: 30),
              _SectionHeading('Boutique tags', count: customer.tags.length),
              _TagRow(tags: customer.tags),
              const SizedBox(height: 30),
              _SectionHeading(
                'What Aveline remembers',
                count: detail.memories.length,
              ),
              _MemoryPanel(memories: detail.memories),
              const SizedBox(height: 30),
              _SectionHeading('Recent activity', count: detail.interactions.length),
              _ActivityRail(interactions: detail.interactions, now: now),
              if (past.isNotEmpty) ...[
                const SizedBox(height: 30),
                const _SectionHeading('Past occasions'),
                _PastOccasions(events: past, now: now),
              ],
              const SizedBox(height: 30),
              const _SectionHeading('Actions'),
              _Actions(
                onLogVisit: onLogVisit,
                onRecomputeStatus: onRecomputeStatus,
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The client's own sheet: who she is, what she is worth, and whether the
/// boutique may reach her.
///
/// One white surface rather than a tinted band with a card floating on it: the
/// name is the largest thing on the page, and the wine in this sheet is spent on
/// the client's grade, which is the one mark on the page that summarises her.
class _ClientSheet extends StatelessWidget {
  const _ClientSheet({
    required this.detail,
    required this.now,
  });

  final CustomerDetail detail;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final customer = detail.customer;

    return Container(
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(22),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.07),
            blurRadius: 24,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Column(
        key: const Key('customer_identity'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(18, 18, 18, 0),
            child: _ClientIdentity(
              customer: customer,
              status: customer.status,
            ),
          ),
          const SizedBox(height: 16),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18),
            child: _KeyLine(detail: detail),
          ),
          const SizedBox(height: 16),
          Divider(
            height: 1,
            thickness: 1,
            color: scheme.outlineVariant.withValues(alpha: 0.45),
          ),
          _Ledger(detail: detail, now: now),
        ],
      ),
    );
  }
}

/// The largest statement on the page: the client's initials beside her name,
/// with the grade she holds as a single pill under the key the shop files her
/// under.
///
/// One grade and only one. The profile also carries `Customer.level`, but that
/// is a grade the mobile book invents (see [Customer]'s own note: the API derives
/// a status, not a grade). Printing both put "LVL 3" and "VIP" on the same
/// client, which reads as a contradiction rather than as two facts, and only one
/// of the two is a value the concierge would ever agree with.
class _ClientIdentity extends StatelessWidget {
  const _ClientIdentity({required this.customer, required this.status});

  final Customer customer;

  /// The grade the client holds, which is the API's own vocabulary.
  final CustomerStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    Widget name() => Text(
      customer.displayName,
      key: const Key('customer_name'),
      style: theme.textTheme.displayMedium?.copyWith(
        color: scheme.onSurface,
        height: 1.1,
      ),
    );

    Widget keyAndGrade() => Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          customer.idLabel,
          key: const Key('customer_id'),
          style: theme.textTheme.labelSmall?.copyWith(
            color: scheme.onSurfaceVariant,
            letterSpacing: 1.4,
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: 10),
        _GradePill(status: status),
        if (customer.hasNicknameAlias) ...[
          const SizedBox(height: 10),
          Text(
            'Called "${customer.nickname}" at the counter',
            style: theme.textTheme.bodySmall?.copyWith(
              color: scheme.onSurfaceVariant,
              fontStyle: FontStyle.italic,
            ),
          ),
        ],
      ],
    );

    // No `LayoutBuilder` here. It defers its children's layout by a frame, and
    // the hero is the first thing in the scroll view: every section under it
    // would then be measured a frame behind, which is enough to make a control
    // below the fold answer a tap where it used to be.
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        CustomerAvatar(customer: customer, size: 74),
        Container(
          width: 1,
          height: 56,
          margin: const EdgeInsets.fromLTRB(16, 6, 16, 0),
          color: scheme.outlineVariant.withValues(alpha: 0.5),
        ),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              // Flexible rather than fixed: a long name wraps under itself
              // instead of being squeezed into a column two words wide.
              name(),
              const SizedBox(height: 8),
              keyAndGrade(),
            ],
          ),
        ),
      ],
    );
  }
}

/// How the shop files the client, and whether it may contact her. Both are
/// properties of the record rather than things the floor came to do, so they sit
/// together rather than in a section of their own.
class _KeyLine extends StatelessWidget {
  const _KeyLine({required this.detail});

  final CustomerDetail detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final consent = detail.consent;
    final tone = consentColor(consent.status, scheme);

    // Whether the boutique may make contact, and since when. The state is what
    // the floor decides on, and the module prints both in one sentence.
    final detailLabel = consent.detailLabel;

    return Container(
      // Reading only: the API records consent from the client's own channel, so
      // the floor has no control for it here.
      key: const Key('customer_consent'),
      padding: const EdgeInsets.fromLTRB(12, 10, 14, 10),
      decoration: BoxDecoration(
        color: tone.withValues(alpha: 0.07),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(
        children: [
          Icon(consentIcon(consent.status), size: 16, color: tone),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Consent · ${consent.statusLabel}',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: tone,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 0.2,
                  ),
                ),
                const SizedBox(height: 2),
                // The module's own sentence for the state, whole: "Granted 3
                // Aug 2026" is the reading an associate checks, and splitting it
                // into a state and a date would have the label and the date
                // drift apart the moment either wording changed.
                Text(
                  detailLabel,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// The three figures an associate reads first, as a ledger on hairlines rather
/// than as a boxed-up table: the paper already says they belong together.
class _Ledger extends StatelessWidget {
  const _Ledger({required this.detail, required this.now});

  final CustomerDetail detail;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final customer = detail.customer;
    final lastVisit = customer.lastVisitAtUtc;

    Widget divider() => Container(
      width: 1,
      height: 40,
      margin: const EdgeInsets.symmetric(horizontal: 6),
      color: scheme.outlineVariant.withValues(alpha: 0.45),
    );

    return Padding(
      key: const Key('customer_metrics'),
      padding: const EdgeInsets.fromLTRB(10, 16, 10, 18),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: _Metric(
              metricKey: const Key('customer_metric_spend'),
              value: customer.totalSpentLabel,
              label: 'Spent with us',
            ),
          ),
          divider(),
          Expanded(
            child: _Metric(
              metricKey: const Key('customer_metric_visits'),
              value: '${customer.visitCount}',
              label: customer.visitCount == 1 ? 'Visit' : 'Visits',
            ),
          ),
          divider(),
          Expanded(
            child: _Metric(
              metricKey: const Key('customer_metric_last-visit'),
              value: lastVisit == null
                  ? 'Never'
                  : relativeDay(lastVisit, now: now),
              label: lastVisit == null ? 'No visits yet' : shortDate(lastVisit),
            ),
          ),
        ],
      ),
    );
  }
}

/// One figure and what it counts. The meaning leads: an associate reads "visits"
/// and then the number, which is the order they think in at the counter.
class _Metric extends StatelessWidget {
  const _Metric({
    required this.metricKey,
    required this.value,
    required this.label,
  });

  final Key metricKey;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      key: metricKey,
      padding: const EdgeInsets.symmetric(horizontal: 5),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label.toUpperCase(),
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              letterSpacing: 0.8,
              fontSize: 9.5,
              fontWeight: FontWeight.w600,
              height: 1.25,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.headlineSmall?.copyWith(
              color: scheme.onSurface,
              height: 1.1,
            ),
          ),
        ],
      ),
    );
  }
}

/// The next occasion, on its own band.
///
/// It is the only thing on the page carrying a deadline, so it is the only block
/// filled with the brand wine: everything else is read at leisure, and this is
/// read against the clock. The leaf holds the day and the band states how long
/// there is, which is the reading the floor actually works from.
class _OccasionBand extends StatelessWidget {
  const _OccasionBand({required this.event, required this.now});

  final CustomerEvent event;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final date = event.dateUtc.toLocal();

    return Container(
      key: ValueKey('customer_occasion_${event.id}'),
      padding: const EdgeInsets.fromLTRB(14, 14, 16, 14),
      decoration: BoxDecoration(
        color: theme.colorScheme.primary,
        borderRadius: BorderRadius.circular(18),
        boxShadow: [
          BoxShadow(
            color: theme.colorScheme.primary.withValues(alpha: 0.22),
            blurRadius: 20,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Row(
        children: [
          // A calendar leaf: the date is the point of the band, and a leaf reads
          // as a date at a glance where another line of text would not.
          Container(
            width: 52,
            padding: const EdgeInsets.symmetric(vertical: 8),
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.16),
              borderRadius: BorderRadius.circular(13),
            ),
            child: Column(
              children: [
                Text(
                  '${date.day}',
                  style: theme.textTheme.headlineSmall?.copyWith(
                    color: Colors.white,
                    height: 1,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  monthAbbreviation(event.dateUtc).toUpperCase(),
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: Colors.white.withValues(alpha: 0.85),
                    fontSize: 9.5,
                    letterSpacing: 1.2,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Icon(occasionIcon(event.type), size: 15, color: Colors.white),
                    const SizedBox(width: 6),
                    Expanded(
                      child: Text(
                        event.typeLabel,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.titleMedium?.copyWith(
                          color: Colors.white,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  event.description ?? 'Ahead for this client.',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: Colors.white.withValues(alpha: 0.86),
                    height: 1.35,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 12),
          Text(
            event.countdownLabel(now: now),
            textAlign: TextAlign.right,
            style: theme.textTheme.headlineSmall?.copyWith(
              color: Colors.white,
              fontSize: 18,
              height: 1.1,
            ),
          ),
        ],
      ),
    );
  }
}

/// How to reach the client, and whether the boutique may.
class _ContactCard extends StatelessWidget {
  const _ContactCard({required this.detail});

  final CustomerDetail detail;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final customer = detail.customer;

    return _Panel(
      padding: EdgeInsets.zero,
      children: [
        _ContactRow(
          rowKey: const Key('customer_contact_phone'),
          icon: Icons.call_outlined,
          label: 'Phone',
          value: customer.phoneNumber,
        ),
        Divider(
          height: 1,
          thickness: 1,
          indent: 62,
          color: scheme.outlineVariant.withValues(alpha: 0.4),
        ),
        _ContactRow(
          rowKey: const Key('customer_contact_email'),
          icon: Icons.mail_outline_rounded,
          label: 'Email',
          value: customer.emailLabel,
          isLast: true,
        ),
      ],
    );
  }
}

/// One way to reach the client, on a line of its own.
///
/// A full-width row rather than half a two-up grid: an address is longer than a
/// phone number, and at 16px on a narrow phone the tile had to break it in the
/// middle of a word.
class _ContactRow extends StatelessWidget {
  const _ContactRow({
    required this.rowKey,
    required this.icon,
    required this.label,
    required this.value,
    this.isLast = false,
  });

  final Key rowKey;
  final IconData icon;
  final String label;
  final String value;
  final bool isLast;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      key: rowKey,
      padding: EdgeInsets.fromLTRB(14, 13, 16, isLast ? 14 : 13),
      child: Row(
        children: [
          Container(
            width: 32,
            height: 32,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: scheme.primary.withValues(alpha: 0.08),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(icon, size: 16, color: scheme.primary),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label.toUpperCase(),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    letterSpacing: 1,
                    fontSize: 9.5,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  value,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: scheme.onSurface,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// What the boutique knows about a client's taste, as tiles that carry how the
/// boutique came to know it.
class _PreferenceGrid extends StatelessWidget {
  const _PreferenceGrid({required this.preferences});

  final List<CustomerPreference> preferences;

  @override
  Widget build(BuildContext context) {
    if (preferences.isEmpty) {
      return const _EmptyNote('Nothing recorded about their taste yet.');
    }

    return LayoutBuilder(
      key: const Key('customer_preferences'),
      builder: (context, constraints) {
        // One tile to a row would make a four-line stack of two-word answers.
        // The grid stays two-up because a preference is short by nature.
        final columns = constraints.maxWidth < 340 ? 1 : 2;
        final tileWidth = columns == 1
            ? constraints.maxWidth
            : (constraints.maxWidth - 10) / 2;

        return Wrap(
          spacing: 10,
          runSpacing: 10,
          children: [
            for (final preference in preferences)
              SizedBox(
                width: tileWidth,
                child: _PreferenceTile(preference: preference),
              ),
          ],
        );
      },
    );
  }
}

class _PreferenceTile extends StatelessWidget {
  const _PreferenceTile({required this.preference});

  final CustomerPreference preference;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final tone = confidenceColor(preference.confidence, scheme);

    return Container(
      key: ValueKey('customer_preference_${preference.id}'),
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 13),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.35)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            preference.key,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              letterSpacing: 0.4,
            ),
          ),
          const SizedBox(height: 5),
          Text(
            preference.value,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.titleMedium?.copyWith(
              color: scheme.onSurface,
            ),
          ),
          const SizedBox(height: 11),
          // How much to trust it, as length and word together: a stated
          // preference fills the rail in the brand accent, a guess barely
          // reaches across it.
          Row(
            children: [
              Expanded(
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(999),
                  child: LinearProgressIndicator(
                    value: preference.confidence,
                    minHeight: 2,
                    backgroundColor: scheme.outlineVariant.withValues(
                      alpha: 0.45,
                    ),
                    valueColor: AlwaysStoppedAnimation<Color>(tone),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Text(
                '${preference.isExplicit ? 'Stated' : 'Inferred'} '
                '${preference.confidenceLabel}',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: tone,
                  fontSize: 9.5,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// The shop's own vocabulary for this client, as pills and nothing else: no card
/// around them, so the page does not become a column of identical surfaces.
class _TagRow extends StatelessWidget {
  const _TagRow({required this.tags});

  final Set<String> tags;

  @override
  Widget build(BuildContext context) {
    if (tags.isEmpty) {
      return const _EmptyNote('No tags on this client yet.');
    }

    return Wrap(
      key: const Key('customer_tags'),
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final tag in tags)
          FilterPill(
            key: ValueKey('customer_tag_$tag'),
            // Read-only: the pill is the shop's own vocabulary, and nothing on
            // this screen edits it.
            selected: false,
            label: tag.replaceAll('-', ' '),
          ),
      ],
    );
  }
}

/// What Aveline remembers: the one place on this page where the boutique speaks
/// rather than records.
///
/// It is the only surface tinted with the brand accent and the only prose set in
/// the serif, because a memory is Aveline's own sentence about a client rather
/// than another field of her record. The attribution under each one is set like
/// a citation — who said it, where it came from, how sure the boutique is, and
/// when — so the reading order is the order an associate judges it in.
class _MemoryPanel extends StatelessWidget {
  const _MemoryPanel({required this.memories});

  final List<CustomerMemory> memories;

  @override
  Widget build(BuildContext context) {
    if (memories.isEmpty) {
      return const _EmptyNote('Nothing remembered about this client yet.');
    }

    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('customer_memories'),
      padding: const EdgeInsets.fromLTRB(16, 18, 16, 20),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.045),
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.10)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (var i = 0; i < memories.length; i++) ...[
            if (i > 0) ...[
              Divider(
                height: 1,
                thickness: 1,
                color: scheme.primary.withValues(alpha: 0.12),
              ),
              const SizedBox(height: 16),
            ],
            _MemoryEntry(memory: memories[i]),
          ],
        ],
      ),
    );
  }
}

class _MemoryEntry extends StatelessWidget {
  const _MemoryEntry({required this.memory});

  final CustomerMemory memory;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final tone = memoryCategoryColor(memory.category, scheme);

    return Row(
      key: ValueKey('customer_memory_${memory.id}'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // An opening quote in the category's colour, at the size the serif is
        // drawn for: the memory is a sentence someone said.
        Text(
          '“',
          style: theme.textTheme.displaySmall?.copyWith(
            color: tone,
            height: 1,
          ),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(memory.content, style: theme.textTheme.bodyLarge),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 6,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  _Pill(
                    label: memory.categoryLabel,
                    colour: tone,
                    icon: memoryCategoryIcon(memory.category),
                  ),
                  Text(
                    '${memory.originLabel} · ${memory.sourceLabel} · '
                    '${memory.confidenceLabel}',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: scheme.onSurfaceVariant,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(
                memory.createdLabel,
                style: theme.textTheme.labelSmall?.copyWith(
                  color: scheme.onSurfaceVariant.withValues(alpha: 0.75),
                  fontWeight: FontWeight.w400,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The exchanges, on a rail: a client's history is a sequence, not a list of
/// records, and the rail is what says so.
///
/// It sits on the page's own surface rather than in a card of its own, so the
/// page does not end in a column of identical boxes and the rail has somewhere
/// to run.
class _ActivityRail extends StatelessWidget {
  const _ActivityRail({required this.interactions, required this.now});

  final List<CustomerInteraction> interactions;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    if (interactions.isEmpty) {
      return const _EmptyNote('No exchanges recorded with this client yet.');
    }

    return Column(
      key: const Key('customer_activity'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (var i = 0; i < interactions.length; i++)
          _ActivityRow(
            interaction: interactions[i],
            now: now,
            isLast: i == interactions.length - 1,
          ),
      ],
    );
  }
}

class _ActivityRow extends StatelessWidget {
  const _ActivityRow({
    required this.interaction,
    required this.now,
    required this.isLast,
  });

  final CustomerInteraction interaction;
  final DateTime now;
  final bool isLast;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final tone = channelColor(interaction.channel, scheme);

    return IntrinsicHeight(
      child: Row(
        key: ValueKey('customer_interaction_${interaction.id}'),
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // The rail: the channel's own mark, joined down to the next exchange.
          Column(
            children: [
              Container(
                width: 30,
                height: 30,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: tone.withValues(alpha: 0.12),
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  channelIcon(interaction.channel),
                  size: 15,
                  color: tone,
                ),
              ),
              if (!isLast)
                Expanded(
                  child: Container(
                    width: 1.5,
                    margin: const EdgeInsets.symmetric(vertical: 5),
                    color: scheme.outlineVariant.withValues(alpha: 0.7),
                  ),
                ),
            ],
          ),
          const SizedBox(width: 13),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(bottom: isLast ? 4 : 20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(
                        interaction.isInbound
                            ? Icons.south_west_rounded
                            : Icons.north_east_rounded,
                        size: 13,
                        color: interaction.isInbound
                            ? scheme.primary
                            : scheme.onSurfaceVariant,
                      ),
                      const SizedBox(width: 5),
                      Expanded(
                        child: Text(
                          '${interaction.directionLabel} · '
                          '${interaction.channelLabel}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        interaction.whenLabel(now: now),
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: scheme.onSurfaceVariant.withValues(alpha: 0.8),
                          fontWeight: FontWeight.w400,
                        ),
                      ),
                    ],
                  ),
                  if (interaction.messageContent != null) ...[
                    const SizedBox(height: 4),
                    Text(
                      interaction.messageContent!,
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Occasions that are no longer ahead, kept quiet: they are context, not work.
///
/// Each is a dot on a hairline grid, dated in month and year rather than in days
/// ago — a past occasion is read as history, not as elapsed time.
class _PastOccasions extends StatelessWidget {
  const _PastOccasions({required this.events, required this.now});

  final List<CustomerEvent> events;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('customer_past_occasions'),
      padding: const EdgeInsets.fromLTRB(12, 6, 12, 6),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.4)),
      ),
      child: Column(
        children: [
          for (var i = 0; i < events.length; i++)
            Padding(
              key: ValueKey('customer_event_${events[i].id}'),
              padding: const EdgeInsets.symmetric(vertical: 9),
              child: Row(
                children: [
                  Container(
                    width: 6,
                    height: 6,
                    decoration: BoxDecoration(
                      color: scheme.onSurfaceVariant.withValues(alpha: 0.35),
                      shape: BoxShape.circle,
                    ),
                  ),
                  const SizedBox(width: 11),
                  Icon(
                    occasionIcon(events[i].type),
                    size: 15,
                    color: scheme.onSurfaceVariant.withValues(alpha: 0.75),
                  ),
                  const SizedBox(width: 9),
                  Expanded(
                    child: Text(
                      events[i].typeLabel,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onSurface,
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  // When it was, and how long ago, stacked rather than run
                  // together on one line: "12 Aug 2026 · 40 days ago" is wider
                  // than the row it has to fit in, and a countdown that wraps
                  // mid-phrase reads worse than one on its own line.
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        events[i].dateLabel,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: scheme.onSurfaceVariant,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        events[i].countdownLabel(now: now),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: scheme.onSurfaceVariant.withValues(alpha: 0.8),
                          fontWeight: FontWeight.w400,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// What an associate can do from the profile.
class _Actions extends StatelessWidget {
  const _Actions({required this.onLogVisit, required this.onRecomputeStatus});

  final VoidCallback onLogVisit;
  final VoidCallback onRecomputeStatus;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        // Stacked, one to a row, each with its own weight: a row of identical
        // buttons makes the associate read all of them before choosing.
        _ActionButton(
          buttonKey: const Key('customer_action_visit'),
          icon: Icons.storefront_outlined,
          label: 'Log a visit',
          tone: _ActionTone.primary,
          onPressed: onLogVisit,
        ),
        const SizedBox(height: 10),
        _ActionButton(
          buttonKey: const Key('customer_action_tier'),
          icon: Icons.auto_awesome_outlined,
          label: 'Recompute the tier',
          tone: _ActionTone.quiet,
          onPressed: onRecomputeStatus,
        ),
        const SizedBox(height: 14),
        // The module's mutation endpoints are not wired on mobile yet, so this
        // says where the change lives rather than implying the boutique's record
        // changed. Consent is not among them: the API takes it from the client's
        // own channel, never from the floor.
        Text(
          'Recorded on this screen. The concierge is not updated yet.',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.labelSmall?.copyWith(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
            fontWeight: FontWeight.w400,
          ),
        ),
      ],
    );
  }
}

/// How much weight an action carries.
enum _ActionTone { primary, quiet }

/// A full-width action, in the tone its intent deserves.
class _ActionButton extends StatelessWidget {
  const _ActionButton({
    required this.buttonKey,
    required this.icon,
    required this.label,
    required this.tone,
    required this.onPressed,
  });

  final Key buttonKey;
  final IconData icon;
  final String label;
  final _ActionTone tone;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final background = tone == _ActionTone.primary
        ? scheme.primary
        : scheme.surfaceContainerLowest;
    final foreground = tone == _ActionTone.primary
        ? scheme.onPrimary
        : scheme.primary;

    final shape = StadiumBorder(
      side: BorderSide(
        color: tone == _ActionTone.primary
            ? Colors.transparent
            : scheme.primary.withValues(alpha: 0.35),
      ),
    );

    return SizedBox(
      width: double.infinity,
      height: 52,
      child: Semantics(
        button: true,
        label: label,
        child: Material(
          color: background,
          shape: shape,
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: buttonKey,
            onTap: onPressed,
            customBorder: shape,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(icon, size: 18, color: foreground),
                const SizedBox(width: 8),
                // Flexible so a long label ellipsises inside the pill rather
                // than overflowing it at a large text scale.
                Flexible(
                  child: Text(
                    label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: foreground,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A section's opening line: the title, how much sits under it, and a hairline
/// running out to the end of the line.
///
/// The rule is what does the grouping. Seven sections that each opened with the
/// same tinted icon chip read as seven copies of one template, and a heading
/// does not need a mark to be found — the content under it does. The title is
/// set once: an overline repeating the same words above it was a second copy of
/// the heading rather than a second thing to know.
class _SectionHeading extends StatelessWidget {
  const _SectionHeading(this.title, {this.count});

  final String title;

  /// How much sits under the heading, shown only when it is worth counting.
  /// A heading that is already followed by four obvious tiles does not need to
  /// announce that there are four of them.
  final int? count;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Flexible(
            child: Text(
              title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.titleLarge?.copyWith(
                color: scheme.onSurface,
              ),
            ),
          ),
          if (count != null && count! > 0) ...[
            const SizedBox(width: 8),
            Text(
              '$count',
              style: theme.textTheme.labelSmall?.copyWith(
                color: scheme.onSurfaceVariant.withValues(alpha: 0.8),
                fontSize: 11,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
          const SizedBox(width: 12),
          // A hairline running to the end of the line, so a section reads as
          // opening rather than as another line of text.
          Expanded(
            child: Container(
              height: 1,
              color: scheme.outlineVariant.withValues(alpha: 0.5),
            ),
          ),
        ],
      ),
    );
  }
}

/// The grade the client holds: one pill, and the only mark on the page that
/// summarises her.
///
/// Tinted rather than filled, matching the value chip the book's rows carry, so
/// a client's grade reads the same wherever it appears. The label is the API's
/// own word for the grade and is never translated into a second vocabulary:
/// printing a level beside a status put two contradictory grades on one client.
class _GradePill extends StatelessWidget {
  const _GradePill({required this.status});

  final CustomerStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colour = theme.colorScheme.primary;

    return Semantics(
      label: 'Grade ${status.label}',
      child: Container(
        key: const Key('customer_grade'),
        padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 5),
        decoration: BoxDecoration(
          color: colour.withValues(alpha: 0.10),
          borderRadius: BorderRadius.circular(999),
        ),
        child: Text(
          status.label.toUpperCase(),
          style: theme.textTheme.labelSmall?.copyWith(
            color: colour,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.9,
            fontSize: 10.5,
          ),
        ),
      ),
    );
  }
}

/// A small tinted mark: a category, a kind of occasion.
class _Pill extends StatelessWidget {
  const _Pill({required this.label, required this.colour, required this.icon});

  final String label;
  final Color colour;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
      decoration: BoxDecoration(
        color: colour.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 12, color: colour),
          const SizedBox(width: 5),
          Text(
            label,
            style: theme.textTheme.labelSmall?.copyWith(
              color: colour,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.3,
            ),
          ),
        ],
      ),
    );
  }
}

/// A white card, for the sections that hold a list.
class _Panel extends StatelessWidget {
  const _Panel({
    required this.children,
    this.padding = const EdgeInsets.all(16),
  });

  final List<Widget> children;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Container(
      padding: padding,
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(18),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
            blurRadius: 20,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: children,
      ),
    );
  }
}

/// The line a section shows when the module has nothing on file.
class _EmptyNote extends StatelessWidget {
  const _EmptyNote(this.text);

  final String text;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Row(
      children: [
        Icon(
          Icons.remove_circle_outline,
          size: 15,
          color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            text,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ),
      ],
    );
  }
}
