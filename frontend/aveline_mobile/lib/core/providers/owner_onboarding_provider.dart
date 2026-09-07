import 'package:flutter/foundation.dart';

import '../../features/onboarding/data/owner_onboarding_api.dart';
import '../../features/onboarding/domain/owner_onboarding.dart';

/// Drives the owner onboarding wizard: boutique details → plan → AI context →
/// complete. Holds the draft organization and current step so the wizard can
/// resume from `GET /status` and reflect tier-gated AI fields.
class OwnerOnboardingProvider extends ChangeNotifier {
  OwnerOnboardingProvider(this._api);

  final OwnerOnboardingApi _api;

  bool _isLoading = false;
  String? _errorMessage;
  OnboardingOrganization? _organization;
  int _currentStep = 2;
  bool _completed = false;
  CompleteOnboardingResult? _completion;

  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;
  OnboardingOrganization? get organization => _organization;
  int get currentStep => _currentStep;
  bool get completed => _completed;
  CompleteOnboardingResult? get completion => _completion;

  /// The plan tier currently selected (defaults to Seed until chosen).
  PlanTier get planTier => _organization?.planTier ?? PlanTier.seed;

  /// Loads resumable progress from the backend.
  Future<void> loadStatus() async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();
    try {
      final status = await _api.fetchStatus();
      _organization = status.organization;
      _currentStep = status.currentStep;
      _completed = status.hasCompletedOnboarding;
    } catch (e) {
      _errorMessage = _friendly(e);
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  Future<void> saveBoutiqueDetails(SaveBoutiqueDetailsRequest request) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();
    try {
      _organization = await _api.saveBoutiqueDetails(request);
      _currentStep = 3;
    } catch (e) {
      _errorMessage = _friendly(e);
      rethrow;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  Future<void> selectPlan(PlanTier tier) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();
    try {
      _organization = await _api.selectPlan(tier);
      _currentStep = 4;
    } catch (e) {
      _errorMessage = _friendly(e);
      rethrow;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  Future<void> saveAiCustomization(SaveAiCustomizationRequest request) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();
    try {
      _organization = await _api.saveAiCustomization(request);
      _currentStep = 5;
    } catch (e) {
      _errorMessage = _friendly(e);
      rethrow;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  Future<void> complete() async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();
    try {
      _completion = await _api.complete();
      _completed = true;
      _currentStep = 6;
    } catch (e) {
      _errorMessage = _friendly(e);
      rethrow;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  void reset() {
    _organization = null;
    _currentStep = 2;
    _completed = false;
    _completion = null;
    _errorMessage = null;
    notifyListeners();
  }

  String _friendly(Object error) {
    final message = error.toString().replaceFirst('Exception: ', '');
    return message.isEmpty ? 'Something went wrong. Please try again.' : message;
  }
}
