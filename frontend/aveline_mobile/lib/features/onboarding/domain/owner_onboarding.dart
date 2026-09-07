/// Plan tiers offered during owner onboarding (demo mode — no payment).
enum PlanTier {
  seed('Seed'),
  bloom('Bloom'),
  orchid('Orchid'),
  rose('Rose'),
  enterprise('Enterprise');

  const PlanTier(this.wireValue);

  final String wireValue;

  static PlanTier parse(String value) {
    for (final tier in values) {
      if (tier.wireValue == value) {
        return tier;
      }
    }
    return PlanTier.seed;
  }
}

/// Draft organization returned by the owner onboarding endpoints.
class OnboardingOrganization {
  const OnboardingOrganization({
    required this.id,
    required this.name,
    required this.slug,
    this.address,
    this.phoneNumber,
    this.description,
    this.logoUrl,
    required this.planTier,
    this.brandVoice,
    this.businessRules,
    this.preferredColorsFabrics,
    this.customerPreferences,
    required this.onboardingStep,
    required this.hasCompletedOnboarding,
  });

  final String id;
  final String name;
  final String slug;
  final String? address;
  final String? phoneNumber;
  final String? description;
  final String? logoUrl;
  final PlanTier planTier;
  final String? brandVoice;
  final String? businessRules;
  final String? preferredColorsFabrics;
  final String? customerPreferences;
  final int onboardingStep;
  final bool hasCompletedOnboarding;

  factory OnboardingOrganization.fromJson(Map<String, dynamic> json) {
    return OnboardingOrganization(
      id: json['id'] as String? ?? '',
      name: json['name'] as String? ?? '',
      slug: json['slug'] as String? ?? '',
      address: json['address'] as String?,
      phoneNumber: json['phoneNumber'] as String?,
      description: json['description'] as String?,
      logoUrl: json['logoUrl'] as String?,
      planTier: PlanTier.parse(json['planTier'] as String? ?? 'Seed'),
      brandVoice: json['brandVoice'] as String?,
      businessRules: json['businessRules'] as String?,
      preferredColorsFabrics: json['preferredColorsFabrics'] as String?,
      customerPreferences: json['customerPreferences'] as String?,
      onboardingStep: json['onboardingStep'] as int? ?? 2,
      hasCompletedOnboarding: json['hasCompletedOnboarding'] as bool? ?? false,
    );
  }
}

/// Resume payload returned by `GET /api/v1/onboarding/status`.
class OnboardingStatus {
  const OnboardingStatus({
    required this.hasCompletedOnboarding,
    required this.currentStep,
    this.organization,
  });

  final bool hasCompletedOnboarding;
  final int currentStep;
  final OnboardingOrganization? organization;

  factory OnboardingStatus.fromJson(Map<String, dynamic> json) {
    final org = json['organization'];
    return OnboardingStatus(
      hasCompletedOnboarding: json['hasCompletedOnboarding'] as bool? ?? false,
      currentStep: json['currentStep'] as int? ?? 2,
      organization: org is Map<String, dynamic>
          ? OnboardingOrganization.fromJson(org)
          : null,
    );
  }
}

/// Result of `POST /api/v1/onboarding/complete`.
class CompleteOnboardingResult {
  const CompleteOnboardingResult({
    required this.organization,
    required this.userRole,
    required this.organizationRole,
    required this.accountState,
    required this.blossomAllocation,
    required this.agentWarmedUp,
  });

  final OnboardingOrganization organization;
  final String userRole;
  final String organizationRole;
  final String accountState;
  final double blossomAllocation;
  final bool agentWarmedUp;

  factory CompleteOnboardingResult.fromJson(Map<String, dynamic> json) {
    final org = json['organization'];
    return CompleteOnboardingResult(
      organization: org is Map<String, dynamic>
          ? OnboardingOrganization.fromJson(org)
          : OnboardingOrganization.fromJson(const {}),
      userRole: json['userRole'] as String? ?? '',
      organizationRole: json['organizationRole'] as String? ?? '',
      accountState: json['accountState'] as String? ?? '',
      blossomAllocation: (json['blossomAllocation'] as num?)?.toDouble() ?? 0,
      agentWarmedUp: json['agentWarmedUp'] as bool? ?? false,
    );
  }
}
