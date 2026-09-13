import 'agent_state.dart';

/// Aveline's in-progress reasoning for the open Salon, rendered as a live activity bubble
/// until her reply lands. Mirrors the web `agentActivity` shape.
class AgentActivity {
  const AgentActivity({required this.startedAt, required this.currentState});

  /// When the run started, used for the "Thought for Xs" caption on the reply.
  final DateTime startedAt;

  /// The most recent lifecycle state reported by the workflow.
  final AgentState currentState;

  /// A copy of this activity carrying [state], keeping [startedAt] so the thought duration
  /// still measures the whole run.
  AgentActivity withState(AgentState state) =>
      AgentActivity(startedAt: startedAt, currentState: state);
}

/// Resolves the activity bubble to show after the workflow reports [state].
///
/// Lifecycle states are published alongside the messages they describe but travel on separate
/// channels, so they are not ordered against each other. Treating every arrival as "Aveline is
/// working again" meant a state that landed after the reply reopened the bubble, and since only
/// a further reply closes it, it hung forever.
///
/// Returns:
///  * `null` for a terminal state ([AgentState.isTerminal]): the run is over, so the bubble
///    collapses, even when it had already been collapsed by the reply;
///  * [current] carrying the new state while a run is in flight, so the bubble tracks
///    thinking -> searching -> processing;
///  * `null` when no run is in flight, so a late in-progress state cannot resurrect a finished
///    run's bubble.
AgentActivity? nextAgentActivity(AgentActivity? current, AgentState state) {
  if (state.isTerminal) return null;
  return current?.withState(state);
}
