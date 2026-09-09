import 'package:aveline_mobile/core/providers/agent_state_provider.dart';
import 'package:aveline_mobile/features/salon/domain/agent_state.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AgentState', () {
    test('parses wire values to enum members', () {
      expect(AgentState.fromWire('idle'), AgentState.idle);
      expect(AgentState.fromWire('thinking'), AgentState.thinking);
      expect(AgentState.fromWire('searching'), AgentState.searching);
      expect(AgentState.fromWire('processing'), AgentState.processing);
      expect(AgentState.fromWire('tool_call'), AgentState.toolCall);
      expect(AgentState.fromWire('waiting'), AgentState.waiting);
      expect(AgentState.fromWire('success'), AgentState.success);
      expect(AgentState.fromWire('error'), AgentState.error);
      expect(AgentState.fromWire('response'), AgentState.response);
    });

    test('defaults to idle for an unknown wire value', () {
      expect(AgentState.fromWire('bogus'), AgentState.idle);
      expect(AgentState.fromWire(null), AgentState.idle);
    });
  });

  group('AgentStateProvider', () {
    test('starts idle and is not working', () {
      final provider = AgentStateProvider();
      expect(provider.state, AgentState.idle);
      expect(provider.isWorking, isFalse);
    });

    test('isWorking is true for active processing states', () {
      final provider = AgentStateProvider();
      for (final state in [
        AgentState.thinking,
        AgentState.searching,
        AgentState.processing,
        AgentState.toolCall,
      ]) {
        provider.apply(state);
        expect(provider.isWorking, isTrue, reason: '$state should be working');
      }
    });

    test('isWorking is false for idle and terminal states', () {
      final provider = AgentStateProvider();
      for (final state in [
        AgentState.idle,
        AgentState.waiting,
        AgentState.success,
        AgentState.error,
        AgentState.response,
      ]) {
        provider.apply(state);
        expect(provider.isWorking, isFalse, reason: '$state should not be working');
      }
    });

    test('transient states settle back to idle after the timeout', () async {
      final provider = AgentStateProvider(
        transientTimeout: const Duration(milliseconds: 100),
      );
      provider.apply(AgentState.success);
      expect(provider.state, AgentState.success);

      await Future<void>.delayed(const Duration(milliseconds: 200));
      expect(provider.state, AgentState.idle);
    });

    test('reset returns to idle', () {
      final provider = AgentStateProvider();
      provider.apply(AgentState.thinking);
      provider.reset();
      expect(provider.state, AgentState.idle);
    });
  });
}
