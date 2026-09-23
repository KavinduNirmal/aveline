import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/home/presentation/screens/home_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

/// Minimal in-memory [AuthRepository] exposing just the signed-in identity.
class _FakeAuthRepository implements AuthRepository {
  @override
  bool get isSignedIn => true;

  @override
  AuthUser? get currentUser => AuthUser(id: 'u1', firstName: 'Nadia');

  @override
  bool get needsSecondFactor => false;

  @override
  String? get secondFactorStrategy => null;

  @override
  Future<String?> getToken() async => 'token';

  @override
  Future<String?> refreshToken() async => 'token';

  @override
  Future<void> signOut() async {}

  @override
  Future<String?> signInWithPassword({
    required String identifier,
    required String password,
  }) async =>
      null;

  @override
  Future<String?> sendSecondFactorCode() async => null;

  @override
  Future<String?> verifySecondFactorCode({required String code}) async => null;

  @override
  Future<String?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  }) async =>
      null;

  @override
  Future<String?> sendEmailVerificationCode() async => null;

  @override
  Future<String?> verifyEmailCode({required String code}) async => null;
}

/// The boutique's book, with one client who can be picked.
class _StubBook implements CustomerRepository {
  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    return CustomerBook([
      CustomerSection(
        letter: 'A',
        customers: [
          Customer(
            id: 'CUS-1001',
            organizationId: 'org-1',
            phoneNumber: '+94 71 445 2091',
            level: CustomerLevel.level3,
            status: CustomerStatus.returning,
            fullName: 'Anjali Perera',
            totalSpent: 184000,
            visitCount: 9,
            lastVisitAtUtc: DateTime.now().toUtc().subtract(
              const Duration(days: 4),
            ),
            createdAtUtc: DateTime.now().toUtc().subtract(
              const Duration(days: 300),
            ),
          ),
        ],
      ),
    ]);
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
        id: 'rec-home',
        channel: request.channel,
        direction: request.direction,
        createdAtUtc: request.occurredAtUtc,
      );
}

/// A phone surface, and the router Home pushes its destinations onto.
///
/// The customer route is a stand-in for the profile: what this file is about is
/// that "Log a visit" reaches it carrying the right id, not what it draws.
Future<void> _pumpHome(WidgetTester tester) async {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);

  final router = GoRouter(
    initialLocation: '/home',
    routes: [
      GoRoute(
        path: '/home',
        builder: (context, state) => Scaffold(
          body: HomeScreen(
            focusTasks: const [],
            customerRepository: _StubBook(),
          ),
        ),
      ),
      GoRoute(
        path: AppRoutes.customerPattern,
        builder: (context, state) => Scaffold(
          body: Center(
            child: Text('profile ${state.pathParameters['customerId']}'),
          ),
        ),
      ),
    ],
  );

  await tester.pumpWidget(
    Provider<AuthRepository>.value(
      value: _FakeAuthRepository(),
      child: MaterialApp.router(
        theme: AppTheme.light,
        routerConfig: router,
      ),
    ),
  );
  await tester.pump();
  await tester.pump();
}

/// Settles transition animations by hand.
///
/// `pumpAndSettle` cannot be used here: Home draws the brand's own backdrop,
/// whose blossoms drift for as long as the screen is alive, so there is no last
/// frame to settle on.
Future<void> _settle(WidgetTester tester) async {
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 500));
}

void main() {
  group('HomeScreen log a visit', () {
    testWidgets('offers the action as a floor tool', (tester) async {
      await _pumpHome(tester);

      // The row scrolls, so the action is in it whether or not it is on screen.
      expect(find.text('Log a visit'), findsOneWidget);
    });

    testWidgets('opens the client book to pick from', (tester) async {
      await _pumpHome(tester);

      await tester.tap(find.text('Log a visit'));
      await _settle(tester);

      expect(
        find.text('Pick the client who came in, then log the visit on their '
            'profile.'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('log_visit_clients')), findsOneWidget);
      expect(find.text('Anjali Perera'), findsOneWidget);
    });

    testWidgets('opens the profile of the client that was picked', (
      tester,
    ) async {
      await _pumpHome(tester);

      await tester.tap(find.text('Log a visit'));
      await _settle(tester);
      await tester.tap(find.byKey(const ValueKey('log_visit_client_CUS-1001')));

      // The sheet's own exit and the profile's entrance, pumped by hand: the
      // sheet is dismissed before the push is issued, so the profile arrives
      // over the page the picker was opened from.
      for (var i = 0; i < 20; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }

      // The id the route was addressed by, which is the only thing the profile
      // resolves from.
      expect(find.text('profile CUS-1001'), findsOneWidget);
      expect(find.byKey(const Key('log_visit_clients')), findsNothing);
    });
  });
}
