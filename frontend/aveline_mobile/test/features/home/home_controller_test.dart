import 'dart:async';

import 'package:aveline_mobile/features/home/data/home_repository.dart';
import 'package:aveline_mobile/features/home/domain/blossom_usage.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:aveline_mobile/features/home/presentation/home_controller.dart';
import 'package:flutter_test/flutter_test.dart';

const _balance = BlossomUsage(used: 10, allowance: 100, renewsOn: '1 October');

const _client = ClientHighlight(
  id: 'c1',
  name: 'Eleanor Vane',
  tier: ClientTier.vip,
  activity: 'Asked for the ivory silk to be held.',
);

const _task = FocusTask(
  id: 't1',
  domain: FocusDomain.wardrobe,
  title: 'Count in the raw silk',
  detail: 'Four pieces below the reorder line.',
  timeLabel: '3:00 PM',
  actionLabel: 'Sign Off',
  doneMessage: 'Signed off.',
);

const _otherTask = FocusTask(
  id: 't2',
  domain: FocusDomain.patron,
  title: "Prepare for Mrs. Silva's fitting",
  detail: 'Tomorrow, 10:00 AM.',
  timeLabel: '4:00 PM',
  actionLabel: 'Mark ready',
  doneMessage: 'Ready.',
);

HomeSnapshot _snapshot({List<FocusTask> tasks = const [_task]}) => HomeSnapshot(
      tasks: tasks,
      clients: const [_client],
      balance: _balance,
    );

/// A stand-in source, so the controller is exercised against a contract rather
/// than against the demo constants it will one day stop using.
class _StubRepository implements HomeRepository {
  _StubRepository({this.snapshot, this.error});

  final HomeSnapshot? snapshot;
  final Object? error;

  int fetchCount = 0;
  int completeCount = 0;
  int visitCount = 0;
  String? visitedCustomerId;
  bool completeThrows = false;
  bool visitThrows = false;
  bool ownerDeckSeen = false;

  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) async {
    fetchCount++;
    ownerDeckSeen = ownerDeck;
    final failure = error;
    if (failure != null) {
      throw failure;
    }
    return snapshot ?? _snapshot();
  }

  @override
  Future<void> completeTask(FocusTask task) async {
    completeCount++;
    if (completeThrows) {
      throw StateError('the server refused');
    }
  }

  @override
  Future<ClientHighlight> createWalkIn(String fullName) async => ClientHighlight(
        id: 'server-1',
        name: fullName,
        tier: null,
        activity: 'Walk-in added at the counter.',
      );

  @override
  Future<void> recordVisit(String customerId) async {
    visitCount++;
    visitedCustomerId = customerId;
    if (visitThrows) {
      throw StateError('the visit was refused');
    }
  }
}

/// A repository whose replies are completed by the test, so a slow reply can be
/// raced against a newer one.
class _GatedRepository implements HomeRepository {
  final List<Completer<HomeSnapshot>> gates = [];

  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) {
    final gate = Completer<HomeSnapshot>();
    gates.add(gate);
    return gate.future;
  }

  @override
  Future<void> completeTask(FocusTask task) async {}

  @override
  Future<ClientHighlight> createWalkIn(String fullName) async => ClientHighlight(
        id: 'server-1',
        name: fullName,
        tier: null,
        activity: 'Walk-in added at the counter.',
      );

  @override
  Future<void> recordVisit(String customerId) async {}
}

void main() {
  group('HomeController.load', () {
    test('exposes the repository snapshot and leaves the loading state', () async {
      final repository = _StubRepository(
        snapshot: _snapshot(tasks: const [_task, _otherTask]),
      );
      final controller = HomeController(repository);
      addTearDown(controller.dispose);

      expect(controller.isLoading, isFalse);
      expect(controller.balance, isNull);

      await controller.load(ownerDeck: false);

      expect(controller.isLoading, isFalse);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.errorMessage, isNull);
      expect(controller.tasks.map((task) => task.id), ['t1', 't2']);
      expect(controller.clients.single.id, 'c1');
      expect(controller.balance, _balance);
      expect(repository.ownerDeckSeen, isFalse);
    });

    test('passes the role through to the repository', () async {
      final repository = _StubRepository();
      final controller = HomeController(repository);
      addTearDown(controller.dispose);

      await controller.load(ownerDeck: true);

      expect(repository.ownerDeckSeen, isTrue);
    });

    test('reports a failure without falling back to demo data', () async {
      final repository = _StubRepository(error: StateError('offline'));
      final controller = HomeController(repository);
      addTearDown(controller.dispose);

      await controller.load(ownerDeck: false);

      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
      expect(controller.errorMessage, isNotNull);
      expect(controller.tasks, isEmpty);
      expect(controller.clients, isEmpty);
      expect(controller.balance, isNull);
    });

    test('drops a stale reply instead of overwriting a newer load', () async {
      final repository = _GatedRepository();
      final controller = HomeController(repository);
      addTearDown(controller.dispose);

      final first = controller.load(ownerDeck: false);
      final second = controller.load(ownerDeck: false);

      // The newer load lands first and stands.
      repository.gates[1].complete(_snapshot(tasks: const [_otherTask]));
      await second;
      expect(controller.tasks.single.id, 't2');

      // The older one arrives late and must not undo it.
      repository.gates[0].complete(_snapshot(tasks: const [_task]));
      await first;
      expect(controller.tasks.single.id, 't2');
    });
  });

  group('HomeController.completeTask', () {
    test('removes the docket and calls the repository once', () async {
      final repository = _StubRepository(
        snapshot: _snapshot(tasks: const [_task, _otherTask]),
      );
      final controller = HomeController(repository);
      addTearDown(controller.dispose);
      await controller.load(ownerDeck: false);

      final accepted = await controller.completeTask(_task);

      expect(accepted, isTrue);
      expect(repository.completeCount, 1);
      expect(controller.tasks.map((task) => task.id), ['t2']);
      expect(controller.actionError, isNull);
    });

    test('puts the docket back when the repository refuses', () async {
      final repository = _StubRepository(
        snapshot: _snapshot(tasks: const [_task, _otherTask]),
      )..completeThrows = true;
      final controller = HomeController(repository);
      addTearDown(controller.dispose);
      await controller.load(ownerDeck: false);

      final accepted = await controller.completeTask(_task);

      expect(accepted, isFalse);
      expect(controller.tasks.map((task) => task.id), ['t1', 't2']);
      expect(controller.actionError, isNotNull);
    });

    test('recordVisit calls the repository once and reports success', () async {
      final repository = _StubRepository();
      final controller = HomeController(repository);
      addTearDown(controller.dispose);

      final recorded = await controller.recordVisit('c1');

      expect(recorded, isTrue);
      expect(repository.visitCount, 1);
      expect(repository.visitedCustomerId, 'c1');
    });

    test('a refused visit reports through actionError', () async {
      final repository = _StubRepository()..visitThrows = true;
      final controller = HomeController(repository);
      addTearDown(controller.dispose);

      final recorded = await controller.recordVisit('c1');

      expect(recorded, isFalse);
      expect(controller.actionError, isNotNull);
    });

    test('takes back the action error once it has been shown', () async {
      final repository = _StubRepository(
        snapshot: _snapshot(tasks: const [_task]),
      )..completeThrows = true;
      final controller = HomeController(repository);
      addTearDown(controller.dispose);
      await controller.load(ownerDeck: false);

      await controller.completeTask(_task);
      expect(controller.actionError, isNotNull);

      controller.clearActionError();
      expect(controller.actionError, isNull);
    });
  });
}
