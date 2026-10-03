/// A piece of information the instance still wants before a sign-up completes.
///
/// A social sign-up only collects what the provider will hand over, so an
/// instance that requires anything else - both Aveline instances require a
/// username - leaves the sign-up unfinished and the user without a session. The
/// app has to ask for the rest itself.
enum SignUpField {
  /// The account's username.
  username('username', 'Username'),

  /// The account's email address.
  emailAddress('email_address', 'Email'),

  /// The account's phone number.
  phoneNumber('phone_number', 'Phone number'),

  /// The account holder's first name.
  firstName('first_name', 'First name'),

  /// The account holder's last name.
  lastName('last_name', 'Last name');

  const SignUpField(this.fieldName, this.label);

  /// The name Clerk gives this field in `sign_up.missing_fields`.
  final String fieldName;

  /// How the field is labelled when the app has to ask for it.
  final String label;

  /// The field named [name], or `null` when it is one this screen cannot
  /// collect - a password on an account that signs in with a provider, say, or
  /// a legal acceptance it has no way to record.
  static SignUpField? fromName(String name) {
    for (final field in values) {
      if (field.fieldName == name) {
        return field;
      }
    }
    return null;
  }

  /// What [signUp] is still missing, in the order Clerk asks for it.
  ///
  /// Fields this screen cannot collect are dropped here rather than turned into
  /// a prompt it could not satisfy; callers that need to know whether anything
  /// was dropped should compare against [missingFields].
  static List<SignUpField> listFromNames(Iterable<String> names) {
    final fields = <SignUpField>[];
    for (final name in names) {
      final field = fromName(name);
      if (field != null && fields.contains(field) == false) {
        fields.add(field);
      }
    }
    return fields;
  }
}
