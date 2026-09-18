import 'dart:async';

import 'package:aveline_mobile/core/notifications/device_token_api.dart';
import 'package:aveline_mobile/core/notifications/push_notification_service.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeTokenSource implements PushTokenSource {
  FakeTokenSource({this.token, this.denyPermission = false});

  String? token;
  bool denyPermission;
  final _refreshController = StreamController<String>.broadcast();

  @override
  Future<String?> getToken() async => denyPermission ? null : token;

  @override
  Stream<String> get onTokenRefresh => _refreshController.stream;

  void rotate(String newToken) => _refreshController.add(newToken);

  void dispose() => _refreshController.close();
}

class FakeDeviceTokenApi implements DeviceTokenApi {
  final registered = <String>[];
  final unregistered = <String>[];

  @override
  Future<void> register(String token, String platform) async {
    registered.add(token);
  }

  @override
  Future<void> unregister(String token) async {
    unregistered.add(token);
  }
}

void main() {
  group('PushNotificationService', () {
    test('initialize registers the current token', () async {
      final source = FakeTokenSource(token: 'tok-1');
      final api = FakeDeviceTokenApi();
      final service = PushNotificationService(source, api, 'Android');

      await service.initialize();

      expect(api.registered, ['tok-1']);
      source.dispose();
    });

    test('initialize does nothing when permission denied', () async {
      final source = FakeTokenSource(token: 'tok-1', denyPermission: true);
      final api = FakeDeviceTokenApi();
      final service = PushNotificationService(source, api, 'Android');

      await service.initialize();

      expect(api.registered, isEmpty);
      source.dispose();
    });

    test('registers a rotated token', () async {
      final source = FakeTokenSource(token: 'tok-1');
      final api = FakeDeviceTokenApi();
      final service = PushNotificationService(source, api, 'Android');

      await service.initialize();
      source.rotate('tok-2');
      await Future<void>.delayed(Duration.zero);

      expect(api.registered, contains('tok-2'));
      source.dispose();
    });

    test('unregister removes the current token', () async {
      final source = FakeTokenSource(token: 'tok-1');
      final api = FakeDeviceTokenApi();
      final service = PushNotificationService(source, api, 'Android');

      await service.initialize();
      await service.unregister();

      expect(api.unregistered, ['tok-1']);
      source.dispose();
    });

    test('unregister without a token does nothing', () async {
      final source = FakeTokenSource(token: null);
      final api = FakeDeviceTokenApi();
      final service = PushNotificationService(source, api, 'Android');

      await service.initialize();
      await service.unregister();

      expect(api.unregistered, isEmpty);
      source.dispose();
    });
  });
}
