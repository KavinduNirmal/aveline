import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/auth/domain/contact_preference.dart';
import 'package:flutter_test/flutter_test.dart';

AvelineUser _user({String contactPreference = 'None'}) {
  return AvelineUser.fromJson({
    'id': 'usr_1',
    'clerkId': 'clerk_1',
    'email': 'charlotte@aveline.com',
    'firstName': 'Charlotte',
    'lastName': 'Tilbury',
    'username': 'charlotte',
    'userRole': 'staff',
    'organizationRole': 'org:boutique_staff',
    'organizationId': 'org_1',
    'hasCompletedOnboarding': true,
    'contactPreference': contactPreference,
    'pushNotificationsEnabled': true,
  });
}

void main() {
  group('ContactPreference', () {
    test('spells every wire value the API defines', () {
      // The enum has to answer the API in the API's own words: `ContactPreferences`
      // in `docs/api/openapi.yaml` is exactly these five, spelled this way.
      expect(
        ContactPreference.values.map((preference) => preference.wireValue),
        ['Email', 'Phone', 'SMS', 'WhatsApp', 'None'],
      );
    });

    test('reads a wire value whatever case it arrives in', () {
      expect(ContactPreference.fromWire('WhatsApp'), ContactPreference.whatsApp);
      expect(ContactPreference.fromWire('sms'), ContactPreference.sms);
      expect(ContactPreference.fromWire(' email '), ContactPreference.email);
    });

    test('falls back to none when there is no preference to read', () {
      expect(ContactPreference.fromWire(null), ContactPreference.none);
      expect(ContactPreference.fromWire(''), ContactPreference.none);
      expect(ContactPreference.fromWire('Carrier pigeon'), ContactPreference.none);
    });

    test('labels every preference for the picker', () {
      expect(ContactPreference.whatsApp.label, 'WhatsApp');
      // Spelled out rather than left as the wire's bare `None`, because the row
      // reads `Preferred contact: ...` and `None` there answers a different
      // question than the associate asked.
      expect(ContactPreference.none.label, 'No preference');
      expect(
        ContactPreference.values.every(
          (preference) =>
              preference.label.isNotEmpty && preference.description.isNotEmpty,
        ),
        isTrue,
      );
    });
  });

  group('AvelineUser.preferredContact', () {
    test('reads the stored string as the enum', () {
      expect(
        _user(contactPreference: 'SMS').preferredContact,
        ContactPreference.sms,
      );
    });

    test('reads an absent preference as none', () {
      expect(_user(contactPreference: '').preferredContact, ContactPreference.none);
    });
  });
}
