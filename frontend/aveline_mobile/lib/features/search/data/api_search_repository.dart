import 'package:dio/dio.dart';

import '../../../../core/network/org_context.dart';

/// A single hit in a search query response across clients, pieces, and messages.
class SearchResultItem {
  const SearchResultItem({
    required this.type,
    required this.id,
    required this.title,
    this.subtitle,
    required this.score,
    required this.href,
  });

  /// The hit's entity family: `customer`, `catalogItem`, or `conversation`.
  final String type;
  final String id;
  final String title;
  final String? subtitle;
  final double score;
  final String href;

  factory SearchResultItem.fromJson(Map<String, dynamic> json) => SearchResultItem(
        type: json['type'] as String? ?? 'unknown',
        id: json['id'] as String? ?? '',
        title: json['title'] as String? ?? '',
        subtitle: json['subtitle'] as String?,
        score: (json['score'] as num?)?.toDouble() ?? 0.0,
        href: json['href'] as String? ?? '/',
      );
}

/// The abstraction for cross-entity search.
abstract interface class SearchRepository {
  Future<List<SearchResultItem>> search(String query, {String scope = 'all'});
}

/// The live implementation of [SearchRepository] querying `/api/v1/orgs/{id}/search`.
class ApiSearchRepository implements SearchRepository {
  ApiSearchRepository(this._dio, {required this.organizationId});

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
  Future<List<SearchResultItem>> search(String query, {String scope = 'all'}) async {
    final clean = query.trim();
    if (clean.length < 2) return const [];

    final orgId = _requireOrganizationId();
    final response = await _dio.get<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/search',
      queryParameters: {'q': clean, 'scope': scope},
    );

    final data = response.data;
    final items = data != null && data['items'] is List
        ? data['items'] as List<dynamic>
        : const <dynamic>[];

    return items
        .whereType<Map<String, dynamic>>()
        .map(SearchResultItem.fromJson)
        .toList();
  }
}
