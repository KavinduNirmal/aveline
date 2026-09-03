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
        return _user;
      }
      return null;
    } on DioException catch (e) {
      _errorMessage = e.message ?? 'Failed to load user profile';
      return null;
    } catch (e) {
      _errorMessage = e.toString();
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
    required String address,
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
        'address': address,
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
}
