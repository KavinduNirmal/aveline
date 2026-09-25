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
/// - `GET /api/v1/orgs/{organizationId}/customers/{customerId}/interactions` (interactions history)
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

      // Fetch consent
      CustomerConsent consent = CustomerConsent.unknown;
      try {
        final consentResponse = await _dio.get<Map<String, dynamic>>(
          '/api/v1/orgs/$orgId/customers/$id/consent',
        );
        if (consentResponse.data != null) {
          consent = _toConsent(consentResponse.data!);
        }
      } catch (_) {
        // Tolerates absent consent row or transient network issue
      }

      // Fetch interactions
      final interactions = <CustomerInteraction>[];
      try {
        final interactionsResponse = await _dio.get<Map<String, dynamic>>(
          '/api/v1/orgs/$orgId/customers/$id/interactions',
          queryParameters: {'page': 1, 'pageSize': 50},
        );
        final rawItems = interactionsResponse.data?['items'];
        if (rawItems is List) {
          for (final raw in rawItems) {
            if (raw is Map<String, dynamic>) {
              final interaction = _toInteraction(raw);
              if (interaction != null) {
                interactions.add(interaction);
              }
            }
          }
        }
      } catch (_) {
        // Tolerates absent interaction history
      }

      return CustomerDetail(
        customer: customer,
        consent: consent,
        interactions: interactions,
        preferences: const [],
        memories: const [],
        events: const [],
      );
    } on DioException catch (error) {
      if (error.response?.statusCode == 404) {
        return null;
      }
      rethrow;
    }
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

  CustomerConsent _toConsent(Map<String, dynamic> raw) {
    return CustomerConsent(
      status: ConsentStatus.parse(raw['consentStatus'] as String?),
      grantedAtUtc: _parseUtc(raw['consentGrantedAt']),
      revokedAtUtc: _parseUtc(raw['consentRevokedAt']),
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
