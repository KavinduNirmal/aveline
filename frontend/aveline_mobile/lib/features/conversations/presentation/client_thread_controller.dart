import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../../core/network/org_context.dart';
import '../../../shared/utils/uuid.dart';
import '../data/thread_repository.dart';
import '../domain/conversation.dart';
import '../domain/thread_message.dart';

/// One file the composer is holding.
///
/// The bytes are kept until the upload succeeds, so a failed upload can be retried without the
/// associate picking the file again and without losing the composed text.
class PendingThreadAttachment {
  PendingThreadAttachment({
    required this.localId,
    required this.fileName,
    required this.contentType,
    required this.bytes,
    this.width,
    this.height,
  });

  final String localId;
  final String fileName;
  final String contentType;
  final Uint8List bytes;
  final int? width;
  final int? height;

  /// The server id, once the upload succeeded.
  String? attachmentId;

  bool uploading = false;
  String? error;

  bool get isUploaded => attachmentId != null;
  bool get isFailed => error != null;
}

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

  /// The server's per-message attachment cap (`MediaContentTypes.MaxPerMessage`), enforced
  /// when a send binds its files (`ConversationService.cs:307-310`).
  ///
  /// Mirrored on the pick path so the sixth file is refused before a byte is uploaded:
  /// uploading bytes that could never be bound only leaves an orphan for the sweep, and the
  /// associate gets a generic send failure instead of an answer about the file.
  static const int maxAttachmentsPerMessage = 5;

  /// What the associate is told when a message already carries the cap.
  ///
  /// Deliberately the API's own sentence, built the same way from the same number, so the
  /// client cannot drift from the server on either the rule or the wording.
  static const String attachmentCapMessage =
      'A message may carry at most $maxAttachmentsPerMessage attachments.';

  List<ThreadMessage> _messages = const [];

  /// The page the window is currently reading from.
  int _page = 1;

  /// The page size the server actually served, which is its clamp of what was
  /// asked for. `null` until a read settles.
  int? _servedPageSize;

  bool _isLoading = false;
  bool _hasLoadedOnce = false;
  bool _isWaitingForOrg = false;
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

  /// The newest server message this device has already told the API it read, so a re-render
  /// does not repeat the write.
  String? _lastMarkedReadId;

  /// The files the composer is holding, in the order they were picked.
  final Map<String, PendingThreadAttachment> _pendingAttachments = {};

  /// The attachments each optimistic message carried, so a retry names the same ones.
  final Map<String, List<String>> _attachmentIdsByLocalId = {};

  int _attachmentCount = 0;

  /// The thread, oldest first.
  List<ThreadMessage> get messages => List.unmodifiable(_messages);

  bool get isLoading => _isLoading;

  /// Whether a read has finished, successfully or not.
  bool get hasLoadedOnce => _hasLoadedOnce;

  /// Whether the read is waiting for the organization id to arrive.
  ///
  /// A "not yet" rather than a failure: the screen keeps its loading state until the id
  /// from `GET /orgs/my` is known, instead of showing an error the boutique never gave.
  bool get isWaitingForOrg => _isWaitingForOrg;

  bool get isLoadingEarlier => _isLoadingEarlier;

  /// Whether this device is still trying to send something.
  bool get isSending => _isSending;

  /// Whether there is more history above the window.
  bool get hasEarlier => _page > 1;

  /// The files waiting to be sent, in the order they were picked.
  List<PendingThreadAttachment> get pendingAttachments =>
      List.unmodifiable(_pendingAttachments.values);

  /// Whether an upload is still in flight, which holds the send back: a message must not be
  /// sent naming an attachment the server has not stored yet.
  bool get isUploadingAttachments =>
      _pendingAttachments.values.any((attachment) => attachment.uploading);

  /// Whether every held file is stored.
  ///
  /// A failed upload holds the send too, rather than being silently dropped: the associate
  /// either retries it or removes it, and the composed text is never lost either way.
  bool get areAttachmentsReady =>
      _pendingAttachments.values.every((attachment) => attachment.isUploaded);

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

  /// Opens the thread on its newest words, or on [around] when a deep-link named a message.
  ///
  /// The anchored read is a single page: the server decides which page holds the anchor and
  /// echoes it, so `hasEarlier` still follows from the response and the window stays contiguous.
  Future<void> load({String? around}) async {
    final id = ++_requestId;
    _isLoading = true;
    _hasLoadedOnce = false;
    _errorMessage = null;
    _actionError = null;
    _messages = const [];
    _page = 1;
    _servedPageSize = null;
    _isWaitingForOrg = false;
    _notify();

    try {
      final first = await _repository.fetchMessages(
        conversation.id,
        page: 1,
        pageSize: pageSize,
        around: around,
      );
      if (id != _requestId) {
        return;
      }

      if (around != null) {
        // The anchor's page is the window: the server served exactly it, so there is no
        // last-page dance and the echoed page carries the position.
        final served = first.pageSize > 0 ? first.pageSize : pageSize;
        _servedPageSize = served;
        _messages = List.unmodifiable(first.items);
        _page = first.page;
        _hasLoadedOnce = true;
        await _markReadIfNewest();
        return;
      }

      // The server clamps pageSize and echoes what it served, so the last page is computed
      // from that value rather than from what was asked for. A body that omits the field
      // reads as 0, which [ThreadPage.pageCount] treats as one page: the thread stays on
      // page one rather than guessing a page from a value the server never confirmed.
      final served = first.pageSize > 0 ? first.pageSize : pageSize;
      _servedPageSize = served;

      if (first.pageCount <= 1) {
        _messages = List.unmodifiable(first.items);
        _page = 1;
        _hasLoadedOnce = true;
        await _markReadIfNewest();
        return;
      }

      // The thread is longer than a page, so the newest words are at the end.
      final last = await _repository.fetchMessages(
        conversation.id,
        page: first.pageCount,
        pageSize: served,
      );
      if (id != _requestId) {
        return;
      }
      _messages = List.unmodifiable(last.items);
      _page = last.page;
      _hasLoadedOnce = true;
      await _markReadIfNewest();
    } catch (error) {
      if (id != _requestId) {
        return;
      }
      // A missing organization id is a "not yet", not a failure: the screen keeps its
      // loading state rather than claiming the thread could not be read.
      if (error is OrgContextUnavailable) {
        _isWaitingForOrg = true;
        _hasLoadedOnce = false;
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
        pageSize: _servedPageSize ?? pageSize,
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

  /// Holds one picked file and uploads it.
  ///
  /// The upload is its own step: the message binds the ids afterwards, so a file that is never
  /// sent stays unbound and the API sweeps it.
  Future<void> attach({
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) async {
    // The server refuses a sixth file when the send binds it (`ConversationService.cs:307-310`).
    // Refusing it here means nothing is uploaded that could never be bound, and the associate
    // is told why in the server's own words rather than by a generic send failure.
    if (_pendingAttachments.length >= maxAttachmentsPerMessage) {
      _actionError = attachmentCapMessage;
      _notify();
      return;
    }

    final pending = PendingThreadAttachment(
      localId: 'local_att_${++_attachmentCount}',
      fileName: fileName,
      contentType: contentType,
      bytes: bytes,
      width: width,
      height: height,
    )..uploading = true;

    _pendingAttachments[pending.localId] = pending;
    _actionError = null;
    _notify();

    await _upload(pending);
  }

  /// Tries a failed upload again, with the bytes the associate already picked.
  Future<void> retryAttachment(String localId) async {
    final pending = _pendingAttachments[localId];
    if (pending == null || !pending.isFailed) {
      return;
    }

    pending.error = null;
    pending.uploading = true;
    _notify();

    await _upload(pending);
  }

  /// Drops a held file. An upload already stored is left to the API's sweep, because nothing
  /// binds it.
  void removeAttachment(String localId) {
    if (_pendingAttachments.remove(localId) != null) {
      _notify();
    }
  }

  /// The stored bytes of an attachment, for the bubble's thumbnail.
  Future<Uint8List> loadAttachmentBytes(String attachmentId) =>
      _repository.fetchAttachmentBytes(conversation.id, attachmentId);

  Future<void> _upload(PendingThreadAttachment pending) async {
    try {
      final stored = await _repository.uploadAttachment(
        conversation.id,
        bytes: pending.bytes,
        contentType: pending.contentType,
        fileName: pending.fileName,
        width: pending.width,
        height: pending.height,
      );
      pending.attachmentId = stored.id;
      pending.error = null;
    } catch (error) {
      pending.attachmentId = null;
      // Named after the file: the tray draws this beside the retry control, and "Could not
      // upload." would not say which of several held files failed.
      pending.error = 'Could not upload ${pending.fileName}.';
    } finally {
      pending.uploading = false;
      _notify();
    }
  }

  /// Sends what the associate typed.
  Future<void> send(String text) async {
    final trimmed = text.trim();
    // A send waits for its uploads, and never goes out naming bytes the server has not stored.
    if (trimmed.isEmpty || _isSending || !areAttachmentsReady) {
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
      // One key per composed message, generated on the send and reused by every retry, so
      // a send that timed out after the row was stored comes back as that row rather than
      // as a second copy of the sentence.
      clientMessageId: uuidV4(),
    );
    _messages = List.unmodifiable([..._messages, local]);
    _attachmentIdsByLocalId[local.id] = [
      for (final attachment in _pendingAttachments.values)
        if (attachment.attachmentId != null) attachment.attachmentId!,
    ];
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
      // The server's own status is authoritative: a decision can settle on a status this
      // device did not guess, and the row is replaced with what was stored rather than
      // with what the optimistic update assumed.
      final decided = await _repository.decideSignOff(
        conversationId: conversation.id,
        message: original,
        approved: approved,
      );
      final at = _messages.indexWhere((item) => item.id == draft.id);
      if (at >= 0) {
        _replace(at, decided);
      }
      _actionError = null;
      _notify();
    } catch (error) {
      // Back where it was: a draft that quietly stopped waiting on the associate
      // would leave a client's question unanswered and nobody told.
      final at = _messages.indexWhere((item) => item.id == draft.id);
      if (at >= 0) {
        _replace(at, original);
      }
      _actionError = 'That decision could not be recorded.';
      _notify();
    }
  }

  /// Takes an approved SignOff back, returning it to the associate's queue.
  ///
  /// The server's answer is drawn rather than a guessed status: a revocation appends to the
  /// immutable decision log and puts the message back to `AwaitingSignOff`, which is what
  /// makes it decidable again and relights the inbox's `approval` marker.
  Future<void> revokeSignOff(ThreadMessage message) async {
    if (!message.isApprovedSignOff) {
      return;
    }

    _actionError = null;
    try {
      final revoked = await _repository.revokeSignOff(
        conversation.id,
        message.id,
      );
      final at = _messages.indexWhere((item) => item.id == message.id);
      if (at >= 0) {
        _replace(at, revoked);
      }
      _notify();
    } catch (error) {
      _actionError = 'That approval could not be taken back.';
      _notify();
    }
  }

  /// Binds this thread to the customer a `choice` block's option named.
  ///
  /// The whole thread is re-read afterwards rather than patched: binding a client changes
  /// the header's name and can unpin the thread, and both come from the server.
  Future<void> selectCustomer(String customerId, {String? query}) async {
    _actionError = null;
    try {
      await _repository.selectCustomer(
        conversation.id,
        customerId,
        query: query,
      );
      await load();
    } catch (error) {
      _actionError = 'That client could not be linked.';
      _notify();
    }
  }

  /// Takes a message the hub pushed while the thread is open.
  ///
  /// Four things can arrive, and each is reconciled rather than appended:
  ///
  /// - a row already on screen, whose status (a `Delivered`/`Read` tick) is replaced in place;
  /// - the echo of this device's own in-flight send, matched by `clientMessageId`, so the
  ///   optimistic row is replaced instead of appearing twice;
  /// - a message that belongs before the window, inserted in `(createdAt, id)` order;
  /// - anything else, appended.
  ///
  /// A `local_*` id never reaches here: the device's own ids are private to it, and a server
  /// event always carries a stored id.
  Future<void> receive(ThreadMessage message) async {
    if (message.id.isEmpty || message.id.startsWith('local_')) {
      return;
    }

    final known = _messages.indexWhere((item) => item.id == message.id);
    if (known >= 0) {
      _replace(
        known,
        _messages[known].copyWith(
          status: message.status,
          blocks: message.blocks,
          text: message.text,
        ),
      );
      _notify();
    } else {
      // The server echoes the key this device composed under, so the in-flight row is adopted
      // by the stored one rather than duplicated.
      final key = message.clientMessageId;
      final inFlight = key == null
          ? -1
          : _messages.indexWhere((item) => item.clientMessageId == key);
      if (inFlight >= 0) {
        _replace(inFlight, message);
        _notify();
      } else {
        final next = [..._messages, message]..sort(_byTimeThenId);
        _messages = List.unmodifiable(next);
        _notify();
      }
    }

    await _markReadIfNewest();
  }

  /// Tells the API how far this device has read, when the newest server message changed.
  ///
  /// Best-effort by contract: reading is not an action the associate took, so a refusal is
  /// not a toast. A `local_*` id is never sent, because the server has never seen it.
  Future<void> _markReadIfNewest() async {
    String? newest;
    for (final message in _messages) {
      if (message.id.isNotEmpty && !message.id.startsWith('local_')) {
        newest = message.id;
      }
    }
    if (newest == null || newest == _lastMarkedReadId) {
      return;
    }

    _lastMarkedReadId = newest;
    try {
      await _repository.markRead(conversation.id, newest);
    } catch (_) {
      // Deliberately swallowed: the marker is the API's bookkeeping, not a user action, and
      // the id is remembered so a refused write is not retried on every rebuild.
    }
  }

  /// The window's order: `(createdAt, id)`.
  ///
  /// The id breaks a tie so two messages written in the same millisecond keep one stable
  /// order, which is what stops a realtime insert from reshuffling the bubble above it.
  static int _byTimeThenId(ThreadMessage a, ThreadMessage b) {
    final byTime = a.createdAt.compareTo(b.createdAt);
    return byTime != 0 ? byTime : a.id.compareTo(b.id);
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
      final stored = await _repository.sendMessage(
        conversation.id,
        local.text,
        clientMessageId: local.clientMessageId,
        attachmentIds: _attachmentIdsByLocalId[local.id] ?? const [],
      );
      final at = _messages.indexWhere((item) => item.id == local.id);
      if (at >= 0) {
        _replace(at, stored);
      }
      // The tray is emptied only once the message that bound the files is stored.
      _pendingAttachments.clear();
      _attachmentIdsByLocalId.remove(local.id);
      _actionError = null;
      // The stored row is now the newest thing on screen, so the marker follows it.
      await _markReadIfNewest();
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

  /// What a failed read should say.
  ///
  /// The status decides the words, because "no access", "gone" and "the shop is not
  /// answering" call for different things from the associate. A status this build has not
  /// been taught keeps the transport's own text and prefers the server's `{message}` when
  /// the body carried one.
  String _describe(Object error) {
    if (error is DioException) {
      switch (error.response?.statusCode) {
        case 401:
          return 'Your session has expired. Sign in again to continue.';
        case 403:
          return 'You do not have access to this conversation.';
        case 404:
          return 'This conversation is no longer available.';
        case 409:
          return 'This message was already sent.';
        case 429:
          return 'Too many messages; try again shortly.';
      }

      final server = _serverMessage(error.response?.data);
      if (server != null) {
        return server;
      }
      if (error.response == null) {
        return 'The boutique is not answering. Check your connection and try again.';
      }
    }

    return error.toString().replaceFirst('Exception: ', '');
  }

  /// The `message` field of an error body, when the API sent one.
  static String? _serverMessage(Object? data) {
    if (data is Map && data['message'] is String) {
      final message = (data['message'] as String).trim();
      if (message.isNotEmpty) {
        return message;
      }
    }
    return null;
  }

  /// `notifyListeners` that tolerates a reply landing after the screen is gone.
  void _notify() {
    if (!_disposed) {
      notifyListeners();
    }
  }
}
