import 'package:aveline_mobile/features/billing/data/payment_repository.dart';
import 'package:aveline_mobile/features/billing/domain/top_up.dart';
import 'package:aveline_mobile/features/billing/presentation/top_up_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class _CreatedCheckout {
  _CreatedCheckout(this.skuCode, this.idempotencyKey);

  final String skuCode;
  final String idempotencyKey;
}

class _FakePaymentRepository implements PaymentRepository {
  List<TopUpPack> packs = const [
    TopUpPack(
      skuCode: 'blossom_pack_100',
      blossomQuantity: 100,
      priceLkr: 500,
      currency: 'LKR',
    ),
    TopUpPack(
      skuCode: 'blossom_pack_500',
      blossomQuantity: 500,
      priceLkr: 2000,
      currency: 'LKR',
    ),
  ];

  int createFailures = 0;
  String polledStatus = 'Succeeded';
  String? polledFailureMessage;
  final List<_CreatedCheckout> checkouts = [];
  int pollReads = 0;

  @override
  Future<List<TopUpPack>> fetchTopUpPacks({required String organizationId}) async =>
      packs;

  @override
  Future<TopUpCheckout> createTopUpCheckout({
    required String organizationId,
    required String skuCode,
    required String idempotencyKey,
  }) async {
    checkouts.add(_CreatedCheckout(skuCode, idempotencyKey));
    if (createFailures > 0) {
      createFailures -= 1;
      throw const PaymentUnavailable('The provider is unavailable.', statusCode: 503);
    }
    return TopUpCheckout(
      paymentIntentId: 'intent-1',
      provider: 'mock',
      status: 'RequiresAction',
      skuCode: skuCode,
      blossomQuantity: 500,
      amountLkr: 2000,
      currency: 'LKR',
      checkoutUrl: 'https://checkout.example.test/intent-1',
    );
  }

  @override
  Future<PaymentIntent> fetchPaymentIntent({
    required String organizationId,
    required String paymentIntentId,
  }) async {
    pollReads += 1;
    return PaymentIntent(
      paymentIntentId: paymentIntentId,
      provider: 'mock',
      providerIntentId: 'mock_intent-1',
      purpose: 'BlossomTopUp',
      status: polledStatus,
      amountLkr: 2000,
      currency: 'LKR',
      checkoutUrl: null,
      failureCode: polledStatus == 'Failed' ? 'card_declined' : null,
      failureMessage: polledFailureMessage,
      createdAt: DateTime.utc(2026, 9, 24),
      settledAt: polledStatus == 'Succeeded' ? DateTime.utc(2026, 9, 24, 0, 5) : null,
      expiresAt: null,
    );
  }

  @override
  Future<PaymentIntent> cancelPaymentIntent({
    required String organizationId,
    required String paymentIntentId,
    required String idempotencyKey,
    String? reason,
  }) async =>
      fetchPaymentIntent(
        organizationId: organizationId,
        paymentIntentId: paymentIntentId,
      );
}

Widget _bed(
  _FakePaymentRepository repository, {
  required List<Uri> opened,
  VoidCallback? onSettled,
}) =>
    MaterialApp(
      home: Scaffold(
        body: TopUpSheet(
          repository: repository,
          organizationId: 'org-1',
          pollInterval: Duration.zero,
          onSettled: onSettled,
          openCheckout: (url) async {
            opened.add(url);
            return true;
          },
        ),
      ),
    );

void main() {
  group('TopUpSheet', () {
    testWidgets('lists the packs the server sells, with their prices',
        (tester) async {
      final repository = _FakePaymentRepository();

      await tester.pumpWidget(_bed(repository, opened: []));
      await tester.pumpAndSettle();

      expect(find.text('100 Blossoms'), findsOneWidget);
      expect(find.text('500 Blossoms'), findsOneWidget);
      expect(find.text('Rs 500'), findsOneWidget);
      expect(find.text('Rs 2,000'), findsOneWidget);
    });

    testWidgets('explains an empty price book instead of offering nothing',
        (tester) async {
      final repository = _FakePaymentRepository()..packs = const [];

      await tester.pumpWidget(_bed(repository, opened: []));
      await tester.pumpAndSettle();

      expect(
        find.text('No top-up packs are configured for this boutique.'),
        findsOneWidget,
      );
    });

    testWidgets(
        'creates the checkout once, opens the provider page, and reports the polled terminal state',
        (tester) async {
      final repository = _FakePaymentRepository();
      final opened = <Uri>[];
      var settled = 0;

      await tester.pumpWidget(
        _bed(repository, opened: opened, onSettled: () => settled += 1),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('500 Blossoms'));
      await tester.pumpAndSettle();

      expect(repository.checkouts, hasLength(1));
      expect(repository.checkouts.single.skuCode, 'blossom_pack_500');
      expect(repository.checkouts.single.idempotencyKey, isNotEmpty);
      expect(opened, [Uri.parse('https://checkout.example.test/intent-1')]);
      expect(repository.pollReads, greaterThanOrEqualTo(1));

      expect(find.text('Top-up complete'), findsOneWidget);
      expect(settled, 1);
    });

    testWidgets('never claims a settled top-up when the provider declined',
        (tester) async {
      final repository = _FakePaymentRepository()
        ..polledStatus = 'Failed'
        ..polledFailureMessage = 'insufficient_funds';

      await tester.pumpWidget(_bed(repository, opened: []));
      await tester.pumpAndSettle();

      await tester.tap(find.text('500 Blossoms'));
      await tester.pumpAndSettle();

      expect(find.text('Payment failed'), findsOneWidget);
      expect(find.textContaining('insufficient_funds'), findsOneWidget);
      expect(find.text('Top-up complete'), findsNothing);
    });

    testWidgets('keeps the same key when the same pack is retried after a refusal',
        (tester) async {
      final repository = _FakePaymentRepository()..createFailures = 1;

      await tester.pumpWidget(_bed(repository, opened: []));
      await tester.pumpAndSettle();

      // The first attempt is refused before the provider ever saw it.
      await tester.tap(find.text('500 Blossoms'));
      await tester.pumpAndSettle();
      expect(find.textContaining('unavailable'), findsOneWidget);

      // Retrying the same purchase must be the same operation to the server.
      await tester.tap(find.text('500 Blossoms'));
      await tester.pumpAndSettle();

      expect(repository.checkouts, hasLength(2));
      expect(
        repository.checkouts[0].idempotencyKey,
        repository.checkouts[1].idempotencyKey,
      );
    });
  });
}
