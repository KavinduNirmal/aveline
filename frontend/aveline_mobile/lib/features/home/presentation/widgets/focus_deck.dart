import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/blossom.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../domain/focus_task.dart';
import 'focus_task_card.dart';

/// `Today's focus` and its pile.
///
/// Owns the local task list so clearing a docket is a real state change on this
/// screen until the API takes over. Callers pass the demo lists from
/// `demoFocusTasks`, which are const, so a parent rebuild does not disturb the
/// pile; a freshly built list resets it.
class FocusSection extends StatefulWidget {
  const FocusSection({super.key, required this.tasks});

  final List<FocusTask> tasks;

  @override
  State<FocusSection> createState() => _FocusSectionState();
}

class _FocusSectionState extends State<FocusSection> {
  late List<FocusTask> _tasks = List.of(widget.tasks);

  @override
  void didUpdateWidget(covariant FocusSection oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!listEquals(oldWidget.tasks, widget.tasks)) {
      _tasks = List.of(widget.tasks);
    }
  }

  void _complete(FocusTask task) {
    setState(() => _tasks.removeWhere((candidate) => candidate.id == task.id));
    AppToast.show(context, task.doneMessage);
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.symmetric(horizontal: 20),
          child: SectionOverline("Today's focus"),
        ),
        const SizedBox(height: 10),
        FocusDeck(tasks: _tasks, onComplete: _complete),
      ],
    );
  }
}

/// What the pile is doing between frames.
enum _DeckMotion {
  /// At rest, or following the finger.
  rest,

  /// The front docket did not travel far enough; it is easing back.
  settle,

  /// The front docket is leaving towards the side so it can join the bottom.
  sendToBack,

  /// The front docket is being signed off and lifted off the pile.
  clear,
}

/// The focus pile: one docket in hand, the rest stacked behind it.
///
/// Swiping the top docket sideways sends it to the bottom of the pile rather
/// than off to the side, so the deck cycles: the docket behind rises to the
/// front and the work keeps its order for the next pass. The chevron button
/// performs the same motion for anyone who would rather tap.
class FocusDeck extends StatefulWidget {
  const FocusDeck({super.key, required this.tasks, required this.onComplete});

  final List<FocusTask> tasks;

  /// Called once a docket has been signed off and lifted away.
  final ValueChanged<FocusTask> onComplete;

  static const double _pageInset = 14;
  static const double _lift = 12;
  static const double _inset = 10;
  static const double _scaleStep = 0.035;
  static const double _deckLift = _lift * 2;

  /// How deep the pile is drawn. Deeper dockets would only ever show a sliver,
  /// and skipping them keeps the swiped docket from popping back into view.
  static const int _visibleLayers = 3;

  @override
  State<FocusDeck> createState() => _FocusDeckState();
}

class _FocusDeckState extends State<FocusDeck>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 340),
  )..addStatusListener(_onMotionEnd);

  /// The pile, front first. A permutation of [FocusDeck.tasks].
  late List<FocusTask> _pile = List.of(widget.tasks);

  _DeckMotion _motion = _DeckMotion.rest;

  /// How far the front docket has been dragged from its resting place.
  Offset _drag = Offset.zero;

  /// The drag offset at the moment the finger lifted.
  Offset _releaseDrag = Offset.zero;

  /// Which way a swiped docket leaves: `-1` left, `1` right.
  double _direction = 1;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  void didUpdateWidget(covariant FocusDeck oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Follow the caller's list: drop what it dropped, queue anything new at the
    // bottom of the pile.
    final ids = widget.tasks.map((task) => task.id).toSet();
    _pile.removeWhere((task) => !ids.contains(task.id));
    final known = _pile.map((task) => task.id).toSet();
    _pile.addAll(widget.tasks.where((task) => !known.contains(task.id)));
  }

  void _onMotionEnd(AnimationStatus status) {
    if (status != AnimationStatus.completed) {
      return;
    }

    final finished = _motion;
    FocusTask? cleared;

    setState(() {
      switch (finished) {
        case _DeckMotion.sendToBack:
          // The docket that left becomes the bottom of the pile.
          _pile = [..._pile.skip(1), _pile.first];
          break;
        case _DeckMotion.clear:
          cleared = _pile.first;
          _pile = _pile.skip(1).toList();
          break;
        case _DeckMotion.settle:
        case _DeckMotion.rest:
          break;
      }
      _motion = _DeckMotion.rest;
      _drag = Offset.zero;
      _releaseDrag = Offset.zero;
    });

    _controller.reset();

    if (cleared != null) {
      widget.onComplete(cleared!);
    }
  }

  void _onHorizontalDragUpdate(DragUpdateDetails details) {
    if (!_canMove) {
      return;
    }
    setState(() => _drag += Offset(details.delta.dx, 0));
  }

  void _onHorizontalDragEnd(DragEndDetails details) {
    if (!_canMove) {
      return;
    }

    final width = MediaQuery.sizeOf(context).width;
    final travelledFarEnough = _drag.dx.abs() > width * 0.22;
    final flicked = (details.primaryVelocity ?? 0).abs() > 700;

    if (travelledFarEnough || flicked) {
      _sendToBack();
      return;
    }

    setState(() {
      _releaseDrag = _drag;
      _motion = _DeckMotion.settle;
    });
    _controller.duration = const Duration(milliseconds: 240);
    _controller.forward(from: 0);
  }

  /// The pile only moves when it is settled and has somewhere to send a docket.
  bool get _canMove => _pile.length > 1 && _motion == _DeckMotion.rest;

  void _sendToBack() {
    if (!_canMove) {
      return;
    }

    if (MediaQuery.disableAnimationsOf(context)) {
      setState(() => _pile = [..._pile.skip(1), _pile.first]);
      return;
    }

    setState(() {
      _releaseDrag = _drag;
      _direction = _drag.dx.isNegative ? -1 : 1;
      _motion = _DeckMotion.sendToBack;
    });
    _controller.duration = const Duration(milliseconds: 340);
    _controller.forward(from: 0);
  }

  void _clearFront() {
    if (_pile.isEmpty || _motion != _DeckMotion.rest) {
      return;
    }

    if (MediaQuery.disableAnimationsOf(context)) {
      final cleared = _pile.first;
      setState(() => _pile = _pile.skip(1).toList());
      widget.onComplete(cleared);
      return;
    }

    setState(() => _motion = _DeckMotion.clear);
    _controller.duration = const Duration(milliseconds: 320);
    _controller.forward(from: 0);
  }

  @override
  Widget build(BuildContext context) {
    if (_pile.isEmpty) {
      return const _FocusEmptyCard();
    }

    final size = MediaQuery.sizeOf(context);
    final reduceMotion = MediaQuery.disableAnimationsOf(context);
    final ordinals = {
      for (var i = 0; i < widget.tasks.length; i++) widget.tasks[i].id: i + 1,
    };

    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        final eased = Curves.easeOutCubic.transform(_controller.value);
        final advancing =
            _motion == _DeckMotion.sendToBack || _motion == _DeckMotion.clear;
        final progress = advancing ? eased : 0.0;

        final frontOffset = switch (_motion) {
          _DeckMotion.rest => _drag,
          _DeckMotion.settle => Offset.lerp(_releaseDrag, Offset.zero, eased)!,
          _DeckMotion.sendToBack => Offset.lerp(
              _releaseDrag,
              Offset(_direction * (size.width + 160), -48),
              eased,
            )!,
          _DeckMotion.clear => Offset(0, -36 * eased),
        };

        final frontOpacity = switch (_motion) {
          _DeckMotion.sendToBack => (1 - eased * 1.7).clamp(0.0, 1.0),
          _DeckMotion.clear => (1 - eased * 1.5).clamp(0.0, 1.0),
          _ => 1.0,
        };

        final deepest = math.min(_pile.length - 1, FocusDeck._visibleLayers - 1);

        // A docket leaning into the direction it is being dragged.
        final frontRotation = frontOffset.dx / size.width * 0.12;

        // The docket in hand is laid out in the flow, so the pile is exactly as
        // tall as it is; the layers behind are positioned against it. A fixed
        // height would either clip a long docket or leave a band of empty card
        // under a short one.
        return AnimatedSize(
          duration: reduceMotion
              ? Duration.zero
              : const Duration(milliseconds: 220),
          curve: Curves.easeOutCubic,
          alignment: Alignment.topCenter,
          child: Stack(
            children: [
              // Painted back to front so the docket in hand stays on top.
              for (var index = deepest; index >= 1; index--)
                _ghostLayer(
                  index: index,
                  depth: index - progress,
                  ordinals: ordinals,
                ),
              _frontLayer(
                frontOffset: frontOffset,
                frontRotation: frontRotation,
                frontOpacity: frontOpacity,
                ordinals: ordinals,
              ),
            ],
          ),
        );
      },
    );
  }

  /// The docket in hand: laid out in the flow so it sets the pile's height.
  Widget _frontLayer({
    required Offset frontOffset,
    required double frontRotation,
    required double frontOpacity,
    required Map<String, int> ordinals,
  }) {
    final task = _pile.first;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        FocusDeck._pageInset,
        FocusDeck._deckLift,
        FocusDeck._pageInset,
        0,
      ),
      child: Transform.translate(
        offset: frontOffset,
        child: Transform.rotate(
          angle: frontRotation,
          child: Opacity(
            opacity: frontOpacity,
            child: KeyedSubtree(
              key: const Key('focus_deck_top'),
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                // Horizontal only, so a vertical drag still scrolls the Home
                // column underneath the pile.
                onHorizontalDragUpdate: _onHorizontalDragUpdate,
                onHorizontalDragEnd: _onHorizontalDragEnd,
                child: FocusTaskCard(
                  task: task,
                  ordinal: ordinals[task.id] ?? 1,
                  total: _pile.length,
                  onNext: _pile.length > 1 ? _sendToBack : null,
                  onAction: _clearFront,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// A docket behind the one in hand: plain paper, inert, matching its height.
  Widget _ghostLayer({
    required int index,
    required double depth,
    required Map<String, int> ordinals,
  }) {
    final task = _pile[index];

    return Positioned(
      top: FocusDeck._deckLift - depth * FocusDeck._lift,
      bottom: depth * FocusDeck._lift,
      left: FocusDeck._pageInset + depth * FocusDeck._inset,
      right: FocusDeck._pageInset + depth * FocusDeck._inset,
      child: Transform.scale(
        alignment: Alignment.topCenter,
        scale: 1 - depth * FocusDeck._scaleStep,
        child: IgnorePointer(
          child: ExcludeSemantics(
            child: FocusTaskCard(
              task: task,
              ordinal: ordinals[task.id] ?? index + 1,
              total: _pile.length,
              prominence: (1 - depth).clamp(0.0, 1.0),
              onNext: null,
              onAction: _clearFront,
            ),
          ),
        ),
      ),
    );
  }
}

/// Shown when the pile is empty: a clear state, not a mood.
class _FocusEmptyCard extends StatelessWidget {
  const _FocusEmptyCard();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Container(
        padding: const EdgeInsets.all(24),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Blossom(size: 28, color: scheme.primary),
            const SizedBox(height: 14),
            Text(
              'Nothing is waiting on you',
              style: theme.textTheme.headlineSmall,
            ),
            const SizedBox(height: 8),
            Text(
              'Approvals and floor tasks land here as they arrive.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
