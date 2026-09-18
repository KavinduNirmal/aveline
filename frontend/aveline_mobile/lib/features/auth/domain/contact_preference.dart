/// How a user prefers to be contacted, mirroring the API's `ContactPreferences`.
///
/// The stored field is a plain string so the record round-trips through JSON
/// untouched. This enum is what lets a picker speak in labels and answer in wire
/// values, instead of a screen hard-coding one spelling while the API expects the
/// other.
enum ContactPreference {
  email('Email', 'Email', 'Replies by email.'),
  phone('Phone', 'Phone call', 'A call to the number on file.'),
  sms('SMS', 'Text message', 'A short text to the number on file.'),
  whatsApp('WhatsApp', 'WhatsApp', 'A message on WhatsApp.'),
  none('None', 'No preference', 'However is easiest at the time.');

  const ContactPreference(this.wireValue, this.label, this.description);

  /// The spelling `PATCH /api/v1/users/me` reads and `UserDto` writes.
  final String wireValue;

  /// What the settings row and the picker call it.
  final String label;

  /// The line under the label in the picker.
  final String description;

  /// Reads a stored preference, falling back to [none] for anything unrecognised.
  ///
  /// Tolerant of case and stray whitespace because the field is free text on the
  /// wire, and a preference this build does not know about must not stop the
  /// associate reaching the rest of their settings.
  static ContactPreference fromWire(String? value) {
    if (value == null) {
      return ContactPreference.none;
    }
    final trimmed = value.trim();
    for (final preference in values) {
      if (preference.wireValue.toLowerCase() == trimmed.toLowerCase()) {
        return preference;
      }
    }
    return ContactPreference.none;
  }
}
