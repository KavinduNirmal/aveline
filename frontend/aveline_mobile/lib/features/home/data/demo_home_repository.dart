import '../domain/client_highlight.dart';
import '../domain/focus_task.dart';
import 'demo_blossom_usage.dart';
import 'demo_client_highlights.dart';
import 'demo_focus_tasks.dart';
import 'home_repository.dart';

/// A stand-in source for Home, kept for development and tests.
///
/// Production reads the API through `ApiHomeRepository`; this exists so the
/// screen can be exercised without a running backend, and so the demo constants
/// are reachable only by an explicit choice rather than as a silent fallback.
class DemoHomeRepository implements HomeRepository {
  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) async {
    return HomeSnapshot(
      tasks: demoFocusTasks(isOwner: ownerDeck),
      clients: demoClientHighlights(),
      balance: demoBlossomUsage,
    );
  }

  @override
  Future<void> completeTask(FocusTask task) async {
    // The demo deck holds no server state, so there is nothing to record.
  }

  @override
  Future<void> recordVisit(String customerId) async {
    // The demo source has no server, so there is no counter to move.
  }

  @override
  Future<ClientHighlight> createWalkIn(String fullName) async {
    // A fabricated id on purpose: this source has no server to ask. Production
    // uses the id the customer endpoint returns.
    return ClientHighlight(
      id: 'demo-walk-in-${DateTime.now().microsecondsSinceEpoch}',
      name: fullName,
      tier: null,
      activity: 'Walk-in added at the counter.',
    );
  }
}
