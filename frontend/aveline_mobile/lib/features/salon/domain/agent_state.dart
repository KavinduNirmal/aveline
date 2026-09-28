/// Aveline's current agentic-workflow state, reflected by the blossom avatar.
///
/// Mirrors the web `AvelineState` union and the backend `AgentStateDto` string values.
/// Received over SignalR via `ReceiveAgentState`.
enum AgentState {
  idle('idle'),
  thinking('thinking'),
  searching('searching'),
  processing('processing'),
  toolCall('tool_call'),
  waiting('waiting'),
  success('success'),
  error('error'),
  response('response');

  const AgentState(this.wireValue);

  /// The canonical wire value shared with the API and web client.
  final String wireValue;

  /// Parses a wire value into an [AgentState], defaulting to [AgentState.idle].
  static AgentState fromWire(String? value) {
    for (final state in AgentState.values) {
      if (state.wireValue == value) return state;
    }
    return AgentState.idle;
  }

  /// True for states that mean the workflow run has finished.
  ///
  /// Mirrors the web `isTerminalState` helper. A terminal state ends the live activity bubble
  /// rather than opening one, so a late terminal event cannot leave a stale "Done"/"Something
  /// went wrong" card hanging in the thread.
  bool get isTerminal =>
      this == AgentState.success ||
      this == AgentState.error ||
      this == AgentState.response;

  /// True for states that mean the run has stopped, whether or not it finished its work.
  ///
  /// [waiting] is the agent service's terminal state for a run that paused for an owner
  /// decision (ADR-024): `agents.py` publishes it in place of `success` when the run status is
  /// `PausedForApproval`. It is not a *finished* state — the header should keep reading
  /// "Awaiting your decision…" until the decision is made — but it is the end of the run, so
  /// the live activity bubble has to collapse. Treating it as in-flight left a spinner in the
  /// thread for as long as the approval was outstanding, which is indefinitely.
  bool get endsRun => isTerminal || this == AgentState.waiting;
}
