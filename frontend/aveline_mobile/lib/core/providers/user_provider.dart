import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../features/auth/domain/aveline_user.dart';
import '../../features/auth/domain/contact_preference.dart';

class UserProvider extends ChangeNotifier {
  AvelineUser? _user;
  bool _isLoading = false;
  String? _errorMessage;
  String? _updateErrorMessage;

  AvelineUser? get user => _user;
  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;
  bool get hasCompletedOnboarding => _user?.hasCompletedOnboarding ?? false;
  AvelineAccountState? get accountState => _user?.accountState;
  bool get isAccountActive =>
      _user?.accountState == AvelineAccountState.active;

  /// Why the last change to the profile was refused, or `null` when the last one
  /// was saved.
  ///
  /// Held apart from [errorMessage], which reports a profile that could not be
  /// *read*: the router answers that one with the retry screen, and a rejected
  /// toggle must not move the associate off the screen they are on.
  String? get updateErrorMessage => _updateErrorMessage;

  /// Whether loading the signed-in profile has failed and has not succeeded
  /// since.
  ///
  /// Deliberately stays true while a retry is in flight: the retry screen keys
  /// off this, and clearing it at the start of a fetch would bounce the user
  /// back to an onboarding screen before the retry resolves.
  bool get hasLoadFailed => _errorMessage != null;

  void setUser(AvelineUser? user) {
    _user = user;
    notifyListeners();
  }

  void clear() {
    _user = null;
    _errorMessage = null;
    _updateErrorMessage = null;
    _isLoading = false;
    notifyListeners();
  }

  /// Takes back the last refusal once it has been shown.
  void clearUpdateError() {
    if (_updateErrorMessage == null) {
      return;
    }
    _updateErrorMessage = null;
    notifyListeners();
  }

  Future<AvelineUser?> fetchUser(Dio dio) async {
    _isLoading = true;
    notifyListeners();

    try {
      final response = await dio.get('/api/v1/users/me');
      if (response.statusCode == 200 && response.data != null) {
        final data = response.data as Map<String, dynamic>;
        _user = AvelineUser.fromJson(data);
        _errorMessage = null;
        debugPrint(
          '[user] fetchUser OK: accountState=${_user!.accountState.wireValue} '
          'onboarded=${_user!.hasCompletedOnboarding} '
          'orgRole=${_user!.organizationRole}',
        );
        return _user;
      }
      _errorMessage = 'The salon could not load your profile.';
      debugPrint('[user] fetchUser non-200: ${response.statusCode}');
      return null;
    } on DioException catch (e) {
      _errorMessage = _describeDioException(e);
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

  /// Describes a [DioException] for the retry screen.
  ///
  /// Dio's own `message` is null whenever the failure came from an interceptor
  /// rather than from the transport, which is what happens when the access
  /// token cannot be minted. Keeping the underlying error preserves the
  /// difference between an unreachable server and a rejected request, which is
  /// what decides whether the user is told to check their connection.
  String _describeDioException(DioException e) {
    final cause = e.error;
    final message = e.message;
    if (message != null && message.isNotEmpty) {
      return cause == null ? message : '$message ($cause)';
    }
    return cause?.toString() ?? 'Could not load your profile (${e.type.name}).';
  }

  /// Saves the parts of the profile the associate owns.
  ///
  /// Only the fields that are passed are sent, which is what the API means by
  /// changing the supplied fields. The answer replaces the held record, so every
  /// control that reads [user] follows the server rather than a local guess -
  /// which is also why a save that fails puts itself back.
  ///
  /// Returns whether the change was saved. A refusal is reported through
  /// [updateErrorMessage] rather than thrown, because the screen has to keep
  /// working either way.
  Future<bool> updateProfile(
    Dio dio, {
    String? displayName,
    String? phoneNumber,
    ContactPreference? contactPreference,
    bool? pushNotificationsEnabled,
  }) async {
    final payload = <String, dynamic>{
      'displayName': ?displayName,
      'phoneNumber': ?phoneNumber,
      'contactPreference': ?contactPreference?.wireValue,
      'pushNotificationsEnabled': ?pushNotificationsEnabled,
    };

    // Nothing to change is not worth a request, and an empty body would read to
    // the API as a caller that meant to send something and sent nothing.
    if (payload.isEmpty) {
      return true;
    }

    // Deliberately not `_isLoading`: that flag means the profile is being
    // *fetched*, and the retry screen keys off it. A save in flight is the
    // screen's own business.
    try {
      final response = await dio.patch('/api/v1/users/me', data: payload);
      final data = response.data;
      if (response.statusCode == 200 && data is Map) {
        _user = AvelineUser.fromJson(Map<String, dynamic>.from(data));
        _updateErrorMessage = null;
        debugPrint('[user] updateProfile OK: updatedAt=${_user!.updatedAt}');
        return true;
      }
      _updateErrorMessage = 'Your settings were not saved.';
      debugPrint('[user] updateProfile non-200: ${response.statusCode}');
      return false;
    } on DioException catch (e) {
      _updateErrorMessage = _describeUpdateFailure(e);
      debugPrint('[user] updateProfile DioException: ${e.message} type=${e.type}');
      return false;
    } catch (e) {
      _updateErrorMessage = 'Your settings were not saved.';
      debugPrint('[user] updateProfile error: $e');
      return false;
    } finally {
      notifyListeners();
    }
  }

  /// Describes why a change to the profile was refused.
  ///
  /// A refused request carries the API's own reason, and that is more use to the
  /// associate than Dio's explanation of `validateStatus`. A request that never
  /// arrived has no response to read, so it keeps the transport's own words.
  String _describeUpdateFailure(DioException e) {
    final response = e.response;
    if (response == null) {
      return _describeDioException(e);
    }
    return _serverDetail(response.data) ??
        'Your settings were not saved (${response.statusCode}).';
  }

  /// The API's own words for a refused request, when it gave any.
  String? _serverDetail(Object? data) {
    if (data is Map) {
      final errors = data['errors'];
      if (errors is Map && errors.isNotEmpty) {
        final firstList = errors.values.first;
        if (firstList is List && firstList.isNotEmpty) {
          final firstMsg = firstList.first;
          if (firstMsg is String && firstMsg.isNotEmpty) {
            return firstMsg;
          }
        }
      }

      final detail = data['detail'] ?? data['message'];
      if (detail is String && detail.isNotEmpty) {
        return detail;
      }
    }
    return null;
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
    return _serverDetail(e.response?.data) ??
        e.message ??
        'Unable to accept the invitation.';
  }
}
