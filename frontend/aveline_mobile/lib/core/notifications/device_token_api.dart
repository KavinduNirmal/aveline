import 'package:dio/dio.dart';

/// Abstraction over the backend device-token endpoints so the push service can be
/// unit tested without a real HTTP client.
abstract interface class DeviceTokenApi {
  /// Registers (or refreshes) a device token. See POST /api/v1/users/me/devices.
  Future<void> register(String token, String platform);

  /// Deactivates a device token. See DELETE /api/v1/users/me/devices/{token}.
  Future<void> unregister(String token);
}

/// Dio-backed [DeviceTokenApi] for the Aveline API.
class DioDeviceTokenApi implements DeviceTokenApi {
  DioDeviceTokenApi(this._dio);

  final Dio _dio;

  @override
  Future<void> register(String token, String platform) async {
    await _dio.post(
      '/api/v1/users/me/devices',
      data: {'token': token, 'platform': platform},
    );
  }

  @override
  Future<void> unregister(String token) async {
    await _dio.delete('/api/v1/users/me/devices/$token');
  }
}
