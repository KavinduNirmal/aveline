/// The onboarding path a new account chooses before completing their profile.
///
/// Owners proceed to the boutique onboarding wizard; staff join an existing
/// boutique by redeeming an invitation code. The choice is persisted locally so
/// the router can resume the correct path across app restarts.
enum AccountType {
  owner('owner'),
  staff('staff');

  const AccountType(this.wireValue);

  /// Value persisted to local storage.
  final String wireValue;

  static AccountType? parse(String? value) {
    for (final type in values) {
      if (type.wireValue == value) {
        return type;
      }
    }
    return null;
  }
}
