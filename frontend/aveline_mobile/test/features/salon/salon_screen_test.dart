import 'dart:math' as math;
import 'dart:typed_data';

import 'package:aveline_mobile/features/conversations/presentation/attachment_picker.dart';
import 'package:aveline_mobile/features/salon/presentation/screens/salon_screen.dart';
import 'package:aveline_mobile/features/salon/presentation/widgets/salon_composer.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  // The Salon's AppBar avatar animates on a repeating controller, so `pumpAndSettle`
  // never settles here. A scroll animation started from a post-frame callback also needs
  // a tick to register before it advances, so these tests pump several explicit frames.
  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 8; i++) {
      await tester.pump(const Duration(milliseconds: 120));
    }
  }

  /// A viewport small enough that the seeded thread overflows and can actually scroll.
  void useShortPhone(WidgetTester tester) {
    tester.view.physicalSize = const Size(1080, 1200);
    tester.view.devicePixelRatio = 3.0;
    addTearDown(tester.view.reset);
  }

  ScrollController threadController(WidgetTester tester) =>
      tester.widget<ListView>(find.byType(ListView)).controller!;

  testWidgets('renders the seeded thread and composer', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: SalonScreen()),
    );

    expect(find.text('The Salon'), findsOneWidget);
    expect(find.text('Message Aveline...'), findsOneWidget);
    // Seeded agent greeting is present.
    expect(find.textContaining('your boutique concierge'), findsOneWidget);
  });

  testWidgets('composer appends a staff note to the thread', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: SalonScreen()),
    );

    await tester.enterText(
      find.byType(TextField),
      'Please draft a note for Mrs. Perera.',
    );
    await tester.tap(find.byTooltip('Send message'));
    await tester.pump();

    expect(
      find.text('Please draft a note for Mrs. Perera.'),
      findsOneWidget,
    );
  });

  testWidgets('opens on the newest message, not the top of the thread',
      (tester) async {
    useShortPhone(tester);
    await tester.pumpWidget(const MaterialApp(home: SalonScreen()));
    await settle(tester);

    final controller = threadController(tester);
    expect(controller.offset, greaterThan(0));
    expect(
      controller.offset,
      moreOrLessEquals(controller.position.maxScrollExtent, epsilon: 0.5),
    );
  });

  testWidgets('jump-to-latest appears when scrolled up and returns to newest',
      (tester) async {
    useShortPhone(tester);
    await tester.pumpWidget(const MaterialApp(home: SalonScreen()));
    await settle(tester);

    // Following the thread: nothing to jump back to.
    expect(find.byTooltip('Jump to latest'), findsNothing);

    await tester.drag(find.byType(ListView), const Offset(0, 400));
    await settle(tester);

    expect(find.byTooltip('Jump to latest'), findsOneWidget);

    await tester.tap(find.byTooltip('Jump to latest'));
    await settle(tester);

    final controller = threadController(tester);
    expect(
      controller.offset,
      moreOrLessEquals(controller.position.maxScrollExtent, epsilon: 0.5),
    );
    expect(find.byTooltip('Jump to latest'), findsNothing);
  });

  testWidgets('composer stays above the keyboard', (tester) async {
    const height = 800.0;
    const keyboard = 300.0;
    tester.view.physicalSize = const Size(1080, height * 3);
    tester.view.devicePixelRatio = 3.0;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(const MaterialApp(home: SalonScreen()));
    await settle(tester);

    tester.view.viewInsets = const FakeViewPadding(bottom: keyboard * 3);
    await settle(tester);

    final composer = tester.getRect(find.byType(SalonComposer));
    expect(
      composer.bottom,
      lessThanOrEqualTo(height - keyboard),
      reason: 'the composer must sit above the on-screen keyboard',
    );
  });

  testWidgets('send icon is tilted, not flat', (tester) async {
    await tester.pumpWidget(const MaterialApp(home: SalonScreen()));

    final angles = tester
        .widgetList<Transform>(
          find.ancestor(
            of: find.byIcon(Icons.send_rounded),
            matching: find.byType(Transform),
          ),
        )
        .map((t) => math.atan2(t.transform.entry(1, 0), t.transform.entry(0, 0)));

    expect(
      angles,
      contains(moreOrLessEquals(-math.pi / 4, epsilon: 0.001)),
    );
  });

  testWidgets('stays pinned to the newest message when the keyboard opens',
      (tester) async {
    useShortPhone(tester);
    await tester.pumpWidget(const MaterialApp(home: SalonScreen()));
    await settle(tester);

    // Opening the keyboard shortens the thread, so the reader who was at the bottom must
    // stay there instead of having the newest message pushed behind the composer.
    tester.view.viewInsets = const FakeViewPadding(bottom: 360);
    await settle(tester);

    final controller = threadController(tester);
    expect(
      controller.offset,
      moreOrLessEquals(controller.position.maxScrollExtent, epsilon: 0.5),
    );
  });

  // A document rather than an image, so the tray draws an icon instead of decoding bytes.
  PickedAttachment document(String name) => PickedAttachment(
    bytes: Uint8List.fromList([1, 2, 3]),
    contentType: 'application/pdf',
    fileName: name,
  );

  Widget salonWithPicker(List<PickedAttachment> Function() files) => MaterialApp(
    home: SalonScreen(attachmentPicker: (source) async => files()),
  );

  Future<void> pickFromGallery(WidgetTester tester) async {
    await tester.tap(find.byKey(const Key('salon_attach')));
    await settle(tester);
    await tester.tap(find.byKey(const Key('salon_attach_gallery')));
    await settle(tester);
  }

  testWidgets('the paperclip picks a file and holds it in the tray', (
    tester,
  ) async {
    await tester.pumpWidget(salonWithPicker(() => [document('lookbook.pdf')]));
    await settle(tester);

    await pickFromGallery(tester);

    // The web draws its Salon drawer with the same `Composer` as its threads, so the
    // paperclip and the tray are the parity that was missing here.
    expect(find.byKey(const Key('salon_attachment_tray')), findsOneWidget);
    expect(
      find.byKey(const ValueKey('salon_pending_local_att_1')),
      findsOneWidget,
    );
  });

  testWidgets('a held file can be removed before sending', (tester) async {
    await tester.pumpWidget(salonWithPicker(() => [document('lookbook.pdf')]));
    await settle(tester);
    await pickFromGallery(tester);

    await tester.tap(find.byKey(const ValueKey('salon_remove_local_att_1')));
    await settle(tester);

    expect(find.byKey(const Key('salon_attachment_tray')), findsNothing);
  });

  testWidgets('a sixth file is refused in the API\u2019s words', (tester) async {
    await tester.pumpWidget(salonWithPicker(() => [document('lookbook.pdf')]));
    await settle(tester);

    for (var i = 0; i < 6; i++) {
      await pickFromGallery(tester);
    }

    // Refused before a byte is uploaded, in the server's own sentence, so the client
    // cannot drift from the API on either the rule or the wording.
    expect(find.byKey(const Key('salon_attachment_notice')), findsOneWidget);
    expect(
      find.text('A message may carry at most 5 attachments.'),
      findsOneWidget,
    );
  });
}
