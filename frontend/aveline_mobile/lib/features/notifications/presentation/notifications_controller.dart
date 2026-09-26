import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import '../data/notification_repository.dart';
import '../domain/app_notification.dart';

/// Owns the notification inbox the tab renders and the count the header badges.
///
/// Every mutation is optimistic: the tile changes the moment the associate
/// touches it and is put back exactly where it was if the API refuses. That is
/// the whole reason for the rollback bookkeeping below - a swipe that appears to
/// work and quietly did not is worse than one that visibly fails.
///
/// Deletion is the one action with a window: the tile goes at once, but the API
/// is not told until [dismissCommitDelay] has passed, which is what makes Undo
/// an actual undo rather than a local lie.
class NotificationsController extends ChangeNotifier {
  NotificationsController(
    this._repository, {
    this.pageSize = 20,
    this.dismissCommitDelay = const Duration(seconds: 5),
  });

  final NotificationRepository _repository;

  /// How many notifications a page holds.
  final int pageSize;

  /// How long a dismissal can be taken back before the API is told.
  ///
  /// The tab shows its Undo offer for exactly this long, so the offer and the
  /// window cannot drift apart.
  final Duration dismissCommitDelay;

  List<AppNotification> _items = const [];
  int _total = 0;
  int _unreadCount = 0;
  bool _unreadOnly = false;

  /// The last page fetched, so the next one is asked for by number rather than
  /// derived from how many tiles happen to be on screen: a delete or a mark
  /// takes tiles away, and a page number worked out from the count would then
  /// re-request a page that has already been read.
  int _page = 1;

  /// The envelope's own word on whether the inbox continues.
  ///
  /// Kept from the server rather than derived from `items.length < total`,
  /// because the client's arithmetic drifts as soon as it removes tiles
  /// optimistically.
  bool _hasMore = false;

  bool _isLoading = false;
  bool _hasLoadedOnce = false;
  bool _isLoadingMore = false;
  String? _errorMessage;
  String? _actionError;

  bool _disposed = false;

  /// Identifies the load an in-flight reply belongs to.
  ///
  /// Switching filters or pulling to refresh can outrun the page that is on its
  /// way, and a page for a narrowing the associate has moved on from must not
  /// replace what is on screen.
  int _requestId = 0;

  _PendingDismissal? _pendingDismissal;

  /// The inbox as it should be drawn.
  List<AppNotification> get items => List.unmodifiable(_items);

  /// How many unread notifications the whole inbox holds, not just this page.
  int get unreadCount => _unreadCount;

  /// Whether the Unread narrowing is in force.
  bool get unreadOnly => _unreadOnly;

  /// Whether a first page is on its way and nothing is on screen yet.
  bool get isLoading => _isLoading;

  /// Whether a load has finished, successfully or not.
  bool get hasLoadedOnce => _hasLoadedOnce;

  bool get isLoadingMore => _isLoadingMore;

  /// Whether the inbox holds more than is on screen.
  bool get hasMore => _hasMore;

  /// Why the inbox could not be loaded, or `null`.
  String? get errorMessage => _errorMessage;

  /// Why the last action failed, or `null`.
  ///
  /// Separate from [errorMessage] because it is not a screen state: the inbox
  /// still stands, and the message is only worth a toast. The screen clears it
  /// once shown, so it is reported exactly once.
  String? get actionError => _actionError;

  /// Whether the narrowing in force finished and matched nothing.
  bool get isEmpty => _hasLoadedOnce && _items.isEmpty;

  /// Whether a delete is still inside its undo window.
  bool get hasPendingDismissal => _pendingDismissal != null;

  /// Loads the first page, optionally switching the narrowing first.
  ///
  /// Started (not awaited) from `initState`, so it notifies synchronously before
  /// any listener is attached; the first build reads the loading state instead.
  Future<void> load({bool? unreadOnly}) async {
    if (unreadOnly != null) {
      _unreadOnly = unreadOnly;
    }

    final id = ++_requestId;
    _isLoading = true;
    _isLoadingMore = false;
    _hasLoadedOnce = false;
    _errorMessage = null;
    _actionError = null;
    _items = const [];
    _total = 0;
    _page = 1;
    _hasMore = false;
    _notify();

    try {
      final page = await _repository.fetchInbox(
        page: 1,
        pageSize: pageSize,
        unreadOnly: _unreadOnly,
      );
      final unread = await _unreadCountOrNull();
      if (id != _requestId) {
        return;
      }
      _items = page.items;
      _total = page.total;
      _page = page.page;
      _hasMore = page.hasMore;
      _unreadCount = unread ?? _unreadFloor;
      _hasLoadedOnce = true;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _errorMessage = _describe(error);
      _hasLoadedOnce = true;
    } finally {
      if (id == _requestId) {
        _isLoading = false;
        _notify();
      }
    }
  }

  /// Re-reads the first page under the narrowing already in force.
  ///
  /// The inbox already on screen is left standing until the newer one lands: a
  /// pull that blanked the list would read as a failure rather than as a
  /// refresh. Pages beyond the first are dropped, because the seed of the list
  /// is what changed.
  Future<void> refresh() async {
    if (!_hasLoadedOnce) {
      return load();
    }

    final id = ++_requestId;
    try {
      final page = await _repository.fetchInbox(
        page: 1,
        pageSize: pageSize,
        unreadOnly: _unreadOnly,
      );
      final unread = await _unreadCountOrNull();
      if (id != _requestId) {
        return;
      }
      _items = page.items;
      _total = page.total;
      _page = page.page;
      _hasMore = page.hasMore;
      _unreadCount = unread ?? _unreadFloor;
      _errorMessage = null;
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

  /// Fetches the page after the ones on screen and appends it.
  Future<void> loadMore() async {
    if (_isLoading || _isLoadingMore || !hasMore) {
      return;
    }

    // Deliberately not a new request id: this page belongs to the load that is
    // already on screen, and must be dropped only if that load is replaced.
    final id = _requestId;
    _isLoadingMore = true;
    _notify();

    try {
      final page = await _repository.fetchInbox(
        page: _page + 1,
        pageSize: pageSize,
        unreadOnly: _unreadOnly,
      );
      if (id != _requestId) {
        return;
      }
      final known = {for (final item in _items) item.id};
      _items = [
        ..._items,
        ...page.items.where((item) => !known.contains(item.id)),
      ];
      _total = page.total;
      _page = page.page;
      _hasMore = page.hasMore;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _actionError = _describe(error);
    } finally {
      _isLoadingMore = false;
      if (id == _requestId) {
        _notify();
      }
    }
  }

  /// Marks one notification read, putting it back if the API refuses.
  Future<void> markRead(AppNotification item) async {
    final index = _items.indexWhere((candidate) => candidate.id == item.id);
    if (index < 0 || _items[index].isRead) {
      return;
    }

    final original = _items[index];
    final read = original.copyWith(
      isRead: true,
      readAt: DateTime.now().toUtc(),
    );
    _replaceAt(index, read);
    _unreadCount = math.max(0, _unreadCount - 1);
    // Under the Unread narrowing the tile has just stopped belonging on screen,
    // and leaving it there would cost the filter its promise.
    final leftTheNarrowing = _unreadOnly;
    if (leftTheNarrowing) {
      _removeAt(index);
      _total = math.max(0, _total - 1);
    }
    _actionError = null;
    _notify();

    try {
      await _repository.markRead(item.id);
    } catch (error) {
      _restore(index, original);
      if (leftTheNarrowing) {
        _total += 1;
      }
      _unreadCount += 1;
      _actionError = 'That notification could not be marked as read.';
      _notify();
    }
  }

  /// Marks every visible notification read, restoring the inbox if refused.
  Future<void> markAllRead() async {
    if (_unreadCount == 0) {
      return;
    }

    final previousItems = _items;
    final previousTotal = _total;
    final previousUnread = _unreadCount;
    final now = DateTime.now().toUtc();

    _items = [
      for (final item in _items)
        if (item.isRead) item else item.copyWith(isRead: true, readAt: now),
    ];
    if (_unreadOnly) {
      _items = const [];
      _total = 0;
    }
    _unreadCount = 0;
    _actionError = null;
    _notify();

    try {
      await _repository.markAllRead();
    } catch (error) {
      _items = previousItems;
      _total = previousTotal;
      _unreadCount = previousUnread;
      _actionError = 'The inbox could not be marked as read.';
      _notify();
    }
  }

  /// Takes a notification out of the inbox and starts its undo window.
  ///
  /// The API is told when the window lapses, not now: that is what makes
  /// [undoDismiss] a real undo.
  void dismiss(AppNotification item) {
    final index = _items.indexWhere((candidate) => candidate.id == item.id);
    if (index < 0) {
      return;
    }

    // The Undo offer covers one removal, so a second delete settles the first
    // rather than leaving two deletions sharing a single offer.
    _commitPendingDismissal();

    _removeAt(index);
    _total = math.max(0, _total - 1);
    if (!item.isRead) {
      _unreadCount = math.max(0, _unreadCount - 1);
    }
    _actionError = null;

    _pendingDismissal = _PendingDismissal(
      item: item,
      index: index,
      timer: Timer(dismissCommitDelay, _commitPendingDismissal),
    );
    _notify();
  }

  /// Puts back the notification whose undo window is still open.
  ///
  /// Does nothing once the window has lapsed, because by then the API has been
  /// told and there is no endpoint that would bring it back.
  void undoDismiss() {
    final pending = _pendingDismissal;
    if (pending == null) {
      return;
    }
    _pendingDismissal = null;
    pending.timer.cancel();

    _restore(pending.index, pending.item);
    _total += 1;
    if (!pending.item.isRead) {
      _unreadCount += 1;
    }
    _notify();
  }

  /// Applies an authoritative count that arrived with a realtime payload.
  ///
  /// The server computes the count after the row was written, so the badge is
  /// exact before the list's own reply lands. Clamped at zero, and silent when
  /// the value has not changed, so an arrival cannot rebuild a frame for a
  /// number nobody can see move. The list still refreshes separately: rows stay
  /// the API's authority.
  void applyUnreadCount(int count) {
    final normalized = math.max(0, count);
    if (normalized == _unreadCount) {
      return;
    }
    _unreadCount = normalized;
    _notify();
  }

  /// Clears [actionError] once the screen has reported it.
  ///
  /// Deliberately silent: the screen calls this from its own listener, and
  /// notifying from inside a notification would run that listener again. Nothing
  /// else reads this field, so there is nothing to tell.
  void clearActionError() {
    _actionError = null;
  }

  /// Empties the inbox on sign-out.
  ///
  /// The next associate to sign in must not open the tab onto the last one's
  /// notifications, and a delete still inside its undo window belongs to a
  /// session that is over: the request would go out without a token, so it is
  /// dropped rather than committed.
  void clearInbox() {
    _pendingDismissal?.timer.cancel();
    _pendingDismissal = null;
    // Invalidates any reply still in flight, so a page for the old user cannot
    // land in the new one's inbox.
    _requestId++;
    _items = const [];
    _total = 0;
    _unreadCount = 0;
    _page = 1;
    _hasMore = false;
    _isLoading = false;
    _isLoadingMore = false;
    _hasLoadedOnce = false;
    _errorMessage = null;
    _actionError = null;
    _notify();
  }

  @override
  void dispose() {
    _disposed = true;
    // A delete the associate already made is honoured rather than dropped: the
    // window lapsed in their mind the moment the tile left the screen.
    final pending = _pendingDismissal;
    _pendingDismissal = null;
    if (pending != null) {
      pending.timer.cancel();
      unawaited(_commit(pending));
    }
    super.dispose();
  }

  /// The unread count the loaded tiles can prove.
  ///
  /// A floor rather than a total: it counts only what is on screen. It stands in
  /// when the count endpoint fails, so the badge never reads "all caught up"
  /// while unread notifications are in front of the associate. The next
  /// successful refresh corrects it.
  int get _unreadFloor => _items.where((item) => !item.isRead).length;

  /// The unread count, or `null` when its own endpoint failed.
  ///
  /// A count that could not be fetched must not take the loaded inbox down with
  /// it: the list is the point of the screen and the badge is an extra.
  Future<int?> _unreadCountOrNull() async {
    try {
      return await _repository.fetchUnreadCount();
    } catch (_) {
      return null;
    }
  }

  void _commitPendingDismissal() {
    final pending = _pendingDismissal;
    if (pending == null) {
      return;
    }
    _pendingDismissal = null;
    pending.timer.cancel();
    unawaited(_commit(pending));
  }

  /// Tells the API about a dismissal that was not taken back.
  Future<void> _commit(_PendingDismissal pending) async {
    try {
      await _repository.dismiss(pending.item.id);
    } catch (error) {
      // The tile returns to where it was, so the inbox never claims a delete
      // that did not happen.
      _restore(pending.index, pending.item);
      _total += 1;
      if (!pending.item.isRead) {
        _unreadCount += 1;
      }
      _actionError = 'That notification could not be deleted.';
      _notify();
    }
  }

  void _replaceAt(int index, AppNotification item) {
    final next = [..._items];
    next[index] = item;
    _items = next;
  }

  void _removeAt(int index) {
    final next = [..._items]..removeAt(index);
    _items = next;
  }

  void _restore(int index, AppNotification item) {
    final next = [..._items];
    next.insert(index.clamp(0, next.length), item);
    _items = next;
  }

  /// `error.toString()` without the `Exception: ` Dart prints in front of it.
  String _describe(Object error) =>
      error.toString().replaceFirst('Exception: ', '');

  /// `notifyListeners` that tolerates a reply landing after the screen is gone.
  void _notify() {
    if (!_disposed) {
      notifyListeners();
    }
  }
}

/// A dismissal that has left the screen but has not reached the API yet.
class _PendingDismissal {
  _PendingDismissal({
    required this.item,
    required this.index,
    required this.timer,
  });

  final AppNotification item;

  /// Where the tile sat, so undo puts it back in place rather than at the end.
  final int index;

  final Timer timer;
}
