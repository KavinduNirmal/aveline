import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import '../data/thread_repository.dart';
import '../domain/conversation.dart';
import '../domain/thread_message.dart';

/// Owns one client's thread: its history, the message being typed into it, and
/// the draft waiting on the associate.
///
/// History is served oldest first and paged from the oldest end, so this opens on
/// the *last* page: a thread that opened on page one would open at the wrong end
/// of the story. Earlier pages are then fetched backwards, one press at a time,
/// which keeps the window contiguous from the newest message down.
///
/// Sending is optimistic. The words appear before the API answers and are marked
/// as sending, because an associate who has just tapped send is owed an answer
/// about whether their words went anywhere; a refusal leaves the message on screen
/// marked failed, with the text still there to try again.
class ClientThreadController extends ChangeNotifier {
  ClientThreadController(
    this.conversation,
    this._repository, {
    this.pageSize = 50,
  });

  /// The thread being read.
  final Conversation conversation;

  final ThreadRepository _repository;

  /// How many messages a page holds.
  final int pageSize;

  List<ThreadMessage> _messages = const [];

  /// The page the window is currently reading from.
  int _page = 1;

  bool _isLoading = false;
  bool _hasLoadedOnce = false;
  bool _isLoadingEarlier = false;
  bool _isSending = false;
  String? _errorMessage;
  String? _actionError;

  bool _disposed = false;

  /// Identifies the read an in-flight reply belongs to.
  int _requestId = 0;

  /// Counts the optimistic messages this session has made, so two sends before
  /// either is confirmed cannot share a local id.
  int _localCount = 0;

  /// The thread, oldest first.
  List<ThreadMessage> get messages => List.unmodifiable(_messages);

  bool get isLoading => _isLoading;

  /// Whether a read has finished, successfully or not.
  bool get hasLoadedOnce => _hasLoadedOnce;

  bool get isLoadingEarlier => _isLoadingEarlier;

  /// Whether this device is still trying to send something.
  bool get isSending => _isSending;

  /// Whether there is more history above the window.
  bool get hasEarlier => _page > 1;

  /// Why the thread could not be read, or `null`.
  String? get errorMessage => _errorMessage;

  /// Why the last thing the associate did failed, or `null`.
  ///
  /// Separate from [errorMessage] because it is not a screen state: the thread
  /// still stands, and the message is only worth a toast. The screen clears it
  /// once shown, so it is reported exactly once.
  String? get actionError => _actionError;

  /// Whether the read finished and the two of them have never spoken.
  bool get isEmpty => _hasLoadedOnce && _messages.isEmpty;

  /// Opens the thread on its newest words.
  Future<void> load() async {
    final id = ++_requestId;
    _isLoading = true;
    _hasLoadedOnce = false;
    _errorMessage = null;
    _actionError = null;
    _messages = const [];
    _page = 1;
    _notify();

    try {
      final first = await _repository.fetchMessages(
        conversation.id,
        page: 1,
        pageSize: pageSize,
      );
      if (id != _requestId) {
        return;
      }

      if (first.pageCount <= 1) {
        _messages = List.unmodifiable(first.items);
        _page = 1;
        _hasLoadedOnce = true;
        return;
      }

      // The thread is longer than a page, so the newest words are at the end.
      final last = await _repository.fetchMessages(
        conversation.id,
        page: first.pageCount,
        pageSize: pageSize,
      );
      if (id != _requestId) {
        return;
      }
      _messages = List.unmodifiable(last.items);
      _page = last.page;
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

  /// Fetches the page above the window and puts it on top.
  Future<void> loadEarlier() async {
    if (_isLoading || _isLoadingEarlier || !hasEarlier) {
      return;
    }

    // Not a new request id: this page belongs to the thread already on screen and
    // must be dropped only if that read is replaced.
    final id = _requestId;
    _isLoadingEarlier = true;
    _errorMessage = null;
    _notify();

    try {
      final earlier = await _repository.fetchMessages(
        conversation.id,
        page: _page - 1,
        pageSize: pageSize,
      );
      if (id != _requestId) {
        return;
      }
      final known = {for (final message in _messages) message.id};
      _messages = List.unmodifiable([
        ...earlier.items.where((message) => !known.contains(message.id)),
        ..._messages,
      ]);
      _page = earlier.page;
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      _errorMessage = _describe(error);
    } finally {
      _isLoadingEarlier = false;
      if (id == _requestId) {
        _notify();
      }
    }
  }

  /// Sends what the associate typed.
  Future<void> send(String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty || _isSending) {
      return;
    }

    final local = ThreadMessage(
      id: 'local_${++_localCount}',
      author: MessageAuthor.staff,
      kind: MessageKind.note,
      status: MessageStatus.sent,
      text: trimmed,
      createdAt: DateTime.now().toUtc(),
      deliveryStatus: MessageDeliveryStatus.sending,
    );
    _messages = List.unmodifiable([..._messages, local]);
    _isSending = true;
    _actionError = null;
    _notify();

    await _deliver(local);
  }

  /// Tries a failed message again.
  Future<void> retry(ThreadMessage message) async {
    if (!message.isFailed || _isSending) {
      return;
    }
    final index = _messages.indexWhere((item) => item.id == message.id);
    if (index < 0) {
      return;
    }

    final retrying = message.copyWith(
      deliveryStatus: MessageDeliveryStatus.sending,
    );
    _replace(index, retrying);
    _isSending = true;
    _actionError = null;
    _notify();

    await _deliver(retrying);
  }

  /// Approves or dismisses the draft waiting on the associate.
  Future<void> decideDraft(
    ThreadMessage draft, {
    required bool approved,
  }) async {
    final index = _messages.indexWhere((item) => item.id == draft.id);
    if (index < 0 || !_messages[index].needsSignOff) {
      return;
    }

    final original = _messages[index];
    _replace(
      index,
      original.copyWith(
        status: approved ? MessageStatus.sent : MessageStatus.cancelled,
      ),
    );
    _actionError = null;
    _notify();

    try {
      await _repository.decideSignOff(
        conversationId: conversation.id,
        message: original,
        approved: approved,
      );
    } catch (error) {
      // Back where it was: a draft that quietly stopped waiting on the associate
      // would leave a client's question unanswered and nobody told.
      _restore(index, original);
      _actionError = 'That decision could not be recorded.';
      _notify();
    }
  }

  /// Clears [actionError] once the screen has reported it.
  ///
  /// Deliberately silent: the screen calls this from its own listener, and
  /// notifying from inside a notification would run that listener again.
  void clearActionError() {
    _actionError = null;
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }

  /// Hands [local] to the API and swaps the optimistic row for the stored one.
  Future<void> _deliver(ThreadMessage local) async {
    final index = _messages.indexWhere((item) => item.id == local.id);
    if (index < 0) {
      return;
    }

    try {
      final stored = await _repository.sendMessage(conversation.id, local.text);
      final at = _messages.indexWhere((item) => item.id == local.id);
      if (at >= 0) {
        _replace(at, stored);
      }
      _actionError = null;
    } catch (error) {
      final at = _messages.indexWhere((item) => item.id == local.id);
      if (at >= 0) {
        _replace(
          at,
          _messages[at].copyWith(deliveryStatus: MessageDeliveryStatus.failed),
        );
      }
      _actionError = 'That message could not be sent.';
    } finally {
      _isSending = false;
      _notify();
    }
  }

  void _replace(int index, ThreadMessage message) {
    final next = [..._messages];
    next[index] = message;
    _messages = List.unmodifiable(next);
  }

  void _restore(int index, ThreadMessage message) {
    final next = [..._messages];
    next.insert(math.min(index, next.length), message);
    _messages = List.unmodifiable(next);
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
