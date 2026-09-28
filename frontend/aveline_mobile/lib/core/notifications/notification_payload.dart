/// A notification received over SignalR or FCM, matching the backend
/// `ReceiveNotification` contract: `{ type, title, body, data, notificationId, unreadCount }`.
///
/// [notificationId] and [unreadCount] are additive: an older server omits them,
/// so both are nullable and a payload without them behaves exactly as it did
/// before — the app falls back to a full inbox refresh.
class NotificationPayload {
  const NotificationPayload({
    required this.type,
    required this.title,
    required this.body,
    this.data = const {},
    this.notificationId,
    this.unreadCount,
  });

  final String type;
  final String title;
  final String body;
  final Map<String, String?> data;

  /// The per-user inbox row this payload is about, so a tap can mark it read.
  final String? notificationId;

  /// The recipient's unread count after the row was written, when the server
  /// sent one. `null` means "unknown", not zero.
  final int? unreadCount;

  factory NotificationPayload.fromJson(Map<String, dynamic> json) {
    final rawData = json['data'];
    return NotificationPayload(
      type: json['type'] as String? ?? '',
      title: json['title'] as String? ?? '',
      body: json['body'] as String? ?? '',
      data: rawData is Map<String, dynamic>
          ? rawData.map((k, v) => MapEntry(k, v?.toString()))
          : const {},
      notificationId: json['notificationId'] as String?,
      unreadCount: (json['unreadCount'] as num?)?.toInt(),
    );
  }
}
