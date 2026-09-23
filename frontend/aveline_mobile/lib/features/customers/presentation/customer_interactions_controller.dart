import 'package:flutter/foundation.dart';

import '../data/customer_repository.dart';
import '../domain/customer.dart';
import '../domain/customer_detail.dart';

/// State management for the Customer Interaction Page.
///
/// Owns the loaded client profile, interaction history, active channel and
/// direction filters, search query, in-flight saving states, and optimistic
/// interaction recording.
class CustomerInteractionsController extends ChangeNotifier {
  CustomerInteractionsController(
    this._repository, {
    required this.customerId,
    CustomerDetail? initialDetail,
  }) : _detail = initialDetail {
    if (initialDetail != null) {
      _interactions = initialDetail.interactions;
    }
  }

  final CustomerRepository _repository;
  final String customerId;

  CustomerDetail? _detail;
  List<CustomerInteraction> _interactions = const <CustomerInteraction>[];
  bool _isLoading = false;
  bool _isSaving = false;
  String? _errorMessage;

  String _search = '';
  InteractionChannel? _channel;
  InteractionDirection? _direction;

  CustomerDetail? get detail => _detail;
  Customer? get customer => _detail?.customer;
  List<CustomerInteraction> get allInteractions => _interactions;
  bool get isLoading => _isLoading;
  bool get isSaving => _isSaving;
  String? get errorMessage => _errorMessage;

  String get search => _search;
  InteractionChannel? get channel => _channel;
  InteractionDirection? get direction => _direction;

  CustomerInteractionQuery get query => CustomerInteractionQuery(
        search: _search,
        channel: _channel,
        direction: _direction,
      );

  List<CustomerInteraction> get filteredInteractions =>
      _interactions.where(query.matches).toList();

  /// Loads the customer profile and their interaction timeline.
  Future<void> load() async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final detail = await _repository.fetchCustomer(customerId);
      if (detail != null) {
        _detail = detail;
        _interactions = detail.interactions;
        _errorMessage = null;
      } else {
        _errorMessage = 'Customer record not found.';
      }
    } catch (e) {
      _errorMessage = 'Could not load customer interactions: $e';
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Refreshes the interaction timeline silently.
  Future<void> refresh() async {
    try {
      final detail = await _repository.fetchCustomer(customerId);
      if (detail != null) {
        _detail = detail;
        _interactions = detail.interactions;
        _errorMessage = null;
        notifyListeners();
      }
    } catch (_) {
      // Retain existing state on silent refresh failure
    }
  }

  void setSearch(String value) {
    final next = value.trim();
    if (_search == next) return;
    _search = next;
    notifyListeners();
  }

  void setChannel(InteractionChannel? channel) {
    if (_channel == channel) {
      _channel = null; // Toggle off if already selected
    } else {
      _channel = channel;
    }
    notifyListeners();
  }

  void setDirection(InteractionDirection? direction) {
    if (_direction == direction) {
      _direction = null; // Toggle off if already selected
    } else {
      _direction = direction;
    }
    notifyListeners();
  }

  void clearFilters() {
    _search = '';
    _channel = null;
    _direction = null;
    notifyListeners();
  }

  /// Records a new interaction, applying optimistic updates to the UI.
  Future<CustomerInteraction?> recordInteraction(
    RecordInteractionRequest request,
  ) async {
    _isSaving = true;
    notifyListeners();

    try {
      final created = await _repository.recordInteraction(customerId, request);

      // Prepend to interaction list
      _interactions = <CustomerInteraction>[created, ..._interactions];

      // Optimistically update customer metrics in detail if counted as visit or has purchase
      if (_detail != null) {
        final currentCustomer = _detail!.customer;
        final newSpent = currentCustomer.totalSpent + (request.purchaseTotal ?? 0);
        final newVisits = currentCustomer.visitCount + (created.countedAsVisit ? 1 : 0);
        final newLastVisit = created.countedAsVisit ? request.occurredAtUtc : currentCustomer.lastVisitAtUtc;

        _detail = _detail!.copyWith(
          interactions: _interactions,
          customer: currentCustomer.copyWith(
            totalSpent: newSpent,
            visitCount: newVisits,
            lastVisitAtUtc: newLastVisit,
            status: CustomerStatus.recommend(
              totalSpent: newSpent,
              visitCount: newVisits,
              lastVisitAtUtc: newLastVisit,
            ),
          ),
        );
      }

      _isSaving = false;
      notifyListeners();
      return created;
    } catch (e) {
      _isSaving = false;
      notifyListeners();
      rethrow;
    }
  }
}
