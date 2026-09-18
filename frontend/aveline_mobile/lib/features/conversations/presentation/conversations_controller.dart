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

  bool _isLoading = false;
  bool _hasLoadedOnce = false;
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

  /// How many unread messages the whole inbox holds, on screen or not.
  ///
  /// Counted over everything rather than over what the search left, because the
  /// number is about the inbox rather than about the narrowing.
  int get unreadTotal =>
      _items.fold(0, (total, conversation) => total + conversation.unreadCount);

  /// The search in force, trimmed. Empty when the inbox is not narrowed.
  String get query => _query;

  bool get isSearching => _query.isNotEmpty;

  /// Whether the search matched anything besides the pinned Salon.
  bool get hasMatches => clients.isNotEmpty || announcements.isNotEmpty;

  bool get isLoading => _isLoading;

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
    _hasLoadedOnce = false;
    _errorMessage = null;
    _items = const [];
    _notify();

    try {
      final conversations = await _repository.fetchConversations();
      if (id != _requestId) {
        return;
      }
      _items = List.unmodifiable(conversations);
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

  /// Re-reads the inbox, leaving what is on screen standing until the newer one
  /// lands: a pull that blanked the column would read as a failure.
  Future<void> refresh() async {
    if (!_hasLoadedOnce) {
      return load();
    }

    final id = ++_requestId;
    try {
      final conversations = await _repository.fetchConversations();
      if (id != _requestId) {
        return;
      }
      _items = List.unmodifiable(conversations);
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

  /// Narrows the inbox to the threads matching [query].
  void search(String query) {
    final next = query.trim();
    if (next == _query) {
      return;
    }
    _query = next;
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
