import 'package:dio/dio.dart';

import 'conversation_repository.dart';

/// The message inbox, over the Aveline API.
///
/// Follows `docs/api/openapi.yaml`:
/// `GET /api/v1/orgs/{organizationId}/conversations`, paged.
///
/// **The organization id is read at call time, not captured.** It arrives from
/// `GET /orgs/my` after the shell mounts, so the constructor takes a callback
/// (the same shape `ApiHomeRepository` uses) and a null id raises
/// [OrgContextUnavailable] - a "not yet" the screen keeps as a loading state.
/// The id is always the active membership's, never the JWT's `org_id` claim,
/// which can be stale; the server's authorization handler deliberately ignores
/// the claim and reads the route value instead.
///
/// **What the endpoint carries.** The list row is complete: the client's name, a
/// block-aware preview of the newest message, the block it came from (the row's
/// category), who spoke last, the agent persona and the actionable marker set.
/// `Conversation.fromJson` treats every one of them as optional, so a payload
/// that predates them still draws.
class ApiConversationRepository implements ConversationRepository {
  ApiConversationRepository(
    this._dio, {
    required this.organizationId,
    this.pageSize = 50,
  });

  final Dio _dio;

  /// The shop whose inbox is being read, resolved at call time. Every
  /// conversation is scoped to an org on the server, so the caller has to name
  /// one; until the id is known there is nothing to call.
  final String? Function() organizationId;

  /// How many threads one read asks for.
  ///
  /// The inbox pages with load-more: the envelope's `total` is what tells the
  /// controller whether another page exists.
  final int pageSize;

  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async {
    final organizationId = this.organizationId();
    if (organizationId == null || organizationId.isEmpty) {
      throw const OrgContextUnavailable();
    }

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations',
      queryParameters: {'page': page, 'pageSize': pageSize},
    );

    return ConversationPage.fromJson(response.data ?? const {});
  }
}
