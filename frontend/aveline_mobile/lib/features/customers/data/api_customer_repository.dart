import 'package:dio/dio.dart';

import '../domain/customer.dart';
import '../domain/customer_book.dart';
import '../domain/customer_detail.dart';
import '../domain/customer_level.dart';
import 'customer_repository.dart';

/// Customer repository backed by the Aveline .NET Backend API.
///
/// Follows `docs/api/openapi.yaml` and `CustomerTenantEndpoints.cs`:
/// - `GET /api/v1/orgs/{organizationId}/customers` (the book)
/// - `GET /api/v1/orgs/{organizationId}/customers/{customerId}` (detail profile)
/// - `GET /api/v1/orgs/{organizationId}/customers/{customerId}/consent` (consent)
/// - `GET /api/v1/orgs/{organizationId}/customers/{customerId}/memories` (saved memories)
/// - `GET /api/v1/orgs/{organizationId}/customers/{customerId}/events` (occasions)
/// - `GET /api/v1/orgs/{organizationId}/customers/{customerId}/interactions` (interactions history)
/// - `POST /api/v1/orgs/{organizationId}/customers/{customerId}/status` (tier recompute)
class ApiCustomerRepository implements CustomerRepository {
  ApiCustomerRepository(
    this._dio, {
    required this.organizationId,
    this.pageSize = 200,
  });

  final Dio _dio;
  final String? Function() organizationId;
  final int pageSize;

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      return CustomerBook.empty;
    }

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/customers',
      queryParameters: {
        'page': 1,
        'pageSize': pageSize,
        if (query.search.trim().isNotEmpty) 'search': query.search.trim(),
        if (query.level != null) 'level': _levelWire(query.level!),
      },
    );

    final data = response.data;
    if (data == null) {
      return CustomerBook.empty;
    }

    final rawItems = data['items'];
    if (rawItems is! List) {
      return CustomerBook.empty;
    }

    final customers = <Customer>[];
    for (final raw in rawItems) {
      if (raw is Map<String, dynamic>) {
        final customer = _toCustomer(raw, orgId);
        if (customer != null) {
          customers.add(customer);
        }
      }
    }

    return _section(customers);
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      return null;
    }

    try {
      final detailResponse = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/customers/$id',
      );
      final detailData = detailResponse.data;
      if (detailData == null) {
        return null;
      }

      final customer = _toCustomerFromDetail(detailData, orgId);

      // The sub-resources are separate reads, so they run together; each one
      // degrades to its empty value rather than failing the whole profile.
      final results = await Future.wait([
        _fetchConsent(orgId, id),
        _fetchMemories(orgId, id),
        _fetchEvents(orgId, id),
        _fetchInteractions(orgId, id),
      ]);

      return CustomerDetail(
        customer: customer,
        consent: results[0] as CustomerConsent,
        memories: results[1] as List<CustomerMemory>,
        events: results[2] as List<CustomerEvent>,
        interactions: results[3] as List<CustomerInteraction>,
        preferences: _parsePreferences(detailData['preferences']),
      );
    } on DioException catch (error) {
      if (error.response?.statusCode == 404) {
        return null;
      }
      rethrow;
    }
  }

  /// Records an in-person walk-in visit at the counter.
  Future<void> recordVisit(String customerId) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      return;
    }

    await _dio.post(
      '/api/v1/orgs/$orgId/customers/$customerId/interactions',
      data: {
        'occurredAtUtc': DateTime.now().toUtc().toIso8601String(),
        'channel': 'in_person',
        'direction': 'inbound',
        'note': 'Walked in; logged at the counter.',
        'purchaseTotal': null,
      },
      options: Options(
        headers: {
          'Idempotency-Key': 'visit-${DateTime.now().microsecondsSinceEpoch}',
        },
      ),
    );
  }

  /// Recomputes the client's loyalty tier from spend, visits, and recency.
  Future<String?> recomputeTier(String customerId) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      return null;
    }

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/customers/$customerId/status',
      data: const <String, dynamic>{},
    );
    return response.data?['status'] as String?;
  }

  /// Reads the consent row, falling back to `unknown` when the client has none
  /// or the read fails.
  Future<CustomerConsent> _fetchConsent(String orgId, String id) async {
    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/customers/$id/consent',
      );
      final data = response.data;
      if (data != null) {
        return _toConsent(data);
      }
    } catch (_) {
      // Tolerates an absent consent row or a transient read failure.
    }
    return CustomerConsent.unknown;
  }

  /// Reads the saved memories, newest first.
  Future<List<CustomerMemory>> _fetchMemories(String orgId, String id) async {
    try {
      final response = await _dio.get<dynamic>(
        '/api/v1/orgs/$orgId/customers/$id/memories',
      );
      final data = response.data;
      if (data is List) {
        return data
            .whereType<Map<String, dynamic>>()
            .map(_toMemory)
            .toList();
      }
    } catch (_) {
      // Tolerates an absent memory list or a transient read failure.
    }
    return const [];
  }

  /// Reads the client's occasions, which is a bare array on the wire.
  Future<List<CustomerEvent>> _fetchEvents(String orgId, String id) async {
    try {
      final response = await _dio.get<dynamic>(
        '/api/v1/orgs/$orgId/customers/$id/events',
      );
      final data = response.data;
      if (data is List) {
        return data
            .whereType<Map<String, dynamic>>()
            .map(_toEvent)
            .whereType<CustomerEvent>()
            .toList();
      }
    } catch (_) {
      // Tolerates an absent event list or a transient read failure.
    }
    return const [];
  }

  /// Reads the interaction history, which is paged on the wire.
  Future<List<CustomerInteraction>> _fetchInteractions(
    String orgId,
    String id,
  ) async {
    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/customers/$id/interactions',
        queryParameters: {'page': 1, 'pageSize': 50},
      );
      final rawItems = response.data?['items'];
      if (rawItems is List) {
        return rawItems
            .whereType<Map<String, dynamic>>()
            .map(_toInteraction)
            .whereType<CustomerInteraction>()
            .toList();
      }
    } catch (_) {
      // Tolerates an absent interaction history or a transient read failure.
    }
    return const [];
  }

  CustomerMemory _toMemory(Map<String, dynamic> raw) {
    return CustomerMemory(
      id: raw['id'] as String? ?? '',
      content: raw['content'] as String? ?? '',
      category: MemoryCategory.parse(raw['category'] as String?),
      source: MemorySource.parse(raw['source'] as String?),
      createdAtUtc: _parseUtc(raw['createdAtUtc']) ?? DateTime.now().toUtc(),
      isExplicit: raw['isExplicit'] as bool? ?? false,
      confidence: (raw['confidence'] as num?)?.toDouble() ?? 1.0,
    );
  }

  /// Returns `null` for an event the API gave no usable date for: an occasion
  /// that cannot be placed on the calendar is not one the screen can show.
  CustomerEvent? _toEvent(Map<String, dynamic> raw) {
    final date = _parseUtc(raw['eventDate']);
    if (date == null) {
      return null;
    }

    return CustomerEvent(
      id: raw['id'] as String? ?? '',
      type: CustomerEventType.parse(raw['eventType'] as String?),
      dateUtc: date,
      description: raw['description'] as String?,
      isActive: raw['isActive'] as bool? ?? true,
    );
  }

  List<CustomerPreference> _parsePreferences(Object? raw) {
    if (raw is! List) {
      return const [];
    }

    return raw
        .whereType<Map<String, dynamic>>()
        .map(
          (item) => CustomerPreference(
            id: item['id'] as String? ?? '',
            key: item['preferenceKey'] as String? ?? '',
            value: item['preferenceValue'] as String? ?? '',
            isExplicit: item['isExplicit'] as bool? ?? true,
            confidence: (item['confidence'] as num?)?.toDouble() ?? 1.0,
          ),
        )
        .toList();
  }

  Customer? _toCustomer(Map<String, dynamic> raw, String orgId) {
    final id = raw['customerId'];
    if (id is! String || id.isEmpty) {
      return null;
    }

    return Customer(
      id: id,
      organizationId: orgId,
      phoneNumber: (raw['phoneNumber'] as String?) ?? '',
      level: _levelFromWire(raw['level'] as String?) ?? CustomerLevel.level1,
      status: CustomerStatus.parse(raw['status'] as String?),
      fullName: raw['fullName'] as String?,
      nickname: raw['nickname'] as String?,
      totalSpent: _decimal(raw['totalSpent']),
      visitCount: (raw['visitCount'] as num?)?.toInt() ?? 0,
      lastVisitAtUtc: _parseUtc(raw['lastVisitAtUtc']),
    );
  }

  Customer _toCustomerFromDetail(Map<String, dynamic> raw, String orgId) {
    final id = raw['customerId'] as String? ?? '';
    final tagsRaw = raw['tags'];
    final tags = tagsRaw is List
        ? tagsRaw.map((t) => t.toString()).toSet()
        : const <String>{};

    return Customer(
      id: id,
      organizationId: orgId,
      phoneNumber: (raw['phoneNumber'] as String?) ?? '',
      level: _levelFromWire(raw['level'] as String?) ?? CustomerLevel.level1,
      status: CustomerStatus.parse(raw['status'] as String?),
      fullName: raw['fullName'] as String?,
      nickname: raw['nickname'] as String?,
      email: raw['email'] as String?,
      totalSpent: _decimal(raw['totalSpent']),
      visitCount: (raw['visitCount'] as num?)?.toInt() ?? 0,
      lastVisitAtUtc: _parseUtc(raw['lastVisitAtUtc']),
      createdAtUtc: _parseUtc(raw['createdAtUtc']),
      tags: tags,
    );
  }

  /// `TenantCustomerConsentDto` carries both the record's own `Status`/`GrantedAtUtc`
  /// and its `ConsentStatus`/`ConsentGrantedAt` aliases, so either spelling is read.
  CustomerConsent _toConsent(Map<String, dynamic> raw) {
    final status = raw['status'] ?? raw['consentStatus'];
    return CustomerConsent(
      status: ConsentStatus.parse(status is String ? status : null),
      grantedAtUtc: _parseUtc(raw['grantedAtUtc'] ?? raw['consentGrantedAt']),
      revokedAtUtc: _parseUtc(raw['revokedAtUtc'] ?? raw['consentRevokedAt']),
    );
  }

  CustomerInteraction? _toInteraction(Map<String, dynamic> raw) {
    final id = raw['interactionId'];
    if (id is! String || id.isEmpty) {
      return null;
    }

    return CustomerInteraction(
      id: id,
      channel: InteractionChannel.parse(raw['channel'] as String?),
      direction: InteractionDirection.parse(raw['direction'] as String?),
      createdAtUtc: _parseUtc(raw['occurredAtUtc']) ?? DateTime.now().toUtc(),
      messageContent: (raw['note'] as String?) ?? (raw['messageContent'] as String?),
    );
  }

  static CustomerBook _section(List<Customer> customers) {
    final byLetter = <String, List<Customer>>{};
    for (final customer in customers) {
      byLetter.putIfAbsent(customer.sectionLetter, () => []).add(customer);
    }

    final letters = byLetter.keys.toList()
      ..sort((a, b) {
        if (a == '#') return 1;
        if (b == '#') return -1;
        return a.compareTo(b);
      });

    return CustomerBook([
      for (final letter in letters)
        CustomerSection(
          letter: letter,
          customers: byLetter[letter]!
            ..sort(
              (a, b) => a.displayName.toLowerCase().compareTo(
                    b.displayName.toLowerCase(),
                  ),
            ),
        ),
    ]);
  }

  static String _levelWire(CustomerLevel level) => level.name;

  static CustomerLevel? _levelFromWire(String? value) {
    if (value == null) {
      return null;
    }
    for (final level in CustomerLevel.values) {
      if (level.name.toLowerCase() == value.toLowerCase() ||
          level.label.toLowerCase() == value.toLowerCase()) {
        return level;
      }
    }
    return null;
  }

  static double _decimal(Object? value) =>
      value is num ? value.toDouble() : 0.0;

  static DateTime? _parseUtc(Object? value) =>
      value is String ? DateTime.tryParse(value)?.toUtc() : null;
}
