import 'notification_kind.dart';

/// One item in the signed-in user's notification inbox, mirroring the API's
/// `UserNotificationDto`.
///
/// This is the per-user inbox row, not the dispatched notification: it carries
/// the user's own read state and its own id, which is what the read and dismiss
/// endpoints are addressed by. The `type` is kept as the raw string the API sent
/// and the [kind] is derived from it, so the two cannot drift apart and an
/// unknown type still round-trips.
class AppNotification {
  const AppNotification({
    required this.id,
    required this.notificationId,
    required this.type,
    required this.title,
    required this.body,
    required this.createdAt,
    this.data = const {},
    this.isRead = false,
    this.readAt,
    this.deliveredAt,
  });

  /// The inbox row's id, which the read and dismiss endpoints are addressed by.
  final String id;

  /// The dispatched notification behind this row.
  final String notificationId;

  /// The API's `type`, e.g. `PaymentConfirmed`.
  final String type;

  final String title;
  final String body;

  /// The payload the dispatcher attached: an order id, a client id, a thread id.
  /// Every value is nullable because the contract says so.
  final Map<String, String?> data;

  final bool isRead;

  /// When the user read it; `null` while unread.
  final DateTime? readAt;

  /// When a delivery channel last succeeded; `null` if none has.
  final DateTime? deliveredAt;

  final DateTime createdAt;

  /// The kind [type] names, or [NotificationKind.unknown].
  NotificationKind get kind => NotificationKind.fromType(type);

  /// The client this notification is about, when it carries one.
  ///
  /// Promoted to a getter because three of the six kinds address a client and
  /// the tile should not each decide what the key is called.
  String? get customerId {
    final id = data['customerId'];
    return id == null || id.isEmpty ? null : id;
  }

  /// The thread this notification is about, when it carries one.
  ///
  /// A notification about a thread addresses the conversation, not the client: the thread may
  /// be one whose client is not identified yet.
  String? get conversationId {
    final id = data['conversationId'];
    return id == null || id.isEmpty ? null : id;
  }

  /// The message the notification was about, so the thread can open on it.
  String? get messageId {
    final id = data['messageId'];
    return id == null || id.isEmpty ? null : id;
  }

  /// Whether tapping this notification has anywhere to go.
  bool get isOpenable => customerId != null || conversationId != null;

  factory AppNotification.fromJson(Map<String, dynamic> json) {
    return AppNotification(
      id: json['id']?.toString() ?? '',
      notificationId: json['notificationId']?.toString() ?? '',
      type: json['type'] as String? ?? '',
      title: json['title'] as String? ?? '',
      body: json['body'] as String? ?? '',
      data: _dataFrom(json['data']),
      isRead: json['isRead'] as bool? ?? false,
      readAt: _timeFrom(json['readAt']),
      deliveredAt: _timeFrom(json['deliveredAt']),
      // The API always sends this. A payload that does not is malformed, and
      // reading it as "just now" is kinder than stamping it 1970.
      createdAt: _timeFrom(json['createdAt']) ?? DateTime.now().toUtc(),
    );
  }

  /// A copy with the read state changed.
  ///
  /// Only the read state is copyable: it is the one field the app mutates
  /// optimistically, and a general-purpose copyWith would invite the UI to
  /// rewrite what the server owns.
  AppNotification copyWith({bool? isRead, DateTime? readAt}) {
    return AppNotification(
      id: id,
      notificationId: notificationId,
      type: type,
      title: title,
      body: body,
      createdAt: createdAt,
      data: data,
      isRead: isRead ?? this.isRead,
      readAt: readAt ?? this.readAt,
      deliveredAt: deliveredAt,
    );
  }

  /// The `data` block, as a map of nullable strings.
  ///
  /// The contract types the values as nullable strings, but a payload that
  /// carries a number or a list must not take the inbox down with it.
  static Map<String, String?> _dataFrom(Object? raw) {
    if (raw is! Map) {
      return const {};
    }
    return {
      for (final entry in raw.entries)
        entry.key.toString(): entry.value?.toString(),
    };
  }

  /// An ISO-8601 timestamp, or `null` when it is absent or unreadable.
  static DateTime? _timeFrom(Object? raw) {
    if (raw is! String) {
      return null;
    }
    return DateTime.tryParse(raw);
  }

  @override
  String toString() => 'AppNotification($id, $type, isRead: $isRead)';
}
