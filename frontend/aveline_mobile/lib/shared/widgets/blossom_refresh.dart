import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'blossom.dart';

/// The revision the refresh gesture has reached.
///
/// A pull re-fetches what the API owns, but screens like Home keep their own copy
/// of the data they were seeded with. They read this and re-seed when it changes,
/// which is what makes a global pull reload the screen rather than only the
/// profile behind it.
class RefreshScope extends InheritedWidget {
  const RefreshScope({super.key, required this.revision, required super.child});

  /// Bumped once per completed refresh.
  final int revision;

  /// The current revision, or `0` when there is no refresh gesture above this
  /// widget — which is how a screen mounted on its own in a test behaves.
  static int revisionOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<RefreshScope>()?.revision ?? 0;

  @override
  bool updateShouldNotify(RefreshScope oldWidget) =>
      oldWidget.revision != revision;
}

/// Pull-to-refresh, wearing the brand mark.
///
/// The gesture is [RefreshIndicator]'s: it already owns the arming threshold, the
/// platform's overscroll and the release handling, and all of that is easy to get
/// subtly wrong by hand. `RefreshIndicator.noSpinner` drops its spinner and
/// reports status changes instead, so the blossom can do the visible work — it
/// unfurls as the pull arms and turns while the refresh runs.
///
/// It also publishes a [RefreshScope], so screens that hold their own copy of the
/// data can re-seed when the pull completes.
class BlossomRefresh extends StatefulWidget {
  const BlossomRefresh({super.key, this.onRefresh, required this.child});

  /// The work a pull performs.
  ///
  /// Optional: with nothing wired the mark still unfurls, which keeps the gesture
  /// honest on screens whose slice has no endpoint behind it yet.
  final Future<void> Function()? onRefresh;

  final Widget child;

  /// The shortest a refresh is allowed to take.
  ///
  /// A refresh that returns in a few milliseconds would flash the mark and read
  /// as a glitch rather than as work. Real fetches exceed this on their own; the
  /// floor only matters for the demo data.
  static const Duration floor = Duration(milliseconds: 450);

  @override
  State<BlossomRefresh> createState() => _BlossomRefreshState();
}

class _BlossomRefreshState extends State<BlossomRefresh>
    with TickerProviderStateMixin {
  /// `0` hidden, `1` fully unfurled.
  late final AnimationController _unfurl = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 240),
  );

  /// Turns the mark while the refresh is in flight.
  late final AnimationController _turn = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 2),
  );

  bool _visible = false;

  int _revision = 0;

  @override
  void dispose() {
    _unfurl.dispose();
    _turn.dispose();
    super.dispose();
  }

  /// Ambient motion is decoration, so under reduced motion the mark appears and
  /// disappears without unfurling or turning.
  bool get _reduceMotion =>
      mounted && MediaQuery.disableAnimationsOf(context);

  Duration _over(Duration duration) =>
      _reduceMotion ? Duration.zero : duration;

  void _handleStatus(RefreshIndicatorStatus? status) {
    switch (status) {
      case RefreshIndicatorStatus.drag:
        _reveal(0.45);
      case RefreshIndicatorStatus.armed:
        _reveal(1);
      case RefreshIndicatorStatus.snap:
      case RefreshIndicatorStatus.refresh:
        _reveal(1);
        if (!_reduceMotion) {
          _turn.repeat();
        }
      case RefreshIndicatorStatus.done:
      case RefreshIndicatorStatus.canceled:
      case null:
        _retract();
    }
  }

  void _reveal(double unfurl) {
    if (!_visible) {
      setState(() => _visible = true);
    }
    _unfurl.animateTo(unfurl, duration: _over(const Duration(milliseconds: 240)));
  }

  void _retract() {
    _turn.stop();
    _unfurl
        .animateBack(0, duration: _over(const Duration(milliseconds: 200)))
        .whenComplete(() {
      if (mounted && _unfurl.value == 0) {
        setState(() => _visible = false);
      }
    });
  }

  Future<void> _refresh() async {
    try {
      await Future.wait([
        if (widget.onRefresh != null) widget.onRefresh!(),
        Future<void>.delayed(BlossomRefresh.floor),
      ]);
    } catch (_) {
      // A failed refresh leaves the screen as it was. The provider that failed
      // already carries the error state, so swallowing it here keeps the pull
      // from turning a network problem into a crash.
    } finally {
      if (mounted) {
        setState(() => _revision++);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return RefreshScope(
      revision: _revision,
      child: RefreshIndicator.noSpinner(
        onRefresh: _refresh,
        onStatusChange: _handleStatus,
        semanticsLabel: 'Refreshing',
        child: Stack(
          children: [
            widget.child,
            if (_visible)
              Positioned(
                top: 0,
                left: 0,
                right: 0,
                child: IgnorePointer(
                  child: Center(child: _Mark(unfurl: _unfurl, turn: _turn)),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// The unfurling mark: the brand blossom in a small disc, so it stays legible
/// over the content it appears on top of.
class _Mark extends StatelessWidget {
  const _Mark({required this.unfurl, required this.turn});

  final Animation<double> unfurl;
  final Animation<double> turn;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return AnimatedBuilder(
      animation: Listenable.merge([unfurl, turn]),
      builder: (context, _) {
        final progress = unfurl.value.clamp(0.0, 1.0);
        // Overshoots slightly, which is what makes it read as unfurling rather
        // than as a disc fading in.
        final scale = 0.55 + Curves.easeOutBack.transform(progress) * 0.45;

        return Opacity(
          opacity: progress,
          child: Transform.rotate(
            angle: turn.value * 2 * math.pi,
            child: Transform.scale(
              scale: scale,
              child: Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: scheme.surfaceContainerLowest,
                  boxShadow: [
                    BoxShadow(
                      color: const Color(0xFF8B2E42).withValues(alpha: 0.16),
                      blurRadius: 12,
                      offset: const Offset(0, 3),
                    ),
                  ],
                ),
                child: Center(child: Blossom(size: 19, color: scheme.primary)),
              ),
            ),
          ),
        );
      },
    );
  }
}
