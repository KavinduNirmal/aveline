import 'dart:async';

import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/customers/presentation/customers_controller.dart';
import 'package:flutter_test/flutter_test.dart';

Customer _customer(String id, {CustomerLevel level = CustomerLevel.level1}) =>
    Customer(
      id: id,
      organizationId: 'org-1',
      phoneNumber: '+94 77 000 0000',
      level: level,
      status: CustomerStatus.fresh,
      fullName: 'Client $id',
    );

CustomerBook _book(List<String> ids) => CustomerBook([
  CustomerSection(letter: 'A', customers: [for (final id in ids) _customer(id)]),
]);

/// Serves a fixed book, or throws when asked to.
class _FixedRepository implements CustomerRepository {
  _FixedRepository({this.book = CustomerBook.empty, this.error});

  final CustomerBook book;
  final Object? error;
  final List<CustomerQuery> queries = [];

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    queries.add(query);
    if (error != null) {
      throw error!;
    }
    return book;
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async => null;

  @override
  Future<List<CustomerInteraction>> fetchCustomerInteractions(
    String customerId, {
    CustomerInteractionQuery query = const CustomerInteractionQuery(),
  }) async => const [];

  @override
  Future<CustomerInteraction> recordInteraction(
    String customerId,
    RecordInteractionRequest request,
  ) async => CustomerInteraction(
        id: 'rec-1',
        channel: request.channel,
        direction: request.direction,
        createdAtUtc: request.occurredAtUtc,
      );
}

/// Hands back one completer per call, so a reply can be held open and released
/// out of order.
class _ControlledRepository implements CustomerRepository {
  final List<Completer<CustomerBook>> pending = [];
  final List<CustomerQuery> queries = [];

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) {
    queries.add(query);
    final completer = Completer<CustomerBook>();
    pending.add(completer);
    return completer.future;
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async => null;

  @override
  Future<List<CustomerInteraction>> fetchCustomerInteractions(
    String customerId, {
    CustomerInteractionQuery query = const CustomerInteractionQuery(),
  }) async => const [];

  @override
  Future<CustomerInteraction> recordInteraction(
    String customerId,
    RecordInteractionRequest request,
  ) async => CustomerInteraction(
        id: 'rec-2',
        channel: request.channel,
        direction: request.direction,
        createdAtUtc: request.occurredAtUtc,
      );
}

void main() {
  group('CustomersController', () {
    test('starts with nothing loaded', () {
      final controller = CustomersController(_FixedRepository());
      addTearDown(controller.dispose);

      expect(controller.book.isEmpty, isTrue);
      expect(controller.hasLoadedOnce, isFalse);
      expect(controller.isLoading, isFalse);
      // An unloaded book is not "empty": the screen is still on its spinner and
      // must not show "no clients match" underneath it.
      expect(controller.isEmpty, isFalse);
    });

    test('loads the book for the narrowing it was given', () async {
      final repository = _FixedRepository(book: _book(['1', '2']));
      final controller = CustomersController(repository);
      addTearDown(controller.dispose);

      await controller.load(
        query: const CustomerQuery(
          search: 'nadia',
          level: CustomerLevel.vip,
        ),
      );

      expect(controller.book.total, 2);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
      expect(controller.errorMessage, isNull);
      expect(repository.queries.single.search, 'nadia');
      expect(repository.queries.single.level, CustomerLevel.vip);
    });

    test('reports an empty book only once a load has finished empty', () async {
      final controller = CustomersController(_FixedRepository());
      addTearDown(controller.dispose);

      await controller.load();

      expect(controller.isEmpty, isTrue);
    });

    test('drops a reply for a narrowing the associate has moved on from', () async {
      final repository = _ControlledRepository();
      final controller = CustomersController(repository);
      addTearDown(controller.dispose);

      final first = controller.load(query: const CustomerQuery(search: 'a'));
      first.ignore();
      final second = controller.load(query: const CustomerQuery(search: 'b'));
      second.ignore();

      // The narrow answer arrives after the wide one, and must not replace it.
      repository.pending[1].complete(_book(['b']));
      await second;
      repository.pending[0].complete(_book(['a']));
      await first;

      expect(controller.book.total, 1);
      expect(controller.book.sections.single.customers.single.id, 'b');
      expect(repository.queries.length, 2);
    });

    test('keeps the message from a failed load', () async {
      final controller = CustomersController(
        _FixedRepository(error: Exception('The book is unavailable.')),
      );
      addTearDown(controller.dispose);

      await controller.load();

      expect(controller.errorMessage, 'The book is unavailable.');
      expect(controller.isLoading, isFalse);
      expect(controller.book.isEmpty, isTrue);
    });

    test('re-runs the narrowing it holds when retrying', () async {
      final repository = _FixedRepository(book: _book(['1']));
      final controller = CustomersController(repository);
      addTearDown(controller.dispose);

      await controller.load(query: const CustomerQuery(search: 'maya'));
      await controller.load();

      expect(repository.queries.length, 2);
      expect(repository.queries.last.search, 'maya');
    });
  });
}
