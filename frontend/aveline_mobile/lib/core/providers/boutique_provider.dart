import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

/// The signed-in associate's boutique identity, from `GET /api/v1/orgs/my`.
///
/// `/api/v1/users/me` carries only the organization id, so the boutique's
/// display name is fetched separately and held here. Any screen that names the
/// shop — the Catalog title, for one — reads it from this provider rather than
/// calling the endpoint again.
class BoutiqueProvider extends ChangeNotifier {
  String? _name;
  bool _isLoading = false;
  String? _errorMessage;

  /// The boutique's display name, or `null` until it is known.
  String? get name => _name;

  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;

  /// Whether loading the boutique has failed and has not succeeded since.
  bool get hasLoadFailed => _errorMessage != null;

  void clear() {
    _name = null;
    _errorMessage = null;
    _isLoading = false;
    notifyListeners();
  }

  /// Loads the boutique name for the signed-in user.
  ///
  /// Failure is not fatal: the name is decoration on screens that carry their
  /// own heading, so a failed load leaves [name] null and the caller falls back
  /// to the brand rather than blocking the screen.
  Future<void> fetchBoutique(Dio dio) async {
    _isLoading = true;
    notifyListeners();

    try {
      final response = await dio.get('/api/v1/orgs/my');
      if (response.statusCode == 200 && response.data != null) {
        _name = _pickName(response.data);
        _errorMessage = _name == null
            ? 'No boutique is linked to this account.'
            : null;
      } else {
        _errorMessage = 'The salon could not load your boutique.';
      }
    } on DioException catch (e) {
      _errorMessage = e.message ?? 'Could not load your boutique.';
    } catch (e) {
      _errorMessage = e.toString();
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Picks the display name out of the memberships `/orgs/my` returns.
  ///
  /// An account can belong to more than one boutique. The active membership
  /// wins; a named membership is the fallback, so that a boutique the associate
  /// has not accepted an invitation to yet still gives the title something to
  /// read.
  String? _pickName(Object? data) {
    if (data is! List) {
      return null;
    }

    String? firstNamed;
    for (final entry in data) {
      if (entry is! Map) {
        continue;
      }
      final raw = entry['organizationName'];
      if (raw is! String || raw.trim().isEmpty) {
        continue;
      }
      final name = raw.trim();
      firstNamed ??= name;

      final status = entry['status'];
      if (status is String && status.toLowerCase() == 'active') {
        return name;
      }
    }
    return firstNamed;
  }
}
