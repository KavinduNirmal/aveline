import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/network/org_context.dart';
import '../domain/thread_attachment.dart';
import '../domain/thread_message.dart';
import 'thread_repository.dart';

/// A client's thread, over the Aveline API.
///
/// Follows `docs/api/openapi.yaml`:
/// - `GET  .../conversations/{id}/messages`              paged, oldest first
/// - `POST .../conversations/{id}/messages`              send `{ text }`
/// - `POST .../conversations/{id}/messages/{mid}/sign-off`  `{ approved, contentHash }`
/// - `POST .../conversations/{id}/deliver`               `{ text }` to the client's channel
/// - `POST .../conversations/{id}/messages/{mid}/regenerate`  `202`, fresh reply over the hub
///
/// The auth interceptor on the shared [Dio] attaches the Clerk token, so nothing
/// here handles credentials.
class ApiThreadRepository implements ThreadRepository {
  ApiThreadRepository(this._dio, {required this.organizationId});

  final Dio _dio;

  /// The shop whose conversation is being read, read **at call time**.
  ///
  /// A callback rather than a value because the canonical id arrives from
  /// `GET /orgs/my` after the shell mounts: a repository built before it is known
  /// has to see the later value. A null or blank id is [OrgContextUnavailable] — a
  /// "not yet", which the controller keeps as a loading state — rather than an
  /// error card, which is reserved for the server's own refusal.
  final String? Function() organizationId;

  /// The active membership's org id, or a "not yet".
  String get _organizationId {
    final id = organizationId();
    if (id == null || id.isEmpty) {
      throw const OrgContextUnavailable();
    }
    return id;
  }

  /// Every route in this module takes `{conversationId:guid}` (and the sign-off
  /// route `{messageId:guid}`), so a non-UUID id would come back as an opaque 400
  /// from the route constraint with nothing in it an associate could act on.
  /// Refusing here names the offending id instead.
  static final RegExp _uuid = RegExp(
    r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
  );

  static void _requireUuid(String value, String name) {
    if (!_uuid.hasMatch(value)) {
      throw ArgumentError.value(value, name, 'must be a UUID');
    }
  }

  String _conversationPath(String conversationId) {
    _requireUuid(conversationId, 'conversationId');
    return '/api/v1/orgs/$_organizationId/conversations/$conversationId';
  }

  String _messagesPath(String conversationId) =>
      '${_conversationPath(conversationId)}/messages';

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    if (around != null) {
      _requireUuid(around, 'around');
    }

    final response = await _dio.get<Map<String, dynamic>>(
      _messagesPath(conversationId),
      queryParameters: {
        'page': page,
        'pageSize': pageSize,
        'around': ?around,
      },
    );
    return ThreadPage.fromJson(response.data ?? const {});
  }

  /// A small in-memory cache, keyed by attachment id.
  ///
  /// A thumbnail is asked for once per scroll, and the bytes never change: the row is immutable
  /// once written. Bounded by the conversation's own attachments, which is small enough.
  final Map<String, Uint8List> _attachmentCache = {};

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      _messagesPath(conversationId),
      data: {
        'text': text,
        'clientMessageId': ?clientMessageId,
        if (attachmentIds.isNotEmpty) 'attachmentIds': attachmentIds,
      },
    );
    return ThreadMessage.fromJson(response.data ?? const {});
  }

  @override
  Future<ThreadAttachment> uploadAttachment(
    String conversationId, {
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) async {
    _requireUuid(conversationId, 'conversationId');
    // Sent as a `data:` URL so the server sees the type the picker reported rather than
    // guessing from the file name.
    final response = await _dio.post<Map<String, dynamic>>(
      '${_conversationPath(conversationId)}/attachments',
      data: {
        'imageData': 'data:$contentType;base64,${base64Encode(bytes)}',
        'fileName': fileName,
        'width': ?width,
        'height': ?height,
      },
    );
    return ThreadAttachment.fromJson(response.data ?? const {});
  }

  @override
  Future<Uint8List> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  ) async {
    _requireUuid(attachmentId, 'attachmentId');

    final cached = _attachmentCache[attachmentId];
    if (cached != null) {
      return cached;
    }

    final response = await _dio.get<List<int>>(
      '${_conversationPath(conversationId)}/attachments/$attachmentId',
      options: Options(responseType: ResponseType.bytes),
    );
    final bytes = Uint8List.fromList(response.data ?? const []);
    _attachmentCache[attachmentId] = bytes;
    return bytes;
  }

  @override
  Future<ThreadMessage> decideSignOff({
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

    _requireUuid(message.id, 'messageId');

    final response = await _dio.post<Map<String, dynamic>>(
      '${_messagesPath(conversationId)}/${message.id}/sign-off',
      data: {'approved': approved, 'contentHash': hash},
    );
    return ThreadMessage.fromJson(response.data ?? const {});
  }

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) async {
    _requireUuid(customerId, 'customerId');
    await _dio.post<void>(
      '/api/v1/orgs/$_organizationId/conversations/$conversationId/select-customer',
      data: {
        'customerId': customerId,
        if (query != null && query.isNotEmpty) 'query': query,
      },
    );
  }

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) async {
    _requireUuid(messageId, 'messageId');
    final response = await _dio.post<Map<String, dynamic>>(
      '${_messagesPath(conversationId)}/$messageId/sign-off/revoke',
      data: {
        if (reason != null && reason.isNotEmpty) 'reason': reason,
      },
    );
    return ThreadMessage.fromJson(response.data ?? const {});
  }

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) async {
    _requireUuid(lastReadMessageId, 'lastReadMessageId');
    await _dio.patch<void>(
      '${_messagesPath(conversationId)}/read',
      data: {'lastReadMessageId': lastReadMessageId},
    );
  }

  @override
  Future<ThreadDelivery> deliver(
    String conversationId,
    String text, {
    String? clientMessageId,
  }) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>(
        '${_conversationPath(conversationId)}/deliver',
        data: {'text': text, 'clientMessageId': ?clientMessageId},
      );
      return ThreadDelivery.fromJson(response.data ?? const {});
    } on DioException catch (error) {
      // A refusal is the server declining on purpose, not the transport failing: the
      // body names the reason. It is thrown as its own type so the caller can turn each
      // code into a sentence instead of reading a raw status off a `DioException`.
      final refusal = _refusalOf(error.response?.data);
      if (refusal != null) {
        throw DeliveryRefused(refusal, detail: _detailOf(error.response?.data));
      }
      rethrow;
    }
  }

  @override
  Future<void> regenerate(String conversationId, String messageId) async {
    _requireUuid(messageId, 'messageId');
    await _dio.post<void>('${_messagesPath(conversationId)}/$messageId/regenerate');
  }

  /// The `refusal` code in an error body, when the API sent one.
  static String? _refusalOf(Object? data) {
    if (data is Map && data['refusal'] is String) {
      final refusal = (data['refusal'] as String).trim();
      if (refusal.isNotEmpty) {
        return refusal;
      }
    }
    return null;
  }

  /// The `detail` sentence in an error body, when the API sent one.
  static String? _detailOf(Object? data) {
    if (data is Map && data['detail'] is String) {
      final detail = (data['detail'] as String).trim();
      if (detail.isNotEmpty) {
        return detail;
      }
    }
    return null;
  }
}
