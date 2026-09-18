/// A notification received over SignalR or FCM, matching the backend
/// `ReceiveNotification` contract: `{ type, title, body, data }`.
class NotificationPayload {
  const NotificationPayload({
    required this.type,
    required this.title,
    required this.body,
    this.data = const {},
  });

  final String type;
  final String title;
  final String body;
  final Map<String, String?> data;

  factory NotificationPayload.fromJson(Map<String, dynamic> json) {
    final rawData = json['data'];
    return NotificationPayload(
      type: json['type'] as String? ?? '',
      title: json['title'] as String? ?? '',
      body: json['body'] as String? ?? '',
      data: rawData is Map<String, dynamic>
          ? rawData.map((k, v) => MapEntry(k, v?.toString()))
          : const {},
    );
  }
}
