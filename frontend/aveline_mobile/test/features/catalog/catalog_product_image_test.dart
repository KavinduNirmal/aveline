import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/catalog_product_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The catalog delivery URL the API hands the client: `w_800,f_auto,q_auto`.
///
/// The client treats it as opaque: `f_auto` picks WebP only when the request's
/// `Accept` header says WebP is wanted, so the widget must advertise it.
const String _deliveryUrl =
    'https://res.cloudinary.com/aveline/image/upload/'
    'w_800,f_auto,q_auto/aveline/piece-001';

CatalogProduct _piece() {
  return CatalogProduct(
    id: 'piece-001',
    organizationId: 'org-1',
    name: 'Handloom Silk Saree',
    category: 'Sarees',
    color: 'Wine',
    sizes: const ['36', '38', '40'],
    price: 24500,
    cost: 12000,
    quantity: 4,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 12),
    imageUrl: _deliveryUrl,
  );
}

/// What the widget asked the decoder for and what the network request
/// advertises, whichever provider shape the widget builds.
///
/// Reading through both shapes keeps the assertions about the request (header,
/// decode size and URL) rather than about a particular constructor.
({NetworkImage? network, Map<String, String>? headers, int? width, int? height,
    ResizeImagePolicy? policy})
_decodeRequest(Image image) {
  final provider = image.image;
  if (provider is ResizeImage) {
    final inner = provider.imageProvider;
    return (
      network: inner is NetworkImage ? inner : null,
      headers: inner is NetworkImage ? inner.headers : null,
      width: provider.width,
      height: provider.height,
      policy: provider.policy,
    );
  }
  if (provider is NetworkImage) {
    return (
      network: provider,
      headers: provider.headers,
      width: null,
      height: null,
      policy: null,
    );
  }
  return (
    network: null,
    headers: null,
    width: null,
    height: null,
    policy: null,
  );
}

/// Pumps the image into a box of exactly [logicalBox] at [devicePixelRatio],
/// so the physical destination is [logicalBox] * [devicePixelRatio].
Future<void> _pumpImage(
  WidgetTester tester, {
  required Size logicalBox,
  double devicePixelRatio = 2,
}) async {
  tester.view.devicePixelRatio = devicePixelRatio;
  tester.view.physicalSize = logicalBox * devicePixelRatio;
  addTearDown(tester.view.reset);

  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: Center(
          child: SizedBox(
            width: logicalBox.width,
            height: logicalBox.height,
            child: CatalogProductImage(product: _piece()),
          ),
        ),
      ),
    ),
  );
}

void main() {
  testWidgets('the request advertises WebP so f_auto is not a silent no-op',
      (tester) async {
    await _pumpImage(tester, logicalBox: const Size(320, 400));

    final request = _decodeRequest(tester.widget<Image>(find.byType(Image)));

    expect(
      request.headers,
      const {'Accept': 'image/webp,image/jpeg,*/*'},
      reason: 'without an Accept header the CDN returns JPEG and the f_auto '
          'saving is silently lost',
    );
  });

  testWidgets('decodes to the 4:5 hero destination, not the full 800px bitmap',
      (tester) async {
    // catalog_product_screen.dart: the hero is AspectRatio(4/5) at 320 dp.
    await _pumpImage(tester, logicalBox: const Size(320, 400));

    final request = _decodeRequest(tester.widget<Image>(find.byType(Image)));

    expect(request.width, 640, reason: '320 dp * 2x DPR');
    expect(request.height, 800, reason: '400 dp * 2x DPR');
  });

  testWidgets('decodes to the grid tile destination, not the hero size',
      (tester) async {
    // 360 dp phone at 2x: (360 - 40 padding - 14 spacing) / 2 = 153 dp wide,
    // 121 dp of the 0.56-aspect card above the copy block.
    await _pumpImage(tester, logicalBox: const Size(153, 121));

    final request = _decodeRequest(tester.widget<Image>(find.byType(Image)));

    expect(request.width, 306);
    expect(request.height, 242);
  });

  testWidgets('never rewrites the delivery URL, so no second w_ is added',
      (tester) async {
    await _pumpImage(tester, logicalBox: const Size(320, 400));

    final request = _decodeRequest(tester.widget<Image>(find.byType(Image)));

    expect(request.network, isNotNull);
    expect(request.network!.url, _deliveryUrl);
    expect('w_'.allMatches(request.network!.url).length, 1);
  });

  testWidgets('never asks to decode wider than the delivered 800px bitmap',
      (tester) async {
    // A surface wider than the 800px delivery, e.g. a tablet hero.
    await _pumpImage(tester, logicalBox: const Size(600, 750));

    final request = _decodeRequest(tester.widget<Image>(find.byType(Image)));

    expect(request.width, lessThanOrEqualTo(800));
  });

  testWidgets('keeps the source aspect ratio instead of stretching to the box',
      (tester) async {
    // The grid tile is landscape and the hero is portrait, so an `exact` decode
    // would stretch every non-matching photograph. `fit` bounds the decode
    // while preserving the source's aspect ratio.
    await _pumpImage(tester, logicalBox: const Size(153, 121));

    final request = _decodeRequest(tester.widget<Image>(find.byType(Image)));

    expect(request.policy, ResizeImagePolicy.fit);
  });
}
