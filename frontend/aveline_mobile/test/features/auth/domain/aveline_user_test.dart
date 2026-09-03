import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AvelineUser Model', () {
    test('fromJson creates valid instance with all fields', () {
      final json = {
        'id': 'usr-001',
        'clerkId': 'user_2xyz',
        'email': 'kasun@aveline.lk',
        'firstName': 'Kasun',
        'lastName': 'Delpachithra',
        'displayName': 'Kasun Delpachithra',
        'username': 'kasun_d',
        'phoneNumber': '+94771234567',
        'address': '15 Alfred House Gardens, Colombo 03',
        'profileImageUrl': 'https://img.clerk.com/avatar.png',
        'userRole': 'associate',
        'organizationRole': 'org:member',
        'organizationId': 'org_colombo',
        'hasCompletedOnboarding': true,
        'contactPreference': 'WhatsApp',
        'pushNotificationsEnabled': true,
        'isActive': true,
        'createdAt': '2026-09-03T10:00:00.000Z',
        'updatedAt': '2026-09-03T10:00:00.000Z',
      };

      final user = AvelineUser.fromJson(json);

      expect(user.id, 'usr-001');
      expect(user.clerkId, 'user_2xyz');
      expect(user.email, 'kasun@aveline.lk');
      expect(user.displayName, 'Kasun Delpachithra');
      expect(user.hasCompletedOnboarding, isTrue);
      expect(user.contactPreference, 'WhatsApp');
      expect(user.pushNotificationsEnabled, isTrue);

      final serialized = user.toJson();
      expect(serialized['clerkId'], 'user_2xyz');
      expect(serialized['hasCompletedOnboarding'], isTrue);
    });

    test('fromJson handles stub user with default values', () {
      final json = {
        'id': 'usr-002',
        'clerkId': 'user_new',
        'email': 'new@aveline.lk',
        'firstName': 'New',
        'lastName': 'User',
        'username': 'new_u',
        'userRole': 'user',
        'organizationRole': '',
        'organizationId': '',
        'hasCompletedOnboarding': false,
      };

      final user = AvelineUser.fromJson(json);

      expect(user.clerkId, 'user_new');
      expect(user.hasCompletedOnboarding, isFalse);
      expect(user.displayName, isNull);
    });
  });
}
