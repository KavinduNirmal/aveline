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
}
