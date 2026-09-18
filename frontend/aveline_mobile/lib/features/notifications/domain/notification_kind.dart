/// The kinds of notification the gateway dispatches, and the words the inbox
/// wears for each.
///
/// The API carries the kind as a string (`NewMessage`, `ApprovalNeeded`, ...)
/// because the enum on the server is the wire contract and clients are expected
/// to tolerate a value they have never met. A kind this build does not know is
/// [unknown] rather than a parse failure, so a newer backend can add a
/// notification type without breaking an older app.
///
/// The icon and the tint for each kind are presentation, not domain, and live in
/// `presentation/widgets/notification_kind_visuals.dart`.
enum NotificationKind {
  newMessage('NewMessage', 'New message'),
  approvalNeeded('ApprovalNeeded', 'Approval needed'),
  paymentConfirmed('PaymentConfirmed', 'Payment confirmed'),
  vipAtRisk('VipAtRisk', 'VIP at risk'),
  eventReminder('EventReminder', 'Event reminder'),
  newMatch('NewMatch', 'New match'),

  /// A kind this build has not been taught. It still renders: a notification
  /// nobody can see is worse than one wearing a generic label.
  unknown('', 'Notification');

  const NotificationKind(this.apiValue, this.label);

  /// The `type` the API sends for this kind.
  final String apiValue;

  /// The words the tile shows.
  final String label;

  /// The kind [type] names, or [unknown] when this build has not met it.
  ///
  /// Matched case-insensitively and after trimming: the type arrives as a plain
  /// string from a server this app does not control.
  static NotificationKind fromType(String type) {
    final needle = type.trim().toLowerCase();
    for (final kind in values) {
      if (kind != unknown && kind.apiValue.toLowerCase() == needle) {
        return kind;
      }
    }
    return unknown;
  }
}
