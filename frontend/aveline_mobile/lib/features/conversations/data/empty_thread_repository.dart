import 'dart:typed_data';

import '../domain/thread_attachment.dart';
import '../domain/thread_message.dart';
import 'thread_repository.dart';

/// A thread with nothing in it, used wherever no real source has been injected.
///
/// The inbox used to fall back to `DemoThreadRepository`, which put a seeded
/// exchange on a production path: the drawer builds `const ConversationsScreen()`
/// with no way to inject anything, so tapping a client row rendered eight
/// invented messages. This repository is the honest stand-in — no history, and a
/// refusal that says why — so the screen draws its real empty state instead of
/// fiction.
///
/// A send or a decision is a programming error on this path rather than a user
/// action: nothing can be written into a thread that has no source. The failure
/// is still returned as a readable [StateError] so a caller that reaches it sees
/// why rather than a null.
class EmptyThreadRepository implements ThreadRepository {
  const EmptyThreadRepository();

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async => ThreadPage(
    items: const [],
    total: 0,
    page: page,
    pageSize: 0,
  );

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) async => throw StateError(
    'There is no thread source, so nothing can be sent.',
  );

  @override
  Future<ThreadAttachment> uploadAttachment(
    String conversationId, {
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) async => throw StateError(
    'There is no thread source, so nothing can be attached.',
  );

  @override
  Future<Uint8List> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  ) async => throw StateError(
    'There is no thread source, so no attachment can be read.',
  );

  @override
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async => throw StateError(
    'There is no thread source, so nothing can be decided.',
  );

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) async => throw StateError(
    'There is no thread source, so no client can be resolved.',
  );

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) async => throw StateError(
    'There is no thread source, so no approval can be taken back.',
  );

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) async {
    // Nothing to mark: this repository serves no history, so there is no source to tell.
  }
}
