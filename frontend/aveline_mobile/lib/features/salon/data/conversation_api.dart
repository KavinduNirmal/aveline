import 'dart:convert';
import 'dart:math' as math;
import 'dart:typed_data';

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

  /// Bytes already fetched, keyed by attachment id. An attachment row is immutable once
  /// written, so a thumbnail that scrolls out of view and back must not refetch.
  final Map<String, Uint8List> _attachmentCache = {};

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
  ///
  /// [attachmentIds] are ids the upload route already stored, bound to this message. The
  /// Salon picks and uploads before it sends, the same way a client thread does, so a file
  /// that is never sent stays unbound and the API sweeps it.
  Future<SalonMessage> sendMessage({
    required String organizationId,
    required String conversationId,
    required String text,
    List<String> attachmentIds = const [],
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId/messages',
      data: {
        'text': text,
        if (attachmentIds.isNotEmpty) 'attachmentIds': attachmentIds,
      },
    );
    return SalonMessage.fromJson(response.data ?? const {});
  }

  /// Stores one picked file against [conversationId] and returns its id.
  ///
  /// Sent as a `data:` URL so the server sees the type the picker reported rather than
  /// guessing from the file name. The upload is its own step: the message binds the ids
  /// afterwards, so a file the associate removes before sending is never bound.
  Future<String> uploadAttachment({
    required String organizationId,
    required String conversationId,
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId/attachments',
      data: {
        'imageData': 'data:$contentType;base64,${base64Encode(bytes)}',
        'fileName': fileName,
        'width': ?width,
        'height': ?height,
      },
    );
    return response.data?['attachmentId']?.toString() ?? '';
  }

  /// One attachment's bytes, read through the authenticated client.
  ///
  /// An `<Image.network>` of the stored route cannot work: an image element cannot carry the
  /// bearer token. The bytes are cached for the life of this client, because an attachment row
  /// is immutable once written and scrolling the Salon must not refetch it. The route is built
  /// from the ids rather than read from the block's stored `url`, the same way
  /// `ApiThreadRepository.fetchAttachmentBytes` does it, so both threads hit one endpoint.
  Future<Uint8List> fetchAttachmentBytes({
    required String organizationId,
    required String conversationId,
    required String attachmentId,
  }) async {
    final cached = _attachmentCache[attachmentId];
    if (cached != null) {
      return cached;
    }

    final response = await _dio.get<List<int>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId/attachments/$attachmentId',
      options: Options(responseType: ResponseType.bytes),
    );
    final bytes = Uint8List.fromList(response.data ?? const []);
    _attachmentCache[attachmentId] = bytes;
    return bytes;
  }

  /// Records the associate's decision on a staged reply.
  ///
  /// The decision is bound to the hash of the content the approver was shown: the API
  /// rejects a mismatch, so the hash travels with the message rather than being
  /// re-derived here. Returns the decided message, which is what the thread draws in
  /// place of the one that was waiting.
  Future<SalonMessage> decideSignOff({
    required String organizationId,
    required String conversationId,
    required String messageId,
    required String contentHash,
    required bool approved,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$organizationId/conversations/$conversationId'
      '/messages/$messageId/sign-off',
      data: {'approved': approved, 'contentHash': contentHash},
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
