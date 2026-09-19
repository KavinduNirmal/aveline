import 'dart:async';

import 'package:flutter/foundation.dart';

import '../../features/salon/domain/agent_state.dart';

/// Holds Aveline's current agentic-workflow state for the open Salon and drives the
/// animated avatar. Transient states (success/error/response) settle back to idle after a
/// short delay.
class AgentStateProvider extends ChangeNotifier {
  AgentStateProvider({this.transientTimeout = const Duration(milliseconds: 2500)});

  /// How long to hold a transient state before settling to idle.
  final Duration transientTimeout;

  AgentState _state = AgentState.idle;
  String? _agentKey;
  Timer? _transientTimer;

  AgentState get state => _state;

  /// The persona the current state was attributed to, when the payload named one.
  String? get agentKey => _agentKey;

  /// True while Aveline is actively working on a reply.
  bool get isWorking => switch (_state) {
        AgentState.thinking ||
        AgentState.searching ||
        AgentState.processing ||
        AgentState.toolCall =>
          true,
        _ => false,
      };

  /// Applies an incoming agent state, scheduling a settle-to-idle for transient states.
  ///
  /// [agentKey] is the persona the state was attributed to, so the open thread can credit
  /// the specialist that is working rather than the umbrella brand.
  void apply(AgentState state, {String? agentKey}) {
    _transientTimer?.cancel();
    _transientTimer = null;

    _state = state;
    _agentKey = agentKey;
    notifyListeners();

    if (state == AgentState.success ||
        state == AgentState.error ||
        state == AgentState.response) {
      _transientTimer = Timer(transientTimeout, () {
        _state = AgentState.idle;
        notifyListeners();
      });
    }
  }

  /// Resets to idle (e.g. when the Salon closes).
  void reset() {
    _transientTimer?.cancel();
    _transientTimer = null;
    _state = AgentState.idle;
    _agentKey = null;
    notifyListeners();
  }

  @override
  void dispose() {
    _transientTimer?.cancel();
    super.dispose();
  }
}
