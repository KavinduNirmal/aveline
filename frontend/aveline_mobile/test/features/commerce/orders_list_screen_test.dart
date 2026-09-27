import 'package:aveline_mobile/features/commerce/domain/entities/order.dart';
import 'package:aveline_mobile/features/commerce/domain/repositories/commerce_repository.dart';
import 'package:aveline_mobile/features/commerce/presentation/screens/orders_list_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeCommerceRepoForOrdersList implements CommerceRepository {
  List<Order> orders = [];

  @override
  Future<List<Order>> fetchOrders({
    String? status,
    int page = 1,
    int pageSize = 20,
  }) async => orders;

  @override
  noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

void main() {
  testWidgets('OrdersListScreen renders All Orders title and has no New Order FAB', (tester) async {
    final repo = FakeCommerceRepoForOrdersList();

    await tester.pumpWidget(
      MaterialApp(
        home: OrdersListScreen(repository: repo),
      ),
    );
    await tester.pump();

    // Verify Title is 'All Orders' and not 'Commerce Orders'
    expect(find.text('All Orders'), findsOneWidget);
    expect(find.text('Commerce Orders'), findsNothing);

    // Verify FAB '+ New Order' is removed to avoid overlap with bottom navigation / chatbot bar
    expect(find.byType(FloatingActionButton), findsNothing);
    expect(find.text('New Order'), findsNothing);
  });
}
