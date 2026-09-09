import 'package:aveline_mobile/features/salon/domain/salon_message.dart';
import 'package:aveline_mobile/features/salon/presentation/widgets/message_bubble.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  SalonMessage agentMessage({double? thoughtSeconds}) => SalonMessage(
        id: 'm1',
        authorKind: 'Agent',
        agentKey: 'aveline',
        text: 'I have looked into this.',
        createdAt: DateTime(2026, 9, 9, 10, 0),
        thoughtSeconds: thoughtSeconds,
      );

  SalonMessage userMessage({MessageDeliveryStatus? status}) => SalonMessage(
        id: 'm2',
        authorKind: 'User',
        text: 'Hello',
        createdAt: DateTime(2026, 9, 9, 10, 0),
        deliveryStatus: status,
      );

  Widget wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

  testWidgets('shows a Sending… status for an optimistic staff message',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: userMessage(status: MessageDeliveryStatus.sending))));
    expect(find.text('Sending…'), findsOneWidget);
  });

  testWidgets('shows a failed status for a failed message', (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: userMessage(status: MessageDeliveryStatus.failed))));
    expect(find.text('Failed to send'), findsOneWidget);
  });

  testWidgets('shows the timestamp once confirmed', (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: userMessage())));
    expect(find.text('10:00'), findsOneWidget);
    expect(find.text('Sending…'), findsNothing);
  });

  testWidgets('shows a Thought for Xs caption on an agent message',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: agentMessage(thoughtSeconds: 0.84))));
    expect(find.text('Thought for 0.84s'), findsOneWidget);
  });

  testWidgets('agent message without thoughtSeconds shows no caption',
      (tester) async {
    await tester.pumpWidget(wrap(MessageBubble(message: agentMessage())));
    expect(find.textContaining('Thought for'), findsNothing);
  });
}
