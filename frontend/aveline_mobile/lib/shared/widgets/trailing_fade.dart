import 'package:flutter/material.dart';

/// Fades the trailing edge of a horizontally scrolling child while there is
/// still content off to the right.
///
/// A row that is wider than the screen would otherwise look like a broken
/// layout rather than a scrollable one. The fade is dropped once the child
/// reaches its end, so the last tile is never dimmed by it.
///
/// The band dims rather than erases — see [_edgeOpacity] for why — and rows take
/// their trailing padding from [endPadding] so the last item can still be scrolled
/// clear of it.
///
/// The [ShaderMask] is always in the tree and only its gradient changes. Swapping
/// the wrapper in and out would change the element type above the scroll view,
/// which unmounts and re-inflates it — and a re-inflated scroll view starts
/// again at offset zero, so the row would snap back the moment it hit its end.
class TrailingFade extends StatefulWidget {
  const TrailingFade({
    super.key,
    required this.controller,
    required this.child,
  });

  /// The controller of the scroll view inside [child].
  final ScrollController controller;

  final Widget child;

  /// The width of the dimmed band at the trailing edge, as a fraction of the
  /// viewport. Rows size their trailing padding from [endPadding] so their last
  /// item can be scrolled clear of the band.
  static const double fadeFraction = 0.10;

  /// The opacity the band leaves at the very edge.
  ///
  /// Deliberately not zero. The item peeking under the band is the row's only
  /// "there is more" affordance, and erasing it completely left the next tool as
  /// the least legible thing on the screen — the row promised more by hiding it.
  static const double _edgeOpacity = 0.65;

  /// Trailing padding for a full-bleed row, so its last item can reach the end of
  /// its scroll without sitting under the fade band.
  static EdgeInsets endPadding(BuildContext context, {double leading = 20}) =>
      EdgeInsets.only(
        left: leading,
        right: leading + fadeFraction * MediaQuery.sizeOf(context).width,
      );

  @override
  State<TrailingFade> createState() => _TrailingFadeState();
}

class _TrailingFadeState extends State<TrailingFade> {
  bool _atEnd = false;

  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_refreshEdge);
    // The position is only known once the row has been laid out, so the first
    // reading has to wait for a frame. Doing it here rather than in build keeps
    // a row that fits on screen from being faded at rest.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        _refreshEdge();
      }
    });
  }

  @override
  void didUpdateWidget(covariant TrailingFade oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller != widget.controller) {
      oldWidget.controller.removeListener(_refreshEdge);
      widget.controller.addListener(_refreshEdge);
    }
    // A rebuild is already under way, so the new value can be taken without
    // notifying: the row grows or shrinks when its content changes.
    _refreshEdge(notify: false);
  }

  @override
  void dispose() {
    widget.controller.removeListener(_refreshEdge);
    super.dispose();
  }

  void _refreshEdge({bool notify = true}) {
    if (!widget.controller.hasClients) {
      return;
    }
    final position = widget.controller.position;
    final atEnd = position.pixels >= position.maxScrollExtent - 1;
    if (atEnd == _atEnd) {
      return;
    }
    if (!notify) {
      _atEnd = atEnd;
      return;
    }
    setState(() => _atEnd = atEnd);
  }

  @override
  Widget build(BuildContext context) {
    return ShaderMask(
      shaderCallback: (bounds) => LinearGradient(
        begin: Alignment.centerLeft,
        end: Alignment.centerRight,
        colors: [
          Colors.black,
          Colors.black,
          // Black keeps the pixel, a lower alpha dims it: at the end of the row
          // the gradient stops fading anything at all.
          _atEnd
              ? Colors.black
              : Colors.black.withValues(alpha: TrailingFade._edgeOpacity),
        ],
        stops: const [0, 1 - TrailingFade.fadeFraction, 1],
      ).createShader(bounds),
      blendMode: BlendMode.dstIn,
      child: widget.child,
    );
  }
}
