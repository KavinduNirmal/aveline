import 'package:flutter/foundation.dart';

import '../data/home_repository.dart';
import '../domain/blossom_usage.dart';
import '../domain/client_highlight.dart';
import '../domain/focus_task.dart';

/// Owns everything the Home tab draws: today's deck, the client row and the
/// Blossom meter.
///
/// Home's blocks used to be seeded from demo constants inside the screen, which
/// made "refreshing" mean "seeding again" and made a walk-in vanish on the next
/// pull. One controller owns the data instead, so the header count, the pile,
/// the strip and the row all read the same state and move together.
///
/// Completion is optimistic: the docket leaves the pile the moment it is signed
/// off and is put back exactly where it was if the server refuses. A sign-off
/// that appears to work and quietly did not is worse than one that visibly
/// fails.
class HomeController extends ChangeNotifier {
  HomeController(this._repository);

  final HomeRepository _repository;

  List<FocusTask> _tasks = const [];
  List<ClientHighlight> _clients = const [];
  BlossomUsage? _balance;

  bool _isLoading = false;
  bool _hasLoadedOnce = false;
  bool _awaitingOrgContext = false;
  String? _errorMessage;
  String? _actionError;

  bool _disposed = false;

  /// Identifies the load an in-flight reply belongs to.
  ///
  /// A pull-to-refresh or a role change can outrun the read already on its way,
  /// and a reply for state the screen has moved on from must not replace what is
  /// on screen.
  int _requestId = 0;

  /// Today's deck, as the server ordered it.
  List<FocusTask> get tasks => List.unmodifiable(_tasks);

  /// The clients behind `Direct client link`, most active first.
  List<ClientHighlight> get clients => List.unmodifiable(_clients);

  /// The shop's Blossom position, or `null` when it is not known or not
  /// readable by this role.
  BlossomUsage? get balance => _balance;

  /// Whether a first load is on its way and nothing is on screen yet.
  bool get isLoading => _isLoading;

  /// Whether a load has finished, successfully or not.
  bool get hasLoadedOnce => _hasLoadedOnce;

  /// Whether the screen is waiting for the account's organization context.
  ///
  /// Every Home endpoint is org-scoped, so until `/orgs/my` has answered there is
  /// nothing to call. That is a "not yet", not a failure: the screen keeps its
  /// loading state and reloads when the context arrives.
  bool get awaitingOrgContext => _awaitingOrgContext;

  /// Why the screen could not be loaded, or `null`.
  ///
  /// A failure is reported, never papered over: Home has no demo fallback, so
  /// this is what the screen renders instead of fiction.
  String? get errorMessage => _errorMessage;

  /// Why the last action failed, or `null`.
  ///
  /// Separate from [errorMessage] because the screen still stands; the message
  /// is worth a toast, and the screen clears it once shown.
  String? get actionError => _actionError;

  /// Loads the screen from scratch, showing the loading state.
  Future<void> load({required bool ownerDeck}) async {
    final id = ++_requestId;
    _isLoading = true;
    _hasLoadedOnce = false;
    _awaitingOrgContext = false;
    _errorMessage = null;
    _actionError = null;
    _notify();

    try {
      final snapshot = await _repository.fetchHome(ownerDeck: ownerDeck);
      if (id != _requestId) {
        return;
      }
      _tasks = snapshot.tasks;
      _clients = snapshot.clients;
      _balance = snapshot.balance;
      _hasLoadedOnce = true;
    } on OrgContextUnavailable {
      if (id != _requestId) {
        return;
      }
      // Not an error: the org id has not arrived yet. Stay loading.
      _awaitingOrgContext = true;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _errorMessage = _describe(error);
      _hasLoadedOnce = true;
    } finally {
      if (id == _requestId) {
        _isLoading = _awaitingOrgContext;
        _notify();
      }
    }
  }

  /// Re-reads the screen, leaving what is on it standing until the newer read
  /// lands.
  ///
  /// A pull that blanked the deck would read as a failure rather than as a
  /// refresh, so a failed refresh is an action error, not a screen error.
  Future<void> refresh({required bool ownerDeck}) async {
    if (!_hasLoadedOnce) {
      return load(ownerDeck: ownerDeck);
    }

    final id = ++_requestId;
    try {
      final snapshot = await _repository.fetchHome(ownerDeck: ownerDeck);
      if (id != _requestId) {
        return;
      }
      _tasks = snapshot.tasks;
      _clients = snapshot.clients;
      _balance = snapshot.balance;
      _errorMessage = null;
      _awaitingOrgContext = false;
    } on OrgContextUnavailable {
      if (id != _requestId) {
        return;
      }
      _awaitingOrgContext = true;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _actionError = _describe(error);
    } finally {
      if (id == _requestId) {
        _notify();
      }
    }
  }

  /// Creates a counter walk-in and returns the server's client.
  ///
  /// Deliberately does not mutate the row: creation is a write, and the row's
  /// membership is owned by the widget that prepends the returned client. On
  /// failure the reason is reported through [actionError] and `null` is
  /// returned.
  Future<ClientHighlight?> createWalkIn(String fullName) async {
    try {
      return await _repository.createWalkIn(fullName);
    } catch (error) {
      _actionError = _describe(error);
      _notify();
      return null;
    }
  }

  /// Records a counter visit. Returns whether the server accepted it.
  ///
  /// The screen must not say a visit was logged when it was not, so a refusal is
  /// reported through [actionError] and the caller stays on the picker's answer.
  Future<bool> recordVisit(String customerId) async {
    try {
      await _repository.recordVisit(customerId);
      return true;
    } catch (error) {
      _actionError = _describe(error);
      _notify();
      return false;
    }
  }

  /// Records one docket's decision, removing it optimistically.
  ///
  /// Returns whether the server accepted it. On refusal the docket comes back
  /// and [actionError] carries the reason.
  Future<bool> completeTask(FocusTask task) async {
    final previous = _tasks;
    _tasks = _tasks.where((candidate) => candidate.id != task.id).toList();
    _notify();

    try {
      await _repository.completeTask(task);
      return true;
    } catch (error) {
      _tasks = previous;
      _actionError = _describe(error);
      _notify();
      return false;
    }
  }

  /// Takes back the last action refusal once it has been shown.
  void clearActionError() {
    if (_actionError == null) {
      return;
    }
    _actionError = null;
    _notify();
  }

  String _describe(Object error) {
    final message = error.toString();
    return message.startsWith('Exception: ')
        ? message.substring('Exception: '.length)
        : message;
  }

  void _notify() {
    if (_disposed) {
      return;
    }
    notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
