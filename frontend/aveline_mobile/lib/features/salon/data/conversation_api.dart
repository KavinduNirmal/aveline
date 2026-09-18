import 'dart:math' as math;

import 'package:dio/dio.dart';

import '../domain/salon_message.dart';

/// A conversation (Salon) as returned by the API. Only the fields needed to open the
/// realtime Salon are modelled here.
class Conversation {
  const Conversation({required this.id, required this.threadId});

  final String id;
  final String threadId;

  factory Conversation.fromJson(Map<String, dynamic> json) {
    return Conversation(
      id: json['id'] as String? ?? '',
      threadId: json['threadId'] as String? ?? '',
    );
  }
}

/// Minimal conversations API client for the Salon. Gets-or-creates the Aveline Salon so the
/// app can join its SignalR group, and sends staff notes that trigger the agent.
class ConversationApi {
  ConversationApi(this._dio);

  final Dio _dio;

  /// Gets-or-creates the Aveline Salon (customerId = null) for [organizationId].
  Future<Conversation> getOrCreateAvelineSalon(String organizationId) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations',
      data: {'customerId': null},
    );
    return Conversation.fromJson(response.data ?? const {});
  }

  /// Sends a staff note in [conversationId] and triggers the agent. Returns the confirmed
  /// message (real id + timestamp).
  Future<SalonMessage> sendMessage({
    required String organizationId,
    required String conversationId,
    required String text,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId/messages',
      data: {'text': text},
    );
    return SalonMessage.fromJson(response.data ?? const {});
  }

  /// Fetches the persisted messages of [conversationId] (newest page, ascending) so the Salon
  /// thread survives a refresh/restart. History is served oldest-first, so page 1 holds the
  /// OLDEST [pageSize] messages; once the Salon grows past a page we load the last (newest) page.
  Future<List<SalonMessage>> fetchMessages({
    required String organizationId,
    required String conversationId,
    int pageSize = 100,
  }) async {
    final pageOne = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId/messages',
      queryParameters: {'page': 1, 'pageSize': pageSize},
    );
    final data = pageOne.data ?? const {};
    final items = (data['items'] as List?) ?? const [];
    final total = (data['total'] as num?)?.toInt() ?? 0;
    final serverPageSize = (data['pageSize'] as num?)?.toInt() ?? pageSize;

    if (total > items.length && serverPageSize > 0) {
      final lastPage = math.max(1, (total / serverPageSize).ceil());
      final newest = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$organizationId/conversations/$conversationId/messages',
        queryParameters: {'page': lastPage, 'pageSize': serverPageSize},
      );
      final newestItems = (newest.data?['items'] as List?) ?? const [];
      return newestItems
          .whereType<Map>()
          .map((m) => SalonMessage.fromJson(Map<String, dynamic>.from(m)))
          .toList();
    }

    return items
        .whereType<Map>()
        .map((m) => SalonMessage.fromJson(Map<String, dynamic>.from(m)))
        .toList();
  }

  /// Binds the Salon to a customer chosen from a resolution `choice` block and re-triggers
  /// the agent with that customer in context (Issue #161). [query] is the original staff text
  /// that triggered the lookup, when available.
  Future<Conversation> selectCustomer({
    required String organizationId,
    required String conversationId,
    required String customerId,
    String? query,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId/select-customer',
      data: {'customerId': customerId, 'query': ?query},
    );
    return Conversation.fromJson(response.data ?? const {});
  }
}
