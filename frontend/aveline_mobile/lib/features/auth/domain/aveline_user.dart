import 'contact_preference.dart';

/// Account lifecycle state mirrored from the API's `AccountState`.
enum AvelineAccountState {
  onboardingPending('OnboardingPending'),
  active('Active'),
  suspended('Suspended');

  const AvelineAccountState(this.wireValue);

  /// Wire value used by `GET /api/v1/users/me`.
  final String wireValue;

  /// Parses the wire value; falls back to deriving state from the profile
  /// booleans so older payloads without an explicit state still behave correctly.
  static AvelineAccountState parse(
    String? value, {
    required bool hasCompletedOnboarding,
    required bool isActive,
    required bool hasOrgContext,
  }) {
    if (value != null) {
      for (final state in values) {
        if (state.wireValue == value) {
          return state;
        }
      }
    }
    if (!isActive) {
      return AvelineAccountState.suspended;
    }
    if (hasCompletedOnboarding && hasOrgContext) {
      return AvelineAccountState.active;
    }
    return AvelineAccountState.onboardingPending;
  }
}

class AvelineUser {
  const AvelineUser({
    required this.id,
    required this.clerkId,
    required this.email,
    required this.firstName,
    required this.lastName,
    this.displayName,
    required this.username,
    this.phoneNumber,
    this.address,
    this.profileImageUrl,
    required this.userRole,
    required this.organizationRole,
    required this.organizationId,
    required this.hasCompletedOnboarding,
    required this.accountState,
    required this.contactPreference,
    required this.pushNotificationsEnabled,
    required this.isActive,
    required this.createdAt,
    required this.updatedAt,
  });

  final String id;
  final String clerkId;
  final String email;
  final String firstName;
  final String lastName;
  final String? displayName;
  final String username;
  final String? phoneNumber;
  final String? address;
  final String? profileImageUrl;
  final String userRole;
  final String organizationRole;
  final String organizationId;
  final bool hasCompletedOnboarding;
  final AvelineAccountState accountState;
  final String contactPreference;
  final bool pushNotificationsEnabled;
  final bool isActive;
  final DateTime createdAt;
  final DateTime updatedAt;

  /// The contact preference as the settings picker reads it.
  ///
  /// The stored [contactPreference] stays a string so the record round-trips
  /// through JSON untouched; parsing it is the domain's job rather than a
  /// widget's.
  ContactPreference get preferredContact =>
      ContactPreference.fromWire(contactPreference);

  /// The name to show for this user: their chosen display name, or the parts of
  /// their name joined, which is what the app falls back to when none is set.
  ///
  /// [fallback] is what a screen shows when the account has no name at all. It is
  /// the caller's to choose because the right words differ by place - a user card
  /// says `Staff Member`, an account card says `Welcome` - while the rule that
  /// picks between them does not.
  String nameForDisplay({String fallback = 'Welcome'}) {
    final chosen = displayName?.trim();
    if (chosen != null && chosen.isNotEmpty) {
      return chosen;
    }
    final parts = [firstName, lastName].where((part) => part.trim().isNotEmpty);
    return parts.isEmpty ? fallback : parts.join(' ');
  }

  factory AvelineUser.fromJson(Map<String, dynamic> json) {
    final hasCompletedOnboarding =
        json['hasCompletedOnboarding'] as bool? ?? false;
    final isActive = json['isActive'] as bool? ?? true;
    final organizationRole = json['organizationRole'] as String? ?? '';
    final organizationId = json['organizationId'] as String? ?? '';
    final accountState = AvelineAccountState.parse(
      json['accountState'] as String?,
      hasCompletedOnboarding: hasCompletedOnboarding,
      isActive: isActive,
      hasOrgContext: organizationId.isNotEmpty || organizationRole.isNotEmpty,
    );

    return AvelineUser(
      id: json['id'] as String? ?? '',
      clerkId: json['clerkId'] as String? ?? '',
      email: json['email'] as String? ?? '',
      firstName: json['firstName'] as String? ?? '',
      lastName: json['lastName'] as String? ?? '',
      displayName: json['displayName'] as String?,
      username: json['username'] as String? ?? '',
      phoneNumber: json['phoneNumber'] as String?,
      address: json['address'] as String?,
      profileImageUrl: json['profileImageUrl'] as String?,
      userRole: json['userRole'] as String? ?? 'user',
      organizationRole: organizationRole,
      organizationId: organizationId,
      hasCompletedOnboarding: hasCompletedOnboarding,
      accountState: accountState,
      contactPreference: json['contactPreference'] as String? ?? 'None',
      pushNotificationsEnabled: json['pushNotificationsEnabled'] as bool? ?? false,
      isActive: isActive,
      createdAt: json['createdAt'] != null
          ? DateTime.tryParse(json['createdAt'] as String) ?? DateTime.now()
          : DateTime.now(),
      updatedAt: json['updatedAt'] != null
          ? DateTime.tryParse(json['updatedAt'] as String) ?? DateTime.now()
          : DateTime.now(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'clerkId': clerkId,
      'email': email,
      'firstName': firstName,
      'lastName': lastName,
      'displayName': displayName,
      'username': username,
      'phoneNumber': phoneNumber,
      'address': address,
      'profileImageUrl': profileImageUrl,
      'userRole': userRole,
      'organizationRole': organizationRole,
      'organizationId': organizationId,
      'hasCompletedOnboarding': hasCompletedOnboarding,
      'accountState': accountState.wireValue,
      'contactPreference': contactPreference,
      'pushNotificationsEnabled': pushNotificationsEnabled,
      'isActive': isActive,
      'createdAt': createdAt.toIso8601String(),
      'updatedAt': updatedAt.toIso8601String(),
    };
  }
}
