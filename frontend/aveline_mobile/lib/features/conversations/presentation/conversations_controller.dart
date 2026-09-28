import 'dart:async';

import 'package:flutter/foundation.dart';

import '../data/conversation_repository.dart';
import '../domain/conversation.dart';

/// Owns the message inbox and the order it is read in.
///
/// The order is the point of this class. The Salon is pinned above everything
/// else whatever it was handed, because the concierge is a fixed destination
/// rather than one of the results, and the client threads follow it by the newest
/// word. Nothing else in the app has to remember that rule, which is why the
/// repository hands back an unordered list.
///
/// The search narrows the client threads and the notices and deliberately leaves
/// the Salon where it is: a search is about finding a person, and moving the
/// concierge's row out from under the thumb to do it would be a surprise.
class ConversationsController extends ChangeNotifier {
  ConversationsController(this._repository);

  final ConversationRepository _repository;

  List<Conversation> _items = const [];
  String _query = '';

  /// The page last read, and how many threads the whole inbox holds.
  int _page = 0;
  int _total = 0;

  bool _isLoading = false;
  bool _isLoadingMore = false;
  bool _hasLoadedOnce = false;
  bool _isWaitingForOrg = false;
  String? _errorMessage;
  bool _disposed = false;

  /// Identifies the read an in-flight reply belongs to, so a reply for a load the
  /// associate has already moved on from cannot land.
  int _requestId = 0;

  /// The Salon, or `null` until the thread exists.
  Conversation? get aveline {
    for (final conversation in _items) {
      if (conversation.isAveline) {
        return conversation;
      }
    }
    return null;
  }

  /// The client threads that match the search, newest word first.
  List<Conversation> get clients => _narrowed(ConversationKind.customer);

  /// The notices that match the search, newest first.
  List<Conversation> get announcements => _narrowed(ConversationKind.system);

  /// The inbox as the screen draws it: the Salon, the clients, then the notices.
  List<Conversation> get items => [?aveline, ...clients, ...announcements];

  /// The search in force, trimmed. Empty when the inbox is not narrowed.
  String get query => _query;

  bool get isSearching => _query.isNotEmpty;

  /// Whether the search matched anything besides the pinned Salon.
  bool get hasMatches => clients.isNotEmpty || announcements.isNotEmpty;

  bool get isLoading => _isLoading;

  /// Whether a further page is in flight.
  bool get isLoadingMore => _isLoadingMore;

  /// How many threads the server says the whole inbox holds.
  ///
  /// Not the same as `items.length`: the screen prints how many are loaded
  /// against this so the footer cannot claim the list is complete while it is
  /// not.
  int get total => _total;

  /// Whether the server holds more threads than have been loaded.
  bool get hasMore => _items.length < _total;

  /// Whether the read is waiting for the organization context to arrive.
  ///
  /// A null org id is a "not yet", not a failure: the canonical id comes from
  /// `GET /orgs/my` after the shell mounts. The error state is reserved for the
  /// server's own refusal, so the two must not read the same on screen.
  bool get isWaitingForOrg => _isWaitingForOrg;

  /// Whether a read has finished, successfully or not.
  bool get hasLoadedOnce => _hasLoadedOnce;

  /// Why the inbox could not be read, or `null`.
  ///
  /// A refresh that fails keeps the inbox on screen and leaves its message here;
  /// the screen only shows it when it has nothing else to show.
  String? get errorMessage => _errorMessage;

  /// Whether the read finished and the boutique has no conversations at all.
  bool get isEmpty => _hasLoadedOnce && _items.isEmpty;

  /// Reads the inbox.
  ///
  /// Started (not awaited) from `initState`, so it notifies synchronously before
  /// any listener is attached; the first build reads the loading state instead.
  Future<void> load() async {
    final id = ++_requestId;
    _isLoading = true;
    _isLoadingMore = false;
    _hasLoadedOnce = false;
    _isWaitingForOrg = false;
    _errorMessage = null;
    _items = const [];
    _page = 0;
    _total = 0;
    _notify();

    try {
      final page = await _repository.fetchConversations();
      if (id != _requestId) {
        return;
      }
      _items = List.unmodifiable(page.items);
      _page = page.page;
      _total = page.total;
      _hasLoadedOnce = true;
    } on OrgContextUnavailable {
      if (id != _requestId) {
        return;
      }
      _isWaitingForOrg = true;
      _errorMessage = null;
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

  /// Re-reads the inbox, leaving what is on screen standing until the newer one
  /// lands: a pull that blanked the column would read as a failure.
  ///
  /// A refresh restarts from the first page: the whole inbox may have changed,
  /// and keeping accumulated pages would leave stale rows above fresh ones.
  Future<void> refresh() async {
    if (!_hasLoadedOnce) {
      return load();
    }

    final id = ++_requestId;
    _isLoadingMore = false;
    try {
      final page = await _repository.fetchConversations();
      if (id != _requestId) {
        return;
      }
      _items = List.unmodifiable(page.items);
      _page = page.page;
      _total = page.total;
      _isWaitingForOrg = false;
      _errorMessage = null;
    } on OrgContextUnavailable {
      if (id != _requestId) {
        return;
      }
      _isWaitingForOrg = true;
      _errorMessage = null;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _errorMessage = _describe(error);
    } finally {
      if (id == _requestId) {
        _notify();
      }
    }
  }

  /// Loads the next page and appends it.
  ///
  /// A thread already held is not appended twice, so a row cannot appear on two
  /// pages - which the server's id tiebreak makes unlikely but the client should
  /// not depend on.
  Future<void> loadMore() async {
    if (_isLoadingMore || !_hasLoadedOnce || !hasMore) {
      return;
    }

    _isLoadingMore = true;
    final id = _requestId;
    _notify();

    try {
      final page = await _repository.fetchConversations(page: _page + 1);
      if (id != _requestId) {
        return;
      }
      final seen = _items.map((conversation) => conversation.id).toSet();
      _items = List.unmodifiable([
        ..._items,
        ...page.items.where((conversation) => seen.add(conversation.id)),
      ]);
      _page = page.page;
      _total = page.total;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _errorMessage = _describe(error);
    } finally {
      if (id == _requestId) {
        _isLoadingMore = false;
        _notify();
      }
    }
  }

  /// Narrows the inbox to the threads matching [query].
  void search(String query) {
    final next = query.trim();
    if (next == _query) {
      return;
    }
    _query = next;
    _notify();
  }

  /// Applies an inbox tile that arrived over the hub.
  ///
  /// A thread already on screen is replaced in place, so its preview, time and markers move
  /// without a re-read. A thread the list does not hold is not invented: the first page is
  /// re-read, so a brand-new thread lands in the position the server gives it.
  void applyChanged(Conversation conversation) {
    final index = _items.indexWhere((item) => item.id == conversation.id);
    if (index < 0) {
      unawaited(refresh());
      return;
    }

    final next = [..._items];
    next[index] = conversation;
    _items = List.unmodifiable(next);
    _notify();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }

  /// One kind of thread, narrowed and ordered newest first.
  List<Conversation> _narrowed(ConversationKind kind) {
    final matches =
        _items
            .where((conversation) => conversation.kind == kind && _matches(conversation))
            .toList()
          ..sort(_byRecency);
    return List.unmodifiable(matches);
  }

  /// Whether [conversation] survives the search in force.
  ///
  /// Matched on the client's name and on the last message, which is what a
  /// person searching their messages looks for. A thread with neither - a notice,
  /// or a client the name never arrived for - survives only an empty search.
  bool _matches(Conversation conversation) {
    if (_query.isEmpty) {
      return true;
    }
    final needle = _query.toLowerCase();
    final name = conversation.customerName?.toLowerCase() ?? '';
    final preview = conversation.lastMessagePreview?.toLowerCase() ?? '';
    return name.contains(needle) || preview.contains(needle);
  }

  /// Newest word first, and a thread nobody has spoken in yet last.
  ///
  /// A client thread opened from the client book has no messages; pinning it
  /// above a thread with a fresh reply would bury the reply.
  int _byRecency(Conversation a, Conversation b) {
    final left = a.lastMessageAt;
    final right = b.lastMessageAt;
    if (left == null && right == null) {
      return 0;
    }
    if (left == null) {
      return 1;
    }
    if (right == null) {
      return -1;
    }
    return right.compareTo(left);
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
