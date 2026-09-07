import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../features/auth/domain/aveline_user.dart';

class UserProvider extends ChangeNotifier {
  AvelineUser? _user;
  bool _isLoading = false;
  String? _errorMessage;

  AvelineUser? get user => _user;
  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;
  bool get hasCompletedOnboarding => _user?.hasCompletedOnboarding ?? false;
  AvelineAccountState? get accountState => _user?.accountState;
  bool get isAccountActive =>
      _user?.accountState == AvelineAccountState.active;

  void setUser(AvelineUser? user) {
    _user = user;
    notifyListeners();
  }

  void clear() {
    _user = null;
    _errorMessage = null;
    _isLoading = false;
    notifyListeners();
  }

  Future<AvelineUser?> fetchUser(Dio dio) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final response = await dio.get('/api/v1/users/me');
      if (response.statusCode == 200 && response.data != null) {
        final data = response.data as Map<String, dynamic>;
        _user = AvelineUser.fromJson(data);
        debugPrint(
          '[user] fetchUser OK: accountState=${_user!.accountState.wireValue} '
          'onboarded=${_user!.hasCompletedOnboarding} '
          'orgRole=${_user!.organizationRole}',
        );
        return _user;
      }
      debugPrint('[user] fetchUser non-200: ${response.statusCode}');
      return null;
    } on DioException catch (e) {
      _errorMessage = e.message ?? 'Failed to load user profile';
      debugPrint('[user] fetchUser DioException: ${e.message} type=${e.type}');
      return null;
    } catch (e) {
      _errorMessage = e.toString();
      debugPrint('[user] fetchUser error: $e');
      return null;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  Future<AvelineUser?> completeOnboarding(
    Dio dio, {
    required String displayName,
    required String phoneNumber,
    String? profileImageUrl,
    String contactPreference = 'WhatsApp',
    bool pushNotificationsEnabled = true,
  }) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final payload = {
        'displayName': displayName,
        'phoneNumber': phoneNumber,
        'profileImageUrl': ?profileImageUrl,
        'contactPreference': contactPreference,
        'pushNotificationsEnabled': pushNotificationsEnabled,
      };

      final response = await dio.post('/api/v1/users/onboarding', data: payload);
      if (response.statusCode == 200 && response.data != null) {
        final data = response.data as Map<String, dynamic>;
        _user = AvelineUser.fromJson(data);
        return _user;
      }
      return null;
    } on DioException catch (e) {
      _errorMessage = e.message ?? 'Failed to complete onboarding';
      rethrow;
    } catch (e) {
      _errorMessage = e.toString();
      rethrow;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Accepts an invitation code (staff join flow), then refreshes the profile
  /// so the account state reflects the new active membership.
  Future<void> acceptInvitationCode(Dio dio, {required String code}) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final response = await dio.post(
        '/api/v1/invitations/accept',
        data: {'code': code.trim()},
      );
      if (response.statusCode != 200) {
        throw Exception('Unable to accept the invitation.');
      }
    } on DioException catch (e) {
      _errorMessage = _invitationErrorMessage(e);
      rethrow;
    } catch (e) {
      _errorMessage = e.toString();
      rethrow;
    } finally {
      _isLoading = false;
      notifyListeners();
    }

    await fetchUser(dio);
  }

  String _invitationErrorMessage(DioException e) {
    final data = e.response?.data;
    if (data is Map<String, dynamic>) {
      final detail = data['detail'] ?? data['message'];
      if (detail is String && detail.isNotEmpty) {
        return detail;
      }
    }
    return e.message ?? 'Unable to accept the invitation.';
  }
}
