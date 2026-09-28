import 'package:aveline_mobile/features/conversations/domain/thread_deep_link.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ThreadDeepLink.fromNotificationData', () {
    test('reads the conversation and the message a notification named', () {
      final link = ThreadDeepLink.fromNotificationData({
        'conversationId': '11111111-1111-4111-8111-111111111111',
        'messageId': '22222222-2222-4222-8222-222222222222',
      });

      expect(link, isNotNull);
      expect(link!.conversationId, '11111111-1111-4111-8111-111111111111');
      expect(link.messageId, '22222222-2222-4222-8222-222222222222');
    });

    test('opens the thread on its newest words when no message was named', () {
      final link = ThreadDeepLink.fromNotificationData({
        'conversationId': '11111111-1111-4111-8111-111111111111',
      });

      expect(link!.messageId, isNull);
    });

    test('a notification that is not about a thread is not a link', () {
      expect(
        ThreadDeepLink.fromNotificationData({'customerId': 'cus_1'}),
        isNull,
      );
      expect(ThreadDeepLink.fromNotificationData({}), isNull);
      expect(
        ThreadDeepLink.fromNotificationData({'conversationId': ''}),
        isNull,
      );
    });
  });
}
