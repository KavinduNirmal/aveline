import 'package:dio/dio.dart';

import '../../../core/network/org_context.dart';
import '../domain/customer.dart';
import '../domain/customer_book.dart';
import '../domain/customer_detail.dart';
import '../domain/customer_level.dart';
import 'customer_repository.dart';

/// The customer API repository connecting to Aveline backend.
class ApiCustomerRepository implements CustomerRepository {
  ApiCustomerRepository(
    this._dio, {
    required this.organizationId,
  });

  final Dio _dio;

  /// The shop whose customers are being read, resolved at call time.
  final String? Function() organizationId;

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      throw const OrgContextUnavailable();
    }

    final params = <String, dynamic>{
      'page': 1,
      'pageSize': 500, // Fetches enough for the book index
    };
    if (query.search.isNotEmpty) {
      params['search'] = query.search;
    }
    if (query.level != null) {
      params['level'] = query.level!.wireValue;
    }

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/customers',
      queryParameters: params,
    );

    final data = response.data;
    if (data == null) {
      return const CustomerBook([]);
    }

    final items = data['items'] as List<dynamic>? ?? [];
    final customers = items.map((e) => _parseBookItem(e as Map<String, dynamic>, orgId)).toList();
    return _section(customers);
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      throw const OrgContextUnavailable();
    }

    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/customers/$id',
      );
      final data = response.data;
      if (data == null) return null;

      final customer = _parseTenantCustomerDetail(data, orgId);
      
      // TenantCustomerDetailDto doesn't include memories, preferences, etc.
      // They would be loaded from separate internal endpoints or a different tenant endpoint later.
      return CustomerDetail(
        customer: customer,
      );
    } on DioException catch (error) {
      if (error.response?.statusCode == 404 || error.response?.statusCode == 403) {
        return null;
      }
      rethrow;
    }
  }

  @override
  Future<List<CustomerInteraction>> fetchCustomerInteractions(
    String customerId, {
    CustomerInteractionQuery query = const CustomerInteractionQuery(),
  }) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      throw const OrgContextUnavailable();
    }

    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/customers/$customerId/interactions',
      queryParameters: {
        'page': 1,
        'pageSize': 100, // Reasonable max for interactions history on mobile
      },
    );

    final data = response.data;
    if (data == null) return [];

    final items = data['items'] as List<dynamic>? ?? [];
    final interactions = items.map((e) => _parseInteraction(e as Map<String, dynamic>)).toList();
    
    return interactions.where(query.matches).toList();
  }

  @override
  Future<CustomerInteraction> recordInteraction(
    String customerId,
    RecordInteractionRequest request,
  ) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      throw const OrgContextUnavailable();
    }

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/customers/$customerId/interactions',
      data: {
        'occurredAtUtc': request.occurredAtUtc.toIso8601String(),
        'channel': request.channel.wireValue,
        'direction': request.direction.wireValue,
        'note': request.note,
        'purchaseTotal': request.purchaseTotal,
      },
    );

    final data = response.data;
    if (data == null) {
      throw Exception('Failed to record interaction: null response data');
    }

    // The backend returns a VisitReceiptDto.
    // It's missing some fields to be a full interaction (like channel, direction), 
    // but we can reconstruct a basic one or return a synthesized one.
    return CustomerInteraction(
      id: data['visitId']?.toString() ?? 'int-rec-${DateTime.now().microsecondsSinceEpoch}',
      channel: InteractionChannel.parse(data['channel']?.toString()),
      direction: request.direction, // Receipt doesn't always reflect direction if it's implicitly inbound for visits
      createdAtUtc: data['occurredAtUtc'] != null ? DateTime.parse(data['occurredAtUtc'].toString()).toLocal() : request.occurredAtUtc,
      messageContent: request.note,
      purchaseTotal: request.purchaseTotal,
      staffMemberName: 'Boutique Staff',
      tags: request.tags,
      countedAsVisit: data['countedAsVisit'] == true,
    );
  }

  // Parses CustomerBookItemDto
  Customer _parseBookItem(Map<String, dynamic> json, String organizationId) {
    return Customer(
      id: json['customerId']?.toString() ?? '',
      organizationId: organizationId,
      phoneNumber: json['phoneNumber']?.toString() ?? '',
      level: CustomerLevel.parse(json['level']?.toString()),
      status: CustomerStatus.parse(json['status']?.toString()),
      fullName: json['fullName']?.toString(),
      nickname: json['nickname']?.toString(),
      totalSpent: (json['totalSpent'] as num?)?.toDouble() ?? 0.0,
      visitCount: json['visitCount'] as int? ?? 0,
      lastVisitAtUtc: json['lastVisitAtUtc'] != null ? DateTime.parse(json['lastVisitAtUtc'].toString()).toLocal() : null,
    );
  }

  // Parses TenantCustomerDetailDto
  Customer _parseTenantCustomerDetail(Map<String, dynamic> json, String organizationId) {
    final tagsRaw = json['tags'] as List<dynamic>? ?? [];
    final tags = tagsRaw.map((e) => e.toString()).toSet();

    return Customer(
      id: json['customerId']?.toString() ?? '',
      organizationId: organizationId,
      phoneNumber: json['phoneNumber']?.toString() ?? '',
      email: json['email']?.toString(),
      level: CustomerLevel.parse(json['level']?.toString()),
      status: CustomerStatus.parse(json['status']?.toString()),
      fullName: json['fullName']?.toString(),
      nickname: json['nickname']?.toString(),
      totalSpent: (json['totalSpent'] as num?)?.toDouble() ?? 0.0,
      visitCount: json['visitCount'] as int? ?? 0,
      lastVisitAtUtc: json['lastVisitAtUtc'] != null ? DateTime.parse(json['lastVisitAtUtc'].toString()).toLocal() : null,
      createdAtUtc: json['createdAtUtc'] != null ? DateTime.parse(json['createdAtUtc'].toString()).toLocal() : null,
      tags: tags,
    );
  }

  // Parses CustomerInteractionItemDto
  CustomerInteraction _parseInteraction(Map<String, dynamic> json) {
    return CustomerInteraction(
      id: json['interactionId']?.toString() ?? '',
      channel: InteractionChannel.parse(json['channel']?.toString()),
      direction: InteractionDirection.parse(json['direction']?.toString()),
      createdAtUtc: json['occurredAtUtc'] != null ? DateTime.parse(json['occurredAtUtc'].toString()).toLocal() : DateTime.now(),
      messageContent: json['note']?.toString(),
      countedAsVisit: json['countedAsVisit'] == true,
    );
  }

  CustomerBook _section(List<Customer> matching) {
    final grouped = <String, List<Customer>>{};
    for (final customer in matching) {
      grouped.putIfAbsent(customer.sectionLetter, () => <Customer>[]).add(customer);
    }

    final letters = grouped.keys.toList()
      ..sort((a, b) {
        if (a == '#') return b == '#' ? 0 : 1;
        if (b == '#') return -1;
        return a.compareTo(b);
      });

    return CustomerBook([
      for (final letter in letters)
        CustomerSection(
          letter: letter,
          customers: grouped[letter]!
            ..sort(
              (a, b) => a.displayName.toLowerCase().compareTo(
                b.displayName.toLowerCase(),
              ),
            ),
        ),
    ]);
  }
}
