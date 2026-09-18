import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/log_visit_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Customer _client(
  String id,
  String name, {
  CustomerLevel level = CustomerLevel.level1,
  int daysSinceVisit = 3,
}) {
  return Customer(
    id: id,
    organizationId: 'org-1',
    phoneNumber: '+94 77 000 0000',
    level: level,
    status: CustomerStatus.returning,
    fullName: name,
    totalSpent: 42000,
    visitCount: 4,
    lastVisitAtUtc: DateTime.now().toUtc().subtract(
      Duration(days: daysSinceVisit),
    ),
    createdAtUtc: DateTime.now().toUtc().subtract(const Duration(days: 200)),
  );
}

/// Serves a book of four clients under three letters, and one profile.
class _StubBook implements CustomerRepository {
  _StubBook({this.fails = false});

  final bool fails;
  int bookCalls = 0;

  final List<Customer> clients = [
    _client('CUS-1003', 'Bianca Costa', level: CustomerLevel.level2),
    _client('CUS-1005', 'Chamari Silva', level: CustomerLevel.vip),
    _client('CUS-1011', 'Eleanor Vane', level: CustomerLevel.vip),
    _client('CUS-1001', 'Anjali Perera', level: CustomerLevel.level3),
  ];

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    bookCalls++;
    if (fails) {
      throw Exception('The book is unavailable.');
    }

    // The repository is the one that sections a book, so the stub does the same
    // rather than trusting the screen to regroup it.
    final grouped = <String, List<Customer>>{};
    for (final client in clients) {
      grouped.putIfAbsent(client.sectionLetter, () => <Customer>[]).add(client);
    }
    final letters = grouped.keys.toList()..sort();
    return CustomerBook([
      for (final letter in letters)
        CustomerSection(letter: letter, customers: grouped[letter]!),
    ]);
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) async => null;
}

/// Opens the sheet from a page, and reports the client that came back.
Future<List<String>> _openSheet(
  WidgetTester tester, {
  required CustomerRepository repository,
}) async {
  final selected = <String>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => showLogVisitSheet(
                  context,
                  repository: repository,
                  onClientSelected: selected.add,
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    ),
  );

  await tester.tap(find.text('open'));
  await tester.pumpAndSettle();
  return selected;
}

/// Scrolls [finder] into the sheet's list.
///
/// The picker is a `ListView.builder`, so a row below the fold is not built yet
/// and cannot be found, let alone tapped.
Future<void> _reveal(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(
    finder,
    120,
    scrollable: find.descendant(
      of: find.byKey(const Key('log_visit_clients')),
      matching: find.byType(Scrollable),
    ),
  );
  await tester.pump();
}

void main() {
  group('LogVisitSheet', () {
    testWidgets('lists the book under the letters it files under', (
      tester,
    ) async {
      await _openSheet(tester, repository: _StubBook());

      expect(find.byKey(const Key('log_visit_clients')), findsOneWidget);
      expect(find.text('Anjali Perera'), findsOneWidget);
      expect(find.text('Bianca Costa'), findsOneWidget);

      // The letters the sheet groups by, and not one for a client it does not
      // have.
      expect(find.text('A'), findsOneWidget);
      expect(find.text('B'), findsOneWidget);
      expect(find.text('D'), findsNothing);

      // The third letter is below the fold, which is where the sheet runs out
      // of room rather than out of book.
      await _reveal(tester, find.text('Chamari Silva'));
      expect(find.text('Chamari Silva'), findsOneWidget);
      expect(find.text('C'), findsOneWidget);
    });

    testWidgets('says how recently each client was in', (tester) async {
      await _openSheet(tester, repository: _StubBook());

      // Which is the one thing that decides whether this is the client standing
      // at the counter.
      expect(find.textContaining('Last visit'), findsWidgets);
    });

    testWidgets('narrows to the name being typed', (tester) async {
      await _openSheet(tester, repository: _StubBook());

      await tester.enterText(
        find.byKey(const Key('log_visit_search_field')),
        'eleanor',
      );
      await tester.pump();

      expect(find.text('Eleanor Vane'), findsOneWidget);
      expect(find.text('Anjali Perera'), findsNothing);
      expect(find.text('Bianca Costa'), findsNothing);
      // A client the query excludes takes their letter with them.
      expect(find.text('E'), findsOneWidget);
      expect(find.text('A'), findsNothing);
    });

    testWidgets('clearing the query brings the book back', (tester) async {
      await _openSheet(tester, repository: _StubBook());

      await tester.enterText(
        find.byKey(const Key('log_visit_search_field')),
        'eleanor',
      );
      await tester.pump();
      await tester.tap(find.byKey(const Key('log_visit_search_clear')));
      await tester.pump();

      expect(find.text('Anjali Perera'), findsOneWidget);
      expect(find.text('Bianca Costa'), findsOneWidget);
    });

    testWidgets('answers a query that matches nobody', (tester) async {
      await _openSheet(tester, repository: _StubBook());

      await tester.enterText(
        find.byKey(const Key('log_visit_search_field')),
        'zzz',
      );
      await tester.pump();

      expect(find.textContaining('No client matches'), findsOneWidget);
      expect(find.byKey(const Key('log_visit_clients')), findsNothing);
    });

    testWidgets('reports the id of the client that was picked', (tester) async {
      final selected = await _openSheet(tester, repository: _StubBook());

      await _reveal(tester, find.text('Eleanor Vane'));
      await tester.tap(find.byKey(const ValueKey('log_visit_client_CUS-1011')));
      await tester.pumpAndSettle();

      // The id, not the row: the profile is addressed by the id alone.
      expect(selected, ['CUS-1011']);
      expect(find.byKey(const Key('log_visit_clients')), findsNothing);
    });

    testWidgets('stands up when the book cannot be fetched', (tester) async {
      await _openSheet(tester, repository: _StubBook(fails: true));

      // A failed lookup is an empty book, not a spinner that never resolves.
      expect(find.text('The client book is empty.'), findsOneWidget);
      expect(find.byType(CircularProgressIndicator), findsNothing);
    });
  });
}
