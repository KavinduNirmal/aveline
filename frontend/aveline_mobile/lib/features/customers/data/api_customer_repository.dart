import 'package:dio/dio.dart';

import '../domain/customer.dart';
import '../domain/customer_book.dart';
import '../domain/customer_detail.dart';
import '../domain/customer_level.dart';
import 'customer_repository.dart';

/// Real API implementation of [CustomerRepository] connecting to
/// `/api/v1/orgs/{organizationId}/customers`.
class ApiCustomerRepository implements CustomerRepository {
  ApiCustomerRepository(
    this._dio, {
    this.organizationId,
    this.organizationIdProvider,
  });

  final Dio _dio;
  final String? organizationId;
  final String? Function()? organizationIdProvider;

  String get _activeOrgId {
    final id = organizationId ?? organizationIdProvider?.call();
    if (id == null || id.isEmpty) {
      throw StateError('Cannot make customer calls without an active organization.');
    }
    return id;
  }

  /// Fetches the entire filtered customer book in one call so the alphabet index
  /// can offer every letter currently in scope.
  @override
  Future<CustomerBook> fetchBook({CustomerQuery query = const CustomerQuery()}) async {
    final response = await _dio.get(
      '/api/v1/orgs/$_activeOrgId/customers',
      queryParameters: {
        'page': 1,
        'pageSize': 200,
        if (query.search.trim().isNotEmpty) 'search': query.search.trim(),
        if (query.level != null) 'level': query.level!.name,
      },
    );

    final items = _items(response.data);
    final customers = items
        .map(_toCustomer)
        .whereType<Customer>()
        .toList()
      ..sort((a, b) => a.sectionLetter.compareTo(b.sectionLetter));

    return CustomerBook(_sections(customers));
  }

  /// Fetches one client's profile along with their consent, memories, events, and interactions.
  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    try {
      final detailResp = await _dio.get(
        '/api/v1/orgs/$_activeOrgId/customers/$id',
      );
      if (detailResp.statusCode != 200 || detailResp.data is! Map) {
        return null;
      }
      final data = detailResp.data as Map;
      final customer = _toCustomer(data);
      if (customer == null) {
        return null;
      }

      // Concurrently fetch sub-resources with resilient fallbacks
      final consentFuture = _fetchConsent(id);
      final memoriesFuture = _fetchMemories(id);
      final eventsFuture = _fetchEvents(id);
      final interactionsFuture = _fetchInteractions(id);

      final results = await Future.wait([
        consentFuture,
        memoriesFuture,
        eventsFuture,
        interactionsFuture,
      ]);

      final consent = results[0] as CustomerConsent;
      final memories = results[1] as List<CustomerMemory>;
      final events = results[2] as List<CustomerEvent>;
      final interactions = results[3] as List<CustomerInteraction>;

      final preferences = _parsePreferences(data['preferences']);

      return CustomerDetail(
        customer: customer,
        preferences: preferences,
        consent: consent,
        memories: memories,
        events: events,
        interactions: interactions,
      );
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) {
        return null;
      }
      rethrow;
    }
  }

  /// Records an in-person walk-in visit at the counter.
  Future<void> recordVisit(String customerId) async {
    await _dio.post(
      '/api/v1/orgs/$_activeOrgId/customers/$customerId/interactions',
      data: {
        'occurredAtUtc': DateTime.now().toUtc().toIso8601String(),
        'channel': 'in_person',
        'direction': 'inbound',
        'note': 'Walked in; logged at the counter.',
        'purchaseTotal': null,
      },
      options: Options(
        headers: {'Idempotency-Key': 'visit-${DateTime.now().microsecondsSinceEpoch}'},
      ),
    );
  }

  /// Recomputes the client's loyalty tier from spend, visits, and recency.
  Future<String?> recomputeTier(String customerId) async {
    final resp = await _dio.post(
      '/api/v1/orgs/$_activeOrgId/customers/$customerId/status',
      data: {},
    );
    if (resp.statusCode == 200 && resp.data is Map) {
      return (resp.data as Map)['status'] as String?;
    }
    return null;
  }

  Future<CustomerConsent> _fetchConsent(String id) async {
    try {
      final resp = await _dio.get('/api/v1/orgs/$_activeOrgId/customers/$id/consent');
      if (resp.statusCode == 200 && resp.data is Map) {
        final map = resp.data as Map;
        return CustomerConsent(
          status: ConsentStatus.parse(map['status'] as String?),
          grantedAtUtc: _parseUtc(map['grantedAtUtc']),
          revokedAtUtc: _parseUtc(map['revokedAtUtc']),
        );
      }
    } catch (_) {}
    return CustomerConsent.unknown;
  }

  Future<List<CustomerMemory>> _fetchMemories(String id) async {
    try {
      final resp = await _dio.get('/api/v1/orgs/$_activeOrgId/customers/$id/memories');
      if (resp.statusCode == 200 && resp.data is List) {
        final list = resp.data as List;
        return list
            .map((item) {
              if (item is! Map) return null;
              return CustomerMemory(
                id: item['id'] as String? ?? '',
                content: item['content'] as String? ?? '',
                category: MemoryCategory.parse(item['category'] as String?),
                source: MemorySource.parse(item['source'] as String?),
                isExplicit: item['isExplicit'] as bool? ?? false,
                confidence: (item['confidence'] as num?)?.toDouble() ?? 1.0,
                createdAtUtc: _parseUtc(item['createdAtUtc']) ?? DateTime.now().toUtc(),
              );
            })
            .whereType<CustomerMemory>()
            .toList();
      }
    } catch (_) {}
    return const [];
  }

  Future<List<CustomerEvent>> _fetchEvents(String id) async {
    try {
      final resp = await _dio.get('/api/v1/orgs/$_activeOrgId/customers/$id/events');
      if (resp.statusCode == 200 && resp.data is List) {
        final list = resp.data as List;
        return list
            .map((item) {
              if (item is! Map) return null;
              final dateStr = item['eventDate'] as String?;
              final date = dateStr != null ? DateTime.tryParse(dateStr)?.toUtc() : null;
              if (date == null) return null;
              return CustomerEvent(
                id: item['id'] as String? ?? '',
                type: CustomerEventType.parse(item['eventType'] as String?),
                dateUtc: date,
                description: item['description'] as String?,
                isActive: item['isActive'] as bool? ?? true,
              );
            })
            .whereType<CustomerEvent>()
            .toList();
      }
    } catch (_) {}
    return const [];
  }

  Future<List<CustomerInteraction>> _fetchInteractions(String id) async {
    try {
      final resp = await _dio.get('/api/v1/orgs/$_activeOrgId/customers/$id/interactions');
      if (resp.statusCode == 200 && resp.data is Map) {
        final items = (resp.data as Map)['items'];
        if (items is List) {
          return items
              .map((item) {
                if (item is! Map) return null;
                return CustomerInteraction(
                  id: item['interactionId'] as String? ?? '',
                  channel: InteractionChannel.parse(item['channel'] as String?),
                  direction: InteractionDirection.parse(item['direction'] as String?),
                  createdAtUtc: _parseUtc(item['occurredAtUtc']) ?? DateTime.now().toUtc(),
                  messageContent: item['note'] as String?,
                );
              })
              .whereType<CustomerInteraction>()
              .toList();
        }
      }
    } catch (_) {}
    return const [];
  }

  List<CustomerPreference> _parsePreferences(Object? raw) {
    if (raw is! List) return const [];
    return raw
        .map((item) {
          if (item is! Map) return null;
          return CustomerPreference(
            id: item['id'] as String? ?? '',
            key: item['preferenceKey'] as String? ?? '',
            value: item['preferenceValue'] as String? ?? '',
            isExplicit: item['isExplicit'] as bool? ?? true,
            confidence: (item['confidence'] as num?)?.toDouble() ?? 1.0,
          );
        })
        .whereType<CustomerPreference>()
        .toList();
  }

  List<Object?> _items(Object? data) {
    if (data is! Map) {
      return const [];
    }
    final items = data['items'];
    return items is List ? items : const [];
  }

  Customer? _toCustomer(Object? raw) {
    if (raw is! Map) {
      return null;
    }
    final id = raw['customerId'];
    if (id is! String || id.isEmpty) {
      return null;
    }

    final rawTags = raw['tags'];
    final tags = rawTags is List
        ? rawTags.whereType<String>().toSet()
        : const <String>{};

    return Customer(
      id: id,
      organizationId: _activeOrgId,
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

  static List<CustomerSection> _sections(List<Customer> customers) {
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

    return [
      for (final letter in letters)
        CustomerSection(letter: letter, customers: byLetter[letter]!),
    ];
  }

  static CustomerLevel? _levelFromWire(String? value) {
    if (value == null) {
      return null;
    }
    for (final level in CustomerLevel.values) {
      if (level.name.toLowerCase() == value.toLowerCase()) {
        return level;
      }
    }
    return null;
  }

  static double _decimal(Object? value) =>
      value is num ? value.toDouble() : 0;

  static DateTime? _parseUtc(Object? value) =>
      value is String ? DateTime.tryParse(value)?.toUtc() : null;
}
