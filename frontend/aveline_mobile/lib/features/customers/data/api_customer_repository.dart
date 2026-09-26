import 'package:dio/dio.dart';

import '../../../../core/network/org_context.dart';
import '../domain/customer.dart';
import '../domain/customer_book.dart';
import '../domain/customer_detail.dart';
import '../domain/customer_level.dart';
import 'customer_repository.dart';

/// Customer repository backed by the Aveline .NET Backend API.
///
/// Communicates with `/api/v1/orgs/{organizationId}/customers`.
class ApiCustomerRepository implements CustomerRepository {
  ApiCustomerRepository(
    this._dio, {
    required this.organizationId,
  });

  final Dio _dio;
  final String? Function() organizationId;

  String _requireOrganizationId() {
    final id = organizationId();
    if (id == null || id.isEmpty) {
      throw const OrgContextUnavailable();
    }
    return id;
  }

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    final orgId = _requireOrganizationId();
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/customers',
      queryParameters: {
        'page': 1,
        'pageSize': 200,
        if (query.search.trim().isNotEmpty) 'search': query.search.trim(),
        if (query.level != null) 'level': query.level!.name,
      },
    );

    final data = response.data;
    final items = data != null && data['items'] is List
        ? data['items'] as List<dynamic>
        : const <dynamic>[];

    final customers = items
        .map((item) => _toCustomer(item, orgId))
        .whereType<Customer>()
        .toList()
      ..sort((a, b) => a.sectionLetter.compareTo(b.sectionLetter));

    return CustomerBook(_sections(customers));
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    final orgId = _requireOrganizationId();
    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/customers/$id',
      );
      final data = response.data;
      if (data == null) return null;
      return _parseCustomerDetail(data, orgId);
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  Customer? _toCustomer(Object? raw, String orgId) {
    if (raw is! Map) return null;
    final id = raw['customerId'];
    if (id is! String || id.isEmpty) return null;

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
      tags: raw['tags'] is List
          ? (raw['tags'] as List).whereType<String>().toSet()
          : const <String>{},
    );
  }

  CustomerDetail _parseCustomerDetail(Map<String, dynamic> raw, String orgId) {
    final customer = _toCustomer(raw, orgId)!;
    return CustomerDetail(
      customer: customer,
      consent: CustomerConsent.unknown,
      preferences: const [],
      memories: const [],
      events: const [],
      interactions: const [],
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
    if (value == null) return null;
    for (final level in CustomerLevel.values) {
      if (level.name == value) return level;
    }
    return null;
  }

  static double _decimal(Object? value) =>
      value is num ? value.toDouble() : 0.0;

  static DateTime? _parseUtc(Object? value) =>
      value is String ? DateTime.tryParse(value)?.toUtc() : null;
}
