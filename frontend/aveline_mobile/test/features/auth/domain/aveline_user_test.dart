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
        'userRole': 'staff',
        'organizationRole': 'org:boutique_staff',
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
      expect(user.accountState, AvelineAccountState.active);
      expect(user.contactPreference, 'WhatsApp');
      expect(user.pushNotificationsEnabled, isTrue);

      final serialized = user.toJson();
      expect(serialized['clerkId'], 'user_2xyz');
      expect(serialized['hasCompletedOnboarding'], isTrue);
      expect(serialized['accountState'], 'Active');
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
      expect(user.accountState, AvelineAccountState.onboardingPending);
    });

    test('parses account state and serializes it back', () {
      final pending = AvelineUser.fromJson({
        'id': 'usr-003',
        'clerkId': 'user_staff',
        'email': 'staff@aveline.lk',
        'firstName': 'Staff',
        'lastName': 'Member',
        'username': 'staff_member',
        'userRole': 'staff',
        'organizationRole': '',
        'organizationId': '',
        'hasCompletedOnboarding': true,
        'accountState': 'OnboardingPending',
      });
      expect(pending.accountState, AvelineAccountState.onboardingPending);
      expect(pending.toJson()['accountState'], 'OnboardingPending');

      final active = AvelineUser.fromJson({
        'id': 'usr-004',
        'clerkId': 'user_owner',
        'email': 'owner@aveline.lk',
        'firstName': 'Owner',
        'lastName': 'User',
        'username': 'owner_user',
        'userRole': 'owner',
        'organizationRole': 'org:boutique_owner',
        'organizationId': 'org_colombo',
        'hasCompletedOnboarding': true,
        'accountState': 'Active',
      });
      expect(active.accountState, AvelineAccountState.active);

      final suspended = AvelineUser.fromJson({
        'id': 'usr-005',
        'clerkId': 'user_suspended',
        'email': 'suspended@aveline.lk',
        'firstName': 'Suspended',
        'lastName': 'User',
        'username': 'suspended_user',
        'userRole': 'staff',
        'organizationRole': 'org:boutique_staff',
        'organizationId': 'org_colombo',
        'hasCompletedOnboarding': true,
        'accountState': 'Suspended',
        'isActive': false,
      });
      expect(suspended.accountState, AvelineAccountState.suspended);
    });

    test('derives account state when the payload omits it', () {
      final inactive = AvelineUser.fromJson({
        'id': 'usr-006',
        'clerkId': 'user_x',
        'email': 'x@aveline.lk',
        'firstName': 'X',
        'lastName': 'Y',
        'username': 'xy',
        'userRole': 'staff',
        'organizationRole': '',
        'organizationId': '',
        'hasCompletedOnboarding': true,
        'isActive': false,
      });
      expect(inactive.accountState, AvelineAccountState.suspended);
    });
  });

  group('AvelineUser.nameForDisplay', () {
    AvelineUser named({
      String? displayName,
      String firstName = '',
      String lastName = '',
    }) => AvelineUser.fromJson({
      'id': 'usr-name',
      'clerkId': 'user_name',
      'email': 'name@aveline.lk',
      'firstName': firstName,
      'lastName': lastName,
      'displayName': displayName,
      'username': 'name',
      'userRole': 'staff',
      'organizationRole': 'org:boutique_staff',
      'organizationId': 'org_colombo',
      'hasCompletedOnboarding': true,
      'accountState': 'Active',
    });

    test('prefers the display name the associate chose', () {
      expect(
        named(displayName: 'Charlotte T.', firstName: 'Charlotte')
            .nameForDisplay(),
        'Charlotte T.',
      );
    });

    test('joins the name parts when no display name is set', () {
      expect(
        named(firstName: 'Charlotte', lastName: 'Tilbury').nameForDisplay(),
        'Charlotte Tilbury',
      );
    });

    test('ignores a display name that is only whitespace', () {
      expect(
        named(
          displayName: '   ',
          firstName: 'Charlotte',
          lastName: 'Tilbury',
        ).nameForDisplay(),
        'Charlotte Tilbury',
      );
    });

    test('shows the caller words for an account with no name at all', () {
      // The rule is the domain's, the wording is the screen's: the side panel's
      // user card says `Staff Member`, an account card says `Welcome`.
      expect(named().nameForDisplay(), 'Welcome');
      expect(named().nameForDisplay(fallback: 'Staff Member'), 'Staff Member');
    });
  });
}
