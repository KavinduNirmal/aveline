import '../router/route_guards.dart';

/// Where a notification goes, decided from its ids alone.
///
/// One rule, shared by the inbox tile ([notificationRouteFor]) and a push tap, so
/// the two can never disagree about what a notification opens. A conversation
/// wins over a client: a thread may be one whose client is not identified yet, so
/// the conversation is the only target that always exists. A client opens the
/// client book. Nothing else opens.
///
/// Pure, so it is testable without a router.
String? notificationRouteForIds({
  String? conversationId,
  String? messageId,
  String? customerId,
}) {
  final conversation = _nonEmpty(conversationId);
  if (conversation != null) {
    return AppRoutes.thread(conversation, messageId: _nonEmpty(messageId));
  }

  final customer = _nonEmpty(customerId);
  return customer == null ? null : AppRoutes.customer(customer);
}

String? _nonEmpty(String? value) =>
    value == null || value.isEmpty ? null : value;
