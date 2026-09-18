import 'package:dio/dio.dart';

import '../domain/conversation.dart';
import 'conversation_repository.dart';

/// The message inbox, over the Aveline API.
///
/// Follows `docs/api/openapi.yaml`:
/// `GET /api/v1/orgs/{organizationId}/conversations`, paged.
///
/// **What the endpoint carries today.** `ConversationDto` has the identity of a
/// thread - `id`, `kind`, `customerId`, `threadId`, `status`, `lastMessageAt` -
/// and nothing else, so rows drawn from the API can show a client's thread and
/// when it last moved, but not the client's name, a preview of the last message
/// or an unread count. `Conversation.fromJson` treats all three as optional, so
/// this repository needs no change when the backend adds them; until then the
/// rows lean on [Conversation.unnamedClientTitle] and read quietly.
///
/// The one thread the backend models today is the Salon, which
/// `Conversation.fromJson` classifies as Aveline's and the inbox pins.
class ApiConversationRepository implements ConversationRepository {
  ApiConversationRepository(
    this._dio, {
    required this.organizationId,
    this.pageSize = 50,
  });

  final Dio _dio;

  /// The shop whose inbox is being read. Every conversation is scoped to an org
  /// on the server, so the caller has to name one.
  final String organizationId;

  /// How many threads one read asks for.
  ///
  /// The inbox draws one unpaged column, so this is a ceiling rather than a page
  /// size: a boutique with more open threads than this needs the paging the
  /// endpoint already supports, and a footer that says how many are left.
  final int pageSize;

  @override
  Future<List<Conversation>> fetchConversations() async {
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations',
      queryParameters: {'page': 1, 'pageSize': pageSize},
    );

    final items = response.data?['items'];
    if (items is! List) {
      return const [];
    }
    return [
      for (final item in items)
        if (item is Map) Conversation.fromJson(Map<String, dynamic>.from(item)),
    ];
  }
}
