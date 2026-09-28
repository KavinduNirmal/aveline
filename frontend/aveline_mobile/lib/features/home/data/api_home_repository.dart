import 'package:dio/dio.dart';

import '../domain/blossom_usage.dart';
import '../domain/client_highlight.dart';
import '../domain/focus_task.dart';
import 'api_blossom_repository.dart';
import 'api_customer_tenant_repository.dart';
import 'api_home_feed_repository.dart';
import 'blossom_repository.dart';
import 'home_repository.dart';

/// Home's production source: the focus feed, the client highlights and the
/// Blossom balance, composed into one snapshot.
///
/// The organization id is read through [organizationId] at call time rather than
/// captured, because it arrives from `GET /orgs/my` after the shell mounts. A
/// null id is [OrgContextUnavailable] — a "not yet", which the controller keeps
/// as a loading state — rather than an error card.
class ApiHomeRepository implements HomeRepository {
  ApiHomeRepository(this._dio, {required this.organizationId});

  final Dio _dio;
  final String? Function() organizationId;

  String get _organizationId {
    final id = organizationId();
    if (id == null || id.isEmpty) {
      throw const OrgContextUnavailable();
    }
    return id;
  }

  @override
  Future<HomeSnapshot> fetchHome({required bool ownerDeck}) async {
    final orgId = _organizationId;
    final feed = ApiHomeFeedRepository(_dio, organizationId: orgId);
    final customers = ApiCustomerTenantRepository(_dio, organizationId: orgId);

    final tasks = await feed.fetchTasks(ownerDeck: ownerDeck);
    final clients = await customers.fetchHighlights();

    // A balance the caller may not read is a hidden meter, not a failed screen:
    // the role gate is deliberate, so its refusal is absorbed here.
    BlossomUsage? balance;
    try {
      balance = await ApiBlossomRepository(_dio).fetchBalance(organizationId: orgId);
    } on BlossomBalanceUnavailable {
      balance = null;
    }

    return HomeSnapshot(tasks: tasks, clients: clients, balance: balance);
  }

  @override
  Future<void> completeTask(FocusTask task) => ApiHomeFeedRepository(
        _dio,
        organizationId: _organizationId,
      ).dismiss(
        task,
        idempotencyKey: 'dismiss-${DateTime.now().microsecondsSinceEpoch}',
      );

  @override
  Future<ClientHighlight> createWalkIn(String fullName) =>
      ApiCustomerTenantRepository(_dio, organizationId: _organizationId)
          .createWalkIn(fullName);

  @override
  Future<void> recordVisit(String customerId) =>
      ApiCustomerTenantRepository(_dio, organizationId: _organizationId)
          .recordVisit(customerId);
}
