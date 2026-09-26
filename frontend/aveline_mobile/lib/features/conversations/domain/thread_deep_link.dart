/// The conversation a notification points at, and the message it was about.
///
/// `Notification.Data` is the only place the ids travel: the router deliberately does not carry
/// `extra`, because it re-parses its location whenever the auth or profile listenable fires. A
/// link with no `messageId` still opens the thread, on its newest words.
class ThreadDeepLink {
  const ThreadDeepLink({required this.conversationId, this.messageId});

  final String conversationId;

  /// The message to open on, when the notification named one.
  final String? messageId;

  /// The link a notification's `data` block describes, or `null` when it is not about a thread.
  static ThreadDeepLink? fromNotificationData(Map<String, String?> data) {
    final conversationId = _nonEmpty(data['conversationId']);
    if (conversationId == null) {
      return null;
    }
    return ThreadDeepLink(
      conversationId: conversationId,
      messageId: _nonEmpty(data['messageId']),
    );
  }

  static String? _nonEmpty(String? value) =>
      value == null || value.isEmpty ? null : value;

  @override
  String toString() => 'ThreadDeepLink($conversationId, $messageId)';
}
