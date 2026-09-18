import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../core/navigation/staff_screens.dart';
import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/providers/user_provider.dart';
import '../../../../shared/widgets/animated_blossom.dart';
import '../../../../shared/widgets/aveline_drawer.dart';
import '../../../../shared/widgets/aveline_header.dart';
import '../../../../shared/widgets/blossom_refresh.dart';
import '../../../../shared/widgets/quit_confirmation_dialog.dart';

/// Primary UI shell scaffold for the Staff application.
///
/// Combines the universal [AvelineHeader] (which can be hidden by passing
/// `showHeader = false`), the [AvelineDrawer] side panel populated with staff
/// screens, the content [child], and the floating [AnimatedBlossom] centerpiece.
///
/// Pressing the Android back button with the drawer closed on the root screen
/// asks for confirmation before the app closes, because the drawer's own edge
/// swipe sits in the same region of the screen as the system's back gesture.
class StaffAppShell extends StatefulWidget {
  const StaffAppShell({
    super.key,
    required this.child,
    this.showHeader = true,
    this.showBlossom = true,
    this.isAtRoot = true,
  });

  /// Width of the strip along the left edge that opens the drawer on a drag.
  ///
  /// Flutter's default is 20 logical pixels, which is narrower than the system
  /// back gesture region: an edge swipe started a little inside the bezel used
  /// to be claimed by Android and popped the route instead of opening the
  /// drawer. Widening the strip makes the drawer win those drags. The trade-off
  /// is intentional and documented rather than incidental, so the value lives
  /// here instead of being inlined into the [Scaffold].
  static const double drawerEdgeDragWidth = 48;

  /// The active screen content.
  final Widget child;

  /// Whether the universal header is displayed at the top.
  /// Set to `false` for screens that require a custom or full-screen layout.
  final bool showHeader;

  /// Whether the floating animated Blossom brand mark is displayed at the bottom.
  final bool showBlossom;

  /// Whether this shell is showing the bottom of the navigation stack.
  ///
  /// When `false`, a back press belongs to the router and must not be
  /// intercepted by the quit confirmation.
  final bool isAtRoot;

  @override
  State<StaffAppShell> createState() => _StaffAppShellState();
}

class _StaffAppShellState extends State<StaffAppShell> {
  /// Guards against stacking a second confirmation while the first is open.
  bool _quitDialogOpen = false;

  /// Handles an Android system back press.
  ///
  /// The drawer closes first: when it is open, a back press dismisses the panel
  /// instead of reaching the quit confirmation.
  Future<void> _handleSystemBack(bool didPop, Object? result) async {
    if (didPop) {
      return;
    }

    final scaffold = Scaffold.maybeOf(context);
    if (scaffold?.isDrawerOpen ?? false) {
      Navigator.of(context).pop();
      return;
    }

    // Mid-stack screens have already been popped by the framework, because
    // `canPop` is true for them. This guard only keeps a deeper screen that
    // somehow reaches here from being asked to quit the app.
    if (!widget.isAtRoot) {
      return;
    }

    if (_quitDialogOpen) {
      return;
    }

    setState(() => _quitDialogOpen = true);
    try {
      final confirmed = await showQuitConfirmationDialog(context);
      if (confirmed) {
        await quitAveline();
      }
    } finally {
      if (mounted) {
        setState(() => _quitDialogOpen = false);
      }
    }
  }

  /// Reads a provider if one is above this shell.
  ///
  /// The shell is mounted directly by widget tests that supply no providers, so a
  /// missing one has to degrade rather than throw.
  T? _providerOrNull<T extends Object>() {
    try {
      return context.read<T>();
    } catch (_) {
      return null;
    }
  }

  /// What a pull-to-refresh re-fetches.
  ///
  /// The account and the boutique are the only state under the shell that comes
  /// from the API; the floor's own screens are seeded from demo data, and they
  /// re-seed themselves off the [BlossomRefresh] revision.
  Future<void> _refresh() async {
    final dio = _providerOrNull<Dio>();
    if (dio == null) {
      return;
    }

    final user = _providerOrNull<UserProvider>();
    if (user != null) {
      await user.fetchUser(dio);
    }

    // Not `else`: the boutique load is independent of the profile, and a
    // profile that failed to load must not leave the shop name stale too.
    final boutique = _providerOrNull<BoutiqueProvider>();
    if (boutique != null) {
      await boutique.fetchBoutique(dio);
    }
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      // Deeper screens pop normally, so the framework performs the back
      // gesture and Android predictive back keeps working. Only the root screen
      // withholds the pop, because it has to ask before leaving the app.
      canPop: !widget.isAtRoot,
      onPopInvokedWithResult: _handleSystemBack,
      child: Scaffold(
        appBar: widget.showHeader ? const AvelineHeader() : null,
        drawer: AvelineDrawer(screens: staffScreens()),
        drawerEdgeDragWidth: StaffAppShell.drawerEdgeDragWidth,
        body: Stack(
          children: [
            // One wrapper for every screen the shell hosts, so the gesture is the
            // same on all of them.
            Positioned.fill(
              child: BlossomRefresh(
                onRefresh: _refresh,
                child: widget.child,
              ),
            ),
            if (widget.showBlossom)
              const Align(
                alignment: Alignment.bottomCenter,
                child: SafeArea(
                  top: false,
                  child: Padding(
                    padding: EdgeInsets.only(bottom: 24),
                    child: AnimatedBlossom(),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
