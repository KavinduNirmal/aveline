import 'package:dio/dio.dart';

import '../domain/thread_message.dart';
import 'thread_repository.dart';

/// A client's thread, over the Aveline API.
///
/// Follows `docs/api/openapi.yaml`:
/// - `GET  .../conversations/{id}/messages`              paged, oldest first
/// - `POST .../conversations/{id}/messages`              send `{ text }`
/// - `POST .../conversations/{id}/messages/{mid}/sign-off`  `{ approved, contentHash }`
///
/// The auth interceptor on the shared [Dio] attaches the Clerk token, so nothing
/// here handles credentials.
class ApiThreadRepository implements ThreadRepository {
  ApiThreadRepository(this._dio, {required this.organizationId});

  final Dio _dio;

  /// The shop whose conversation is being read. Every conversation is scoped to
  /// an org on the server, so the caller has to name one.
  final String organizationId;

  String _messagesPath(String conversationId) =>
      '/api/v1/orgs/$organizationId/conversations/$conversationId/messages';

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
  }) async {
    final response = await _dio.get<Map<String, dynamic>>(
      _messagesPath(conversationId),
      queryParameters: {'page': page, 'pageSize': pageSize},
    );
    return ThreadPage.fromJson(response.data ?? const {});
  }

  @override
  Future<ThreadMessage> sendMessage(String conversationId, String text) async {
    final response = await _dio.post<Map<String, dynamic>>(
      _messagesPath(conversationId),
      data: {'text': text},
    );
    return ThreadMessage.fromJson(response.data ?? const {});
  }

  @override
  Future<void> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async {
    final hash = message.contentHash;
    if (hash == null || hash.isEmpty) {
      // The API binds a decision to the hash of the content the approver was
      // shown, and rejects a mismatch. Sending a blank hash would come back as a
      // 400 nobody can act on, so it is refused here with something that says why.
      throw StateError(
        'A sign-off decision needs the content hash of the message it decides.',
      );
    }

    await _dio.post<void>(
      '${_messagesPath(conversationId)}/${message.id}/sign-off',
      data: {'approved': approved, 'contentHash': hash},
    );
  }
}
