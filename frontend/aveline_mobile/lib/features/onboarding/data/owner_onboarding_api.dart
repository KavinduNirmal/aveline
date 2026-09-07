import 'package:dio/dio.dart';

import '../domain/owner_onboarding.dart';

/// Request payloads for the owner onboarding endpoints.
class SaveBoutiqueDetailsRequest {
  const SaveBoutiqueDetailsRequest({
    required this.name,
    required this.address,
    required this.phoneNumber,
    this.description,
    this.logoUrl,
    this.slug,
  });

  final String name;
  final String address;
  final String phoneNumber;
  final String? description;
  final String? logoUrl;
  final String? slug;

  Map<String, dynamic> toJson() => {
        'name': name,
        'address': address,
        'phoneNumber': phoneNumber,
        'description': description,
        'logoUrl': logoUrl,
        'slug': slug,
      };
}

class SaveAiCustomizationRequest {
  const SaveAiCustomizationRequest({
    this.brandVoice,
    this.businessRules,
    this.preferredColorsFabrics,
    this.customerPreferences,
  });

  final String? brandVoice;
  final String? businessRules;
  final String? preferredColorsFabrics;
  final String? customerPreferences;

  Map<String, dynamic> toJson() => {
        'brandVoice': brandVoice,
        'businessRules': businessRules,
        'preferredColorsFabrics': preferredColorsFabrics,
        'customerPreferences': customerPreferences,
      };
}

/// Typed client for the owner onboarding endpoints under `/api/v1/onboarding`.
class OwnerOnboardingApi {
  OwnerOnboardingApi(this._dio);

  final Dio _dio;

  Future<OnboardingStatus> fetchStatus() async {
    final response = await _dio.get('/api/v1/onboarding/status');
    return OnboardingStatus.fromJson(response.data as Map<String, dynamic>);
  }

  Future<OnboardingOrganization> saveBoutiqueDetails(
    SaveBoutiqueDetailsRequest request,
  ) async {
    final response = await _dio.post(
      '/api/v1/onboarding/owner',
      data: request.toJson(),
    );
    return OnboardingOrganization.fromJson(response.data as Map<String, dynamic>);
  }

  Future<OnboardingOrganization> selectPlan(PlanTier tier) async {
    final response = await _dio.post(
      '/api/v1/onboarding/plan',
      data: {'planTier': tier.wireValue},
    );
    return OnboardingOrganization.fromJson(response.data as Map<String, dynamic>);
  }

  Future<OnboardingOrganization> saveAiCustomization(
    SaveAiCustomizationRequest request,
  ) async {
    final response = await _dio.post(
      '/api/v1/onboarding/customize',
      data: request.toJson(),
    );
    return OnboardingOrganization.fromJson(response.data as Map<String, dynamic>);
  }

  Future<CompleteOnboardingResult> complete() async {
    final response = await _dio.post('/api/v1/onboarding/complete');
    return CompleteOnboardingResult.fromJson(
      response.data as Map<String, dynamic>,
    );
  }
}
