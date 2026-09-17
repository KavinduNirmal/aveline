import 'package:flutter/material.dart';

import '../../../../core/navigation/staff_screens.dart';
import '../../../../shared/widgets/animated_blossom.dart';
import '../../../../shared/widgets/aveline_drawer.dart';
import '../../../../shared/widgets/aveline_header.dart';
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
            Positioned.fill(child: widget.child),
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
