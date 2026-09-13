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
}
