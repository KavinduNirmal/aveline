import '../domain/app_notification.dart';
import '../domain/notification_kind.dart';
import '../domain/notification_page.dart';
import 'notification_repository.dart';

/// A boutique's notification inbox, held in memory.
///
/// Test/preview fixture only — the live path reads `ApiNotificationRepository`.
/// It mirrors `NotificationPage` and the read/dismiss rules exactly - paging, the
/// `unreadOnly` narrowing, idempotent marks, a soft delete - so a preview behaves
/// the way the API does, and the seed covers every kind the dispatcher sends so a
/// tile of each can be seen and reviewed.
///
/// Ages are measured from an injectable [clock] rather than from the wall clock,
/// so "20 minutes ago" means the same thing in a test as it does on screen.
class DemoNotificationRepository implements NotificationRepository {
  DemoNotificationRepository({
    DateTime Function()? clock,
    this.latency = const Duration(milliseconds: 300),
  }) : _clock = clock ?? DateTime.now;

  /// Where "now" comes from. Injectable so a test can pin the inbox's ages.
  final DateTime Function() _clock;

  /// How long each call takes.
  ///
  /// The seed is local, so without a delay the inbox would arrive instantly and
  /// the loading state would never be seen. Tests pass `Duration.zero`.
  final Duration latency;

  List<AppNotification>? _inbox;

  @override
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  }) async {
    await _settle();
    final visible = _visible(unreadOnly: unreadOnly);
    return NotificationPage(
      items: _slice(visible, page: page, pageSize: pageSize),
      total: visible.length,
      page: page,
      pageSize: pageSize,
    );
  }

  @override
  Future<int> fetchUnreadCount() async {
    await _settle();
    return _visible().where((item) => !item.isRead).length;
  }

  @override
  Future<void> markRead(String id) async {
    await _settle();
    final inbox = _inbox ??= _buildInbox();
    final index = inbox.indexWhere((item) => item.id == id);
    // An id the inbox does not hold, or one already read, is not a failure:
    // the endpoint is idempotent and the tile has nothing left to change.
    if (index < 0 || inbox[index].isRead) {
      return;
    }
    inbox[index] = inbox[index].copyWith(
      isRead: true,
      readAt: _clock().toUtc(),
    );
  }

  @override
  Future<void> markAllRead() async {
    await _settle();
    final inbox = _inbox ??= _buildInbox();
    final now = _clock().toUtc();
    for (var index = 0; index < inbox.length; index++) {
      if (!inbox[index].isRead) {
        inbox[index] = inbox[index].copyWith(isRead: true, readAt: now);
      }
    }
  }

  @override
  Future<void> dismiss(String id) async {
    await _settle();
    // Removed outright: the API's dismiss is a soft delete, but the inbox the
    // list reads hides it either way.
    (_inbox ??= _buildInbox()).removeWhere((item) => item.id == id);
  }

  Future<void> _settle() async {
    if (latency > Duration.zero) {
      await Future<void>.delayed(latency);
    }
  }

  /// What the list should see, newest first.
  List<AppNotification> _visible({bool unreadOnly = false}) {
    final inbox = _inbox ??= _buildInbox();
    final items = unreadOnly
        ? inbox.where((item) => !item.isRead).toList()
        : [...inbox];
    items.sort((a, b) => b.createdAt.compareTo(a.createdAt));
    return items;
  }

  List<AppNotification> _slice(
    List<AppNotification> items, {
    required int page,
    required int pageSize,
  }) {
    if (pageSize <= 0 || page < 1) {
      return const [];
    }
    final start = (page - 1) * pageSize;
    if (start >= items.length) {
      return const [];
    }
    return items.sublist(start, (start + pageSize).clamp(0, items.length));
  }

  /// One seeded notification, aged from the clock.
  AppNotification _seed({
    required String id,
    required NotificationKind kind,
    required String title,
    required String body,
    required Duration age,
    Map<String, String?> data = const {},
    bool isRead = false,
  }) {
    final createdAt = _clock().toUtc().subtract(age);
    return AppNotification(
      id: id,
      notificationId: 'nr_$id',
      type: kind.apiValue,
      title: title,
      body: body,
      data: data,
      isRead: isRead,
      readAt: isRead ? createdAt.add(const Duration(minutes: 11)) : null,
      deliveredAt: createdAt.add(const Duration(seconds: 2)),
      createdAt: createdAt,
    );
  }

  /// Eleven notifications: one of every kind, a mix of read and unread, and two
  /// that address the same client so a thread of updates reads as one.
  List<AppNotification> _buildInbox() => [
    _seed(
      id: 'un_msg_nadeesha',
      kind: NotificationKind.newMessage,
      title: 'Nadeesha Perera replied',
      body:
          'She asked whether the wine silk saree can be taken in before Friday '
          'evening, and whether the same cloth comes in a deeper shade.',
      age: const Duration(minutes: 4),
      data: {
        'conversationId': 'cnv_1041',
        'customerId': 'cus_204',
        'customerName': 'Nadeesha Perera',
      },
    ),
    _seed(
      id: 'un_approval_4821',
      kind: NotificationKind.approvalNeeded,
      title: 'Approval needed on order #4821',
      body:
          'A 12% goodwill discount for the Menaka wedding party is waiting on a '
          'manager. The order cannot be released until it is signed off.',
      age: const Duration(minutes: 26),
      data: {
        'orderId': '4821',
        'requestedBy': 'Ishara',
        'discount': '12%',
      },
    ),
    _seed(
      id: 'un_payment_4816',
      kind: NotificationKind.paymentConfirmed,
      title: 'Payment received',
      body:
          'Order #4816 settled in full at the Colombo counter, collected by the '
          'client\u2019s driver.',
      age: const Duration(hours: 2, minutes: 10),
      data: {'orderId': '4816', 'amount': 'LKR 84,500', 'method': 'Card'},
      isRead: true,
    ),
    _seed(
      id: 'un_match_ivory',
      kind: NotificationKind.newMatch,
      title: 'A match for the ivory anarkali',
      body:
          'The Visual agent found a piece in this morning\u2019s arrivals that '
          'suits Chathurika\u2019s brief, in her size.',
      age: const Duration(hours: 5, minutes: 5),
      data: {'customerId': 'cus_118', 'productId': 'prd_882'},
    ),
    _seed(
      id: 'un_fitting_chathurika',
      kind: NotificationKind.eventReminder,
      title: 'Chathurika Silva\u2019s fitting is tomorrow',
      body:
          'Fitting at the atelier with the alterations team. The blouse is '
          'pinned and ready for a second look.',
      age: const Duration(days: 1, hours: 3),
      data: {'customerId': 'cus_118', 'appointmentId': 'apt_77', 'time': '10:30'},
      isRead: true,
    ),
    _seed(
      id: 'un_vip_hasini',
      kind: NotificationKind.vipAtRisk,
      title: 'Hasini de Silva is drifting',
      body:
          'No visit in 96 days, against her usual three weeks. Her last three '
          'purchases were evening wear.',
      age: const Duration(days: 2, hours: 4),
      data: {'customerId': 'cus_091', 'daysSinceVisit': '96'},
    ),
    _seed(
      id: 'un_msg_kasun',
      kind: NotificationKind.newMessage,
      title: 'Kasun Bandara sent a photograph',
      body:
          'He sent a picture of the shirt he bought last month and asked '
          'whether it comes in a second colour.',
      age: const Duration(days: 3, hours: 6),
      data: {
        'conversationId': 'cnv_0987',
        'customerId': 'cus_233',
        'customerName': 'Kasun Bandara',
      },
      isRead: true,
    ),
    _seed(
      id: 'un_payment_4788',
      kind: NotificationKind.paymentConfirmed,
      title: 'Payment received',
      body: 'Order #4788 settled in full by bank transfer.',
      age: const Duration(days: 6, hours: 2),
      data: {'orderId': '4788', 'amount': 'LKR 32,000', 'method': 'Transfer'},
      isRead: true,
    ),
    _seed(
      id: 'un_vip_amaya',
      kind: NotificationKind.vipAtRisk,
      title: 'Amaya Fernando is drifting',
      body:
          'Her anniversary is in three weeks and she has not been in since '
          'June.',
      age: const Duration(days: 12),
      data: {'customerId': 'cus_045', 'daysSinceVisit': '112'},
      isRead: true,
    ),
    _seed(
      id: 'un_integration_whatsapp',
      kind: NotificationKind.integrationExpired,
      title: 'WhatsApp needs reconnecting',
      body:
          'The access token for the boutique\u2019s WhatsApp number has expired. '
          'Messages will not be fetched until it is reconnected.',
      age: const Duration(hours: 8, minutes: 20),
      data: {'integrationType': 'whatsapp', 'error': 'token_expired'},
      isRead: true,
    ),
    _seed(
      id: 'un_alert_stock_sync',
      kind: NotificationKind.systemAlert,
      title: 'Stock sync above threshold',
      body:
          'Failed stock synchronisations crossed the critical threshold over the '
          'last hour. Three consecutive evaluations breached it.',
      age: const Duration(days: 4, hours: 1),
      data: {
        'alertId': 'alt_3001',
        'ruleId': 'rule_stock_sync',
        'metricName': 'catalog.stock.sync_failures',
        'observedValue': '14',
        'threshold': '5',
      },
      isRead: true,
    ),
  ];
}
