import 'package:aveline_mobile/features/salon/domain/agent_activity.dart';
import 'package:aveline_mobile/features/salon/domain/agent_state.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final startedAt = DateTime(2026, 9, 13, 19, 29, 15);

  AgentActivity running(AgentState state) =>
      AgentActivity(startedAt: startedAt, currentState: state);

  group('nextAgentActivity', () {
    test('ignores an in-progress state when no run is in flight', () {
      // A state for a run this screen is not tracking must not open a bubble that only
      // another agent reply could close.
      for (final state in [
        AgentState.thinking,
        AgentState.searching,
        AgentState.processing,
        AgentState.toolCall,
        AgentState.waiting,
      ]) {
        expect(nextAgentActivity(null, state), isNull, reason: '$state');
      }
    });

    test('tracks in-progress states while a run is in flight', () {
      final thinking = running(AgentState.thinking);

      final searching = nextAgentActivity(thinking, AgentState.searching);
      expect(searching, isNotNull);
      expect(searching!.currentState, AgentState.searching);
      // The run's start time is preserved so "Thought for Xs" measures the whole run.
      expect(searching.startedAt, startedAt);

      final processing = nextAgentActivity(searching, AgentState.processing);
      expect(processing!.currentState, AgentState.processing);
      expect(processing.startedAt, startedAt);
    });

    test('collapses the bubble on every terminal state', () {
      for (final state in [
        AgentState.success,
        AgentState.error,
        AgentState.response,
      ]) {
        expect(
          nextAgentActivity(running(AgentState.processing), state),
          isNull,
          reason: '$state should end the run',
        );
      }
    });

    test('stays collapsed when a terminal state arrives twice', () {
      final collapsed = nextAgentActivity(running(AgentState.thinking), AgentState.success);
      expect(collapsed, isNull);
      expect(nextAgentActivity(collapsed, AgentState.success), isNull);
    });

    test('a late in-progress state does not resurrect a finished run', () {
      // The regression: the reply collapses the bubble, then agent.status=thinking (published
      // before the reply, but delivered after it) reopened it and nothing ever closed it.
      final afterReply = nextAgentActivity(running(AgentState.thinking), AgentState.success);
      expect(afterReply, isNull);

      expect(nextAgentActivity(afterReply, AgentState.thinking), isNull);
    });
  });

  group('AgentState.isTerminal', () {
    test('is true only for states that finish the run', () {
      expect(AgentState.success.isTerminal, isTrue);
      expect(AgentState.error.isTerminal, isTrue);
      expect(AgentState.response.isTerminal, isTrue);

      for (final state in [
        AgentState.idle,
        AgentState.thinking,
        AgentState.searching,
        AgentState.processing,
        AgentState.toolCall,
        AgentState.waiting,
      ]) {
        expect(state.isTerminal, isFalse, reason: '$state');
      }
    });
  });
}
