import 'package:dio/dio.dart';

import '../../customers/data/customer_repository.dart';
import '../../customers/domain/customer.dart';
import '../../customers/domain/customer_book.dart';
import '../../customers/domain/customer_level.dart';
import '../domain/client_highlight.dart';

/// The tenant-facing customer surface, from
/// `/api/v1/orgs/{id}/customers` (the book, walk-in creation) and
/// `/api/v1/orgs/{id}/customers/highlights` (Home's client row).
///
/// Deliberately narrower than the customers feature: Home reads the book and
/// creates walk-ins, and maps the wire shape into Home's own `ClientHighlight`
/// rather than importing the customers domain into the Home row.
class ApiCustomerTenantRepository implements CustomerBookSource {
  ApiCustomerTenantRepository(this._dio, {required this.organizationId});

  final Dio _dio;
  final String organizationId;

  /// The whole narrowing in one call, because the alphabet index has to be able
  /// to reach every letter it offers.
  @override
  Future<CustomerBook> fetchBook({CustomerQuery query = const CustomerQuery()}) async {
    final response = await _dio.get(
      '/api/v1/orgs/$organizationId/customers',
      queryParameters: {
        'page': 1,
        'pageSize': 200,
        if (query.search.trim().isNotEmpty) 'search': query.search.trim(),
        if (query.level != null) 'level': _levelWire(query.level!),
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

  /// The clients Home's row shows, most recent activity first.
  Future<List<ClientHighlight>> fetchHighlights({int limit = 25}) async {
    final response = await _dio.get(
      '/api/v1/orgs/$organizationId/customers/highlights',
      queryParameters: {'limit': limit},
    );

    final highlights = <ClientHighlight>[];
    for (final raw in _items(response.data)) {
      if (raw is! Map) {
        continue;
      }
      final id = raw['customerId'];
      final name = (raw['name'] as String?)?.trim();
      if (id is! String || name == null || name.isEmpty) {
        continue;
      }
      highlights.add(ClientHighlight(
        id: id,
        name: name,
        tier: ClientTier.fromWire(raw['level'] as String?),
        activity: (raw['activity'] as String?) ?? '',
      ));
    }
    return highlights;
  }

  /// Creates a counter walk-in and returns the client the server recorded.
  Future<ClientHighlight> createWalkIn(String fullName) async {
    final response = await _dio.post(
      '/api/v1/orgs/$organizationId/customers',
      data: {
        'fullName': fullName,
        'source': 'counter_walkin',
        'phoneNumber': null,
        'nickname': null,
      },
      options: Options(
        headers: {'Idempotency-Key': _idempotencyKey()},
      ),
    );

    final data = response.data;
    if (data is! Map || data['customerId'] is! String) {
      throw const FormatException('The walk-in came back without an id.');
    }

    final duplicate = data['duplicateOfCustomerId'] != null;
    return ClientHighlight(
      id: data['customerId'] as String,
      name: (data['fullName'] as String?)?.trim().isNotEmpty == true
          ? (data['fullName'] as String).trim()
          : fullName,
      tier: ClientTier.fromWire(data['level'] as String?),
      activity: duplicate
          ? 'Already on file.'
          : 'Added at the counter.',
    );
  }

  /// Records a counter visit: an inbound in-person interaction, which is what
  /// moves the customer's visit counters and can upgrade their tier.
  Future<void> recordVisit(String customerId) async {
    await _dio.post(
      '/api/v1/orgs/$organizationId/customers/$customerId/interactions',
      data: {
        'occurredAtUtc': DateTime.now().toUtc().toIso8601String(),
        'channel': 'in_person',
        'direction': 'inbound',
        'note': null,
        'purchaseTotal': null,
      },
      options: Options(
        headers: {'Idempotency-Key': _idempotencyKey(prefix: 'visit')},
      ),
    );
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

    return Customer(
      id: id,
      organizationId: organizationId,
      phoneNumber: (raw['phoneNumber'] as String?) ?? '',
      // The server stores no grade until the boutique sets one. The customers
      // domain has no "ungraded" value yet (making it nullable is the sibling
      // plan's change), so the picker falls back to the entry grade here; Home's
      // own row reads the nullable wire value directly and hides the badge.
      level: _levelFromWire(raw['level'] as String?) ?? CustomerLevel.level1,
      status: CustomerStatus.parse(raw['status'] as String?),
      fullName: raw['fullName'] as String?,
      nickname: raw['nickname'] as String?,
      totalSpent: _decimal(raw['totalSpent']),
      visitCount: (raw['visitCount'] as num?)?.toInt() ?? 0,
      lastVisitAtUtc: _parseUtc(raw['lastVisitAtUtc']),
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

  static String _levelWire(CustomerLevel level) => level.name;

  static CustomerLevel? _levelFromWire(String? value) {
    if (value == null) {
      return null;
    }
    for (final level in CustomerLevel.values) {
      if (level.name == value) {
        return level;
      }
    }
    return null;
  }

  static double _decimal(Object? value) =>
      value is num ? value.toDouble() : 0;

  static DateTime? _parseUtc(Object? value) =>
      value is String ? DateTime.tryParse(value)?.toUtc() : null;

  static String _idempotencyKey({String prefix = 'walkin'}) =>
      '$prefix-${DateTime.now().microsecondsSinceEpoch}';
}
