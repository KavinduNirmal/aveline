import 'package:aveline_mobile/features/auth/domain/sign_up_field.dart';
import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SignUpField.fromName', () {
    test('maps the fields the app can ask for', () {
      expect(SignUpField.fromName('username'), SignUpField.username);
      expect(SignUpField.fromName('email_address'), SignUpField.emailAddress);
      expect(SignUpField.fromName('phone_number'), SignUpField.phoneNumber);
      expect(SignUpField.fromName('first_name'), SignUpField.firstName);
      expect(SignUpField.fromName('last_name'), SignUpField.lastName);
    });

    test('agrees with the names Clerk reports', () {
      // Pinned against the SDK's own field names so a rename upstream shows up
      // here rather than as a prompt that never appears.
      expect(SignUpField.username.fieldName, clerk.Field.username.name);
      expect(SignUpField.emailAddress.fieldName, clerk.Field.emailAddress.name);
      expect(SignUpField.firstName.fieldName, clerk.Field.firstName.name);
    });

    test('returns null for a field this screen cannot collect', () {
      expect(SignUpField.fromName('legal_accepted'), isNull);
      expect(SignUpField.fromName('password'), isNull);
      expect(SignUpField.fromName('saml'), isNull);
    });
  });

  group('SignUpField.listFromNames', () {
    test('keeps the order Clerk asks in', () {
      expect(
        SignUpField.listFromNames(['last_name', 'username']),
        [SignUpField.lastName, SignUpField.username],
      );
    });

    test('keeps what it can and drops the rest', () {
      // The production instance asks for a username, which the app can collect,
      // alongside nothing else it knows how to prompt for. Dropping the rest
      // silently is why the caller can compare against signUpIncomplete.
      expect(
        SignUpField.listFromNames(['legal_accepted', 'username']),
        [SignUpField.username],
      );
    });

    test('reports nothing when nothing is missing', () {
      expect(SignUpField.listFromNames(const []), isEmpty);
    });

    test('lists a repeated field once', () {
      expect(
        SignUpField.listFromNames(['username', 'username']),
        [SignUpField.username],
      );
    });
  });
}
