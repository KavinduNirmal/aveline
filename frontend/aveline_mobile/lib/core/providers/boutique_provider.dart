import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

/// The signed-in associate's boutique identity, from `GET /api/v1/orgs/my`.
///
/// `/api/v1/users/me` carries only the organization id, so the boutique's
/// display name is fetched separately and held here. Any screen that names the
/// shop — the Catalog title, for one — reads it from this provider rather than
/// calling the endpoint again.
///
/// The name, the organization id and the boutique role all come from the **same
/// membership row**, so a screen can build `/api/v1/orgs/{id}/…` for the shop
/// whose name it is showing. The id is the canonical, membership-checked GUID:
/// the JWT's `org_id` claim can be stale, and the authorization handler
/// deliberately ignores it.
class BoutiqueProvider extends ChangeNotifier {
  String? _name;
  String? _organizationId;
  String? _boutiqueRole;
  bool _isLoading = false;
  String? _errorMessage;

  /// The boutique's display name, or `null` until it is known.
  String? get name => _name;

  /// The active membership's organization id, or `null` when there is none.
  ///
  /// A null value is a hard stop for any org-scoped call: the route template is
  /// `{organizationId:guid}` and the authorization handler fails closed on a
  /// missing or non-GUID value.
  String? get organizationId => _organizationId;

  /// The active membership's boutique role (for example `org:boutique_staff`).
  ///
  /// Read from the provider rather than from JWT claims, because the membership
  /// row is the source the server authorizes against.
  String? get boutiqueRole => _boutiqueRole;

  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;

  /// Whether loading the boutique has failed and has not succeeded since.
  bool get hasLoadFailed => _errorMessage != null;

  void clear() {
    _name = null;
    _organizationId = null;
    _boutiqueRole = null;
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
        final membership = _pickMembership(response.data);
        _name = membership?.name;
        _organizationId = membership?.organizationId;
        _boutiqueRole = membership?.boutiqueRole;
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

  /// Picks the membership `/orgs/my` should describe the account with.
  ///
  /// An account can belong to more than one boutique. The active membership
  /// wins; a named membership is the fallback, so that a boutique the associate
  /// has not accepted an invitation to yet still gives the title something to
  /// read. The id, the role and the name are read from that one entry, so they
  /// cannot describe different memberships.
  _Membership? _pickMembership(Object? data) {
    if (data is! List) {
      return null;
    }

    _Membership? firstNamed;
    for (final entry in data) {
      if (entry is! Map) {
        continue;
      }
      final raw = entry['organizationName'];
      if (raw is! String || raw.trim().isEmpty) {
        continue;
      }

      final membership = _Membership(
        name: raw.trim(),
        organizationId: _stringOrNull(entry['organizationId']),
        boutiqueRole: _stringOrNull(entry['boutiqueRole']),
      );
      firstNamed ??= membership;

      final status = entry['status'];
      if (status is String && status.toLowerCase() == 'active') {
        return membership;
      }
    }
    return firstNamed;
  }

  static String? _stringOrNull(Object? value) {
    if (value is! String || value.trim().isEmpty) {
      return null;
    }
    return value.trim();
  }
}

/// One membership row, as far as the provider needs it.
class _Membership {
  const _Membership({
    required this.name,
    required this.organizationId,
    required this.boutiqueRole,
  });

  final String name;
  final String? organizationId;
  final String? boutiqueRole;
}
