import '../../../../core/network/org_context.dart';
import '../domain/blossom_usage.dart';
import '../domain/client_highlight.dart';
import '../domain/focus_task.dart';

// Re-exported so existing importers of this file keep seeing the one exception,
// which now lives in core alongside the "not yet" contract it expresses.
export '../../../../core/network/org_context.dart' show OrgContextUnavailable;

/// Everything Home draws, from one read.
///
/// Home has three separate blocks fed by three separate endpoints (the focus
/// feed, the client highlights and the Blossom balance). This snapshot is the
/// client's aggregate of them, so the screen and its controller speak in one
/// object rather than three independently-loading ones.
class HomeSnapshot {
  const HomeSnapshot({
    required this.tasks,
    required this.clients,
    required this.balance,
  });

  /// Today's focus deck, as the server ordered it.
  final List<FocusTask> tasks;

  /// The clients behind `Direct client link`, most active first.
  final List<ClientHighlight> clients;

  /// The shop's Blossom position for this cycle, or `null` when the caller's
  /// role may not read it. The card is hidden rather than showing a zero.
  final BlossomUsage? balance;
}

/// The one source Home reads.
///
/// The production implementation is an API repository over the three endpoints;
/// this interface exists so the screen and the controller can be built and
/// tested before those endpoints land, and so the demo constants can never be
/// reached from production code as a silent fallback.
abstract interface class HomeRepository {
  /// Loads the screen's data.
  ///
  /// [ownerDeck] is whether the signed-in role should see the owner's work. It
  /// is a filter for the derived feed's capability rules, not a permission
  /// check: the server decides what the caller may actually see.
  ///
  /// Throws [OrgContextUnavailable] when the organization id is not known yet.
  Future<HomeSnapshot> fetchHome({required bool ownerDeck});

  /// Records the associate's decision on one docket.
  ///
  /// Throws when the server refuses, so the controller can put the docket back.
  Future<void> completeTask(FocusTask task);

  /// Creates a counter walk-in and returns the client the server recorded.
  ///
  /// The returned id is the real profile id, so the tile the row prepends
  /// resolves to the client that now exists.
  Future<ClientHighlight> createWalkIn(String fullName);

  /// Records a counter visit for [customerId].
  ///
  /// Throws when the server refuses, so the screen does not claim a visit that
  /// was not recorded.
  Future<void> recordVisit(String customerId);
}
