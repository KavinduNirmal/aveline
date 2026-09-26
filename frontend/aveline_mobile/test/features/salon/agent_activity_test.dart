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

    test('collapses the bubble when a run pauses for a decision', () {
      // The agent service publishes `waiting` as the *terminal* state of a run that
      // stopped for an owner decision (ADR-024), in place of `success`. Treating it as
      // in-flight left the spinner in the thread for as long as the approval was
      // outstanding, which is indefinitely.
      expect(
        nextAgentActivity(running(AgentState.processing), AgentState.waiting),
        isNull,
      );
      // And a `waiting` for a run this screen is not tracking opens nothing either.
      expect(nextAgentActivity(null, AgentState.waiting), isNull);
    });

    test('a late in-progress state does not resurrect a finished run', () {
      // The regression: the reply collapses the bubble, then agent.status=thinking (published
      // before the reply, but delivered after it) reopened it and nothing ever closed it.
      final afterReply = nextAgentActivity(running(AgentState.thinking), AgentState.success);
      expect(afterReply, isNull);

      expect(nextAgentActivity(afterReply, AgentState.thinking), isNull);
    });
  });

  group('AgentState.endsRun', () {
    test('counts a paused run as stopped without calling it finished', () {
      // `waiting` ends the activity bubble, but it must stay non-terminal: the header
      // has to keep reading "Awaiting your decision…" until the decision is made.
      expect(AgentState.waiting.endsRun, isTrue);
      expect(AgentState.waiting.isTerminal, isFalse);

      for (final state in [
        AgentState.success,
        AgentState.error,
        AgentState.response,
      ]) {
        expect(state.endsRun, isTrue, reason: '$state');
      }
      for (final state in [
        AgentState.idle,
        AgentState.thinking,
        AgentState.searching,
        AgentState.processing,
        AgentState.toolCall,
      ]) {
        expect(state.endsRun, isFalse, reason: '$state');
      }
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
