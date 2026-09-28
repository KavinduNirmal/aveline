import 'package:flutter/foundation.dart';

import '../data/catalog_product_repository.dart';
import '../domain/sourcing_payloads.dart';
import '../domain/sourcing_request.dart';
import '../domain/sourcing_status.dart';
import '../domain/supplier.dart';

/// Controller managing bespoke atelier sourcing requests, pipeline stages, financials, and archive lifecycle.
class SourcingController extends ChangeNotifier {
  SourcingController(this.repository);

  final CatalogProductRepository repository;

  List<SourcingRequest> _tickets = const [];
  List<Supplier> _suppliers = const [];
  SourcingStatus? _selectedStage;
  String _searchQuery = '';
  bool _showArchived = false;
  final Map<String, bool> _collapsed = {};
  bool _isLoading = false;
  bool _isMutating = false;
  String? _errorMessage;

  List<SourcingRequest> get allTickets => _tickets;
  List<Supplier> get suppliers => _suppliers;
  SourcingStatus? get selectedStage => _selectedStage;
  String get searchQuery => _searchQuery;
  bool get showArchived => _showArchived;
  bool get isLoading => _isLoading;
  bool get isMutating => _isMutating;
  String? get errorMessage => _errorMessage;

  /// Active tickets currently advancing through the pipeline.
  List<SourcingRequest> get activeTickets {
    return _tickets.where((t) {
      if (t.isArchived) return false;
      if (_selectedStage != null && t.status != _selectedStage) return false;
      if (_searchQuery.trim().isNotEmpty) {
        final q = _searchQuery.trim().toLowerCase();
        final match = t.clientName.toLowerCase().contains(q) ||
            t.category.toLowerCase().contains(q) ||
            t.color.toLowerCase().contains(q) ||
            t.itemDescription.toLowerCase().contains(q) ||
            t.supplierName.toLowerCase().contains(q);
        if (!match) return false;
      }
      return true;
    }).toList();
  }

  /// Tickets off the active pipeline stored under Archived.
  List<SourcingRequest> get archivedTickets {
    return _tickets.where((t) {
      if (!t.isArchived) return false;
      if (_searchQuery.trim().isNotEmpty) {
        final q = _searchQuery.trim().toLowerCase();
        final match = t.clientName.toLowerCase().contains(q) ||
            t.category.toLowerCase().contains(q) ||
            t.color.toLowerCase().contains(q) ||
            t.itemDescription.toLowerCase().contains(q) ||
            t.supplierName.toLowerCase().contains(q);
        if (!match) return false;
      }
      return true;
    }).toList();
  }

  /// Total count of all open active tickets regardless of current stage filter.
  int get totalActiveCount => _tickets.where((t) => t.isOpen).length;

  /// Total atelier cost volume across all open active tickets.
  double get estimatedAtelierVolume {
    return _tickets
        .where((t) => t.isOpen)
        .fold<double>(0.0, (sum, t) => sum + t.estimatedCost);
  }

  /// Average profit margin percentage across all open active tickets.
  double get averageMarginPercent {
    final open = _tickets.where((t) => t.isOpen && t.estimatedCost > 0).toList();
    if (open.isEmpty) return 0.0;
    final sum = open.fold<double>(0.0, (acc, t) => acc + t.marginPercentage);
    return sum / open.length;
  }

  /// Live count per pipeline stage.
  int countForStage(SourcingStatus stage) {
    return _tickets.where((t) => t.status == stage).length;
  }

  /// Whether a specific ticket card is folded into compact view.
  bool isCollapsed(String id) => _collapsed[id] ?? false;

  /// Whether all visible tickets in a stage are folded.
  bool allFoldedIn(SourcingStatus stage) {
    final ticketsInStage = _tickets.where((t) => t.status == stage).toList();
    if (ticketsInStage.isEmpty) return false;
    return ticketsInStage.every((t) => isCollapsed(t.id));
  }

  /// Loads sourcing tickets and partner craft ateliers.
  Future<void> loadData() async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final results = await Future.wait([
        repository.getSourcingRequests(),
        repository.getSuppliers(),
      ]);

      _tickets = results[0] as List<SourcingRequest>;
      _suppliers = results[1] as List<Supplier>;
      _isLoading = false;
      notifyListeners();
    } catch (e) {
      _errorMessage = 'Failed to load sourcing pipeline: $e';
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Filters by pipeline stage (or null for All active tickets).
  void setStageFilter(SourcingStatus? stage) {
    if (_selectedStage == stage) return;
    _selectedStage = stage;
    notifyListeners();
  }

  /// Filters tickets by text query.
  void setSearchQuery(String query) {
    _searchQuery = query;
    notifyListeners();
  }

  /// Toggles between active pipeline and archived drawer.
  void toggleShowArchived() {
    _showArchived = !_showArchived;
    notifyListeners();
  }

  /// Toggles folding state for a single ticket card.
  void toggleCollapse(String id) {
    _collapsed[id] = !isCollapsed(id);
    notifyListeners();
  }

  /// Folds or expands multiple tickets at once.
  void setCollapsedFor(List<String> ids, bool collapsed) {
    for (final id in ids) {
      _collapsed[id] = collapsed;
    }
    notifyListeners();
  }

  /// Advances or moves a ticket to a new pipeline stage.
  Future<bool> updateTicketStatus(String id, SourcingStatus newStatus, {String? notes}) async {
    _isMutating = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final updated = await repository.updateSourcingStatus(id, newStatus, notes: notes);
      _tickets = _tickets.map((t) => t.id == id ? updated : t).toList();
      _isMutating = false;
      notifyListeners();
      return true;
    } catch (e) {
      _errorMessage = 'Could not update status: $e';
      _isMutating = false;
      notifyListeners();
      return false;
    }
  }

  /// Archives a ticket off the active pipeline, returning previous status for undo.
  Future<SourcingStatus?> archiveTicket(SourcingRequest ticket) async {
    final previousStatus = ticket.status;
    final success = await updateTicketStatus(ticket.id, SourcingStatus.archived);
    return success ? previousStatus : null;
  }

  /// Restores an archived ticket back to the top of the pipeline (Pending Quote) or its previous stage.
  Future<bool> restoreTicket(SourcingRequest ticket, {SourcingStatus targetStage = SourcingStatus.pending}) async {
    return updateTicketStatus(ticket.id, targetStage);
  }

  /// Creates a new bespoke commission ticket and adds it to the active pool.
  Future<SourcingRequest?> createTicket(CreateSourcingRequestPayload payload) async {
    _isMutating = true;
    _errorMessage = null;
    notifyListeners();

    try {
      final created = await repository.createSourcingRequest(payload);
      _tickets = [created, ..._tickets];
      _isMutating = false;
      notifyListeners();
      return created;
    } catch (e) {
      _errorMessage = 'Could not create sourcing request: $e';
      _isMutating = false;
      notifyListeners();
      return null;
    }
  }
}
