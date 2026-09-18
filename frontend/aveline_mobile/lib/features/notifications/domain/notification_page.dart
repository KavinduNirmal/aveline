import 'app_notification.dart';

/// One page of the notification inbox, mirroring the API's `NotificationPage`.
///
/// The total is the size of the whole inbox rather than the size of the page,
/// which is what lets the list know whether asking for the next page is worth
/// it.
class NotificationPage {
  const NotificationPage({
    required this.items,
    required this.total,
    required this.page,
    required this.pageSize,
  });

  /// An inbox with nothing in it.
  static const NotificationPage empty = NotificationPage(
    items: [],
    total: 0,
    page: 1,
    pageSize: 0,
  );

  final List<AppNotification> items;

  /// How many items the inbox holds in total, across every page.
  final int total;

  final int page;
  final int pageSize;

  /// Whether the inbox holds anything after this page.
  ///
  /// Measured from how far into the inbox the page reaches rather than from
  /// `items.length < total`, which would call a page past the end of the inbox
  /// "more to come" merely because the inbox is longer than an empty page.
  bool get hasMore => (page - 1) * pageSize + items.length < total;

  factory NotificationPage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return NotificationPage(
      items: rawItems is List
          ? [
              for (final item in rawItems)
                if (item is Map)
                  AppNotification.fromJson(Map<String, dynamic>.from(item)),
            ]
          : const [],
      total: (json['total'] as num?)?.toInt() ?? 0,
      page: (json['page'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? 0,
    );
  }
}
