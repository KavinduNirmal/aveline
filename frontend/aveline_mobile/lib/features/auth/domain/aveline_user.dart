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
  final String contactPreference;
  final bool pushNotificationsEnabled;
  final bool isActive;
  final DateTime createdAt;
  final DateTime updatedAt;

  factory AvelineUser.fromJson(Map<String, dynamic> json) {
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
      organizationRole: json['organizationRole'] as String? ?? '',
      organizationId: json['organizationId'] as String? ?? '',
      hasCompletedOnboarding: json['hasCompletedOnboarding'] as bool? ?? false,
      contactPreference: json['contactPreference'] as String? ?? 'None',
      pushNotificationsEnabled: json['pushNotificationsEnabled'] as bool? ?? false,
      isActive: json['isActive'] as bool? ?? true,
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
      'contactPreference': contactPreference,
      'pushNotificationsEnabled': pushNotificationsEnabled,
      'isActive': isActive,
      'createdAt': createdAt.toIso8601String(),
      'updatedAt': updatedAt.toIso8601String(),
    };
  }
}
