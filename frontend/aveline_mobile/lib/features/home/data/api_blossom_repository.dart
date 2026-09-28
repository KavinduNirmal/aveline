import 'package:dio/dio.dart';

import '../domain/blossom_usage.dart';
import 'blossom_repository.dart';

/// Reads `GET /api/v1/orgs/{organizationId}/blossoms/balance`.
///
/// The wire quantities are decimals and the server sends its own reconciled
/// `blossomRemaining`, so this is where the projection becomes the card's domain
/// shape. Nothing here truncates: a fractional Blossom is a normal Blossom.
class ApiBlossomRepository implements BlossomRepository {
  ApiBlossomRepository(this._dio);

  final Dio _dio;

  @override
  Future<BlossomUsage> fetchBalance({required String organizationId}) async {
    try {
      final response = await _dio.get(
        '/api/v1/orgs/$organizationId/blossoms/balance',
      );
      final data = response.data;
      if (data is! Map) {
        throw const BlossomBalanceUnavailable(
          'The balance came back in a shape the app does not understand.',
        );
      }
      return _toUsage(data);
    } on DioException catch (error) {
      throw BlossomBalanceUnavailable(
        _describe(error),
        statusCode: error.response?.statusCode,
      );
    }
  }

  BlossomUsage _toUsage(Map<dynamic, dynamic> data) {
    final limit = _decimal(data['monthlyBlossomLimit']);
    final granted = _decimal(data['blossomGranted']);
    final adjusted = _decimal(data['blossomAdjusted']);

    return BlossomUsage(
      used: _decimal(data['blossomUsed']),
      allowance: limit + granted - adjusted,
      renewsOn: _renewalLabel(data['periodEnd']),
      reportedRemaining: _optionalDecimal(data['blossomRemaining']),
      lowBalanceThresholdPercent:
          _optionalDecimal(data['lowBalanceThresholdPercent']) ?? 20,
    );
  }

  static double _decimal(Object? value) {
    if (value is num) {
      return value.toDouble();
    }
    if (value is String) {
      return double.tryParse(value) ?? 0;
    }
    return 0;
  }

  static double? _optionalDecimal(Object? value) {
    if (value == null) {
      return null;
    }
    return _decimal(value);
  }

  /// `2026-10-01T00:00:00Z` reads as `1 October`, the way a renewal date is
  /// spoken. The date is taken in UTC because the server's period boundary is
  /// UTC.
  static String _renewalLabel(Object? value) {
    final parsed = value is String ? DateTime.tryParse(value) : null;
    if (parsed == null) {
      return 'the next cycle';
    }
    final utc = parsed.toUtc();
    return '${utc.day} ${_months[utc.month - 1]}';
  }

  static const List<String> _months = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
  ];

  static String _describe(DioException error) {
    final status = error.response?.statusCode;
    if (status == 403) {
      return 'The balance is not available for your role.';
    }
    if (status == 401) {
      return 'Your session has expired.';
    }
    if (status != null) {
      return 'The balance could not be read ($status).';
    }
    return error.message ?? 'The balance could not be read.';
  }
}
