/// Dates and times, in the boutique's own words.
library;

const List<String> _months = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

/// `12 Sep 2026`, in the device's own time zone.
///
/// The API sends UTC; a boutique reads its own calendar, so every label here
/// converts before it prints.
String shortDate(DateTime value) {
  final local = value.toLocal();
  return '${local.day} ${_months[local.month - 1]} ${local.year}';
}

/// `SEP`, for a calendar leaf or a compact axis label.
String monthAbbreviation(DateTime value) =>
    _months[value.toLocal().month - 1].toUpperCase();

/// `14:32`, on the 24-hour clock the counter works to.
String clockTime(DateTime value) {
  final local = value.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:'
      '${local.minute.toString().padLeft(2, '0')}';
}

/// How many calendar days separate [value] from [now]: negative in the past,
/// `0` today, positive ahead.
///
/// Compared by calendar day rather than by elapsed hours, so something at 09:00
/// tomorrow reads `1` even at 23:00 tonight. [now] is injectable so a label can
/// be tested without the clock moving under it.
int daysUntil(DateTime value, {DateTime? now}) {
  final today = _startOfDay((now ?? DateTime.now()).toLocal());
  final day = _startOfDay(value.toLocal());
  return day.difference(today).inDays;
}

/// How far off a day is, in words: `Today`, `Yesterday`, `3 days ago`,
/// `in 5 days`.
String relativeDay(DateTime value, {DateTime? now}) {
  final days = daysUntil(value, now: now);

  return switch (days) {
    0 => 'Today',
    1 => 'Tomorrow',
    -1 => 'Yesterday',
    < 0 => '${-days} days ago',
    _ => 'in $days days',
  };
}

/// `Today · 14:32`, for a log where the day matters more than the minute.
String relativeDayAndTime(DateTime value, {DateTime? now}) =>
    '${relativeDay(value, now: now)} · ${clockTime(value)}';

/// How long ago something happened, in the shortest form that is still exact:
/// `Just now`, `5m ago`, `3h ago`, `2d ago`, and then the date itself.
///
/// Measured in elapsed time rather than by calendar day, because this labels a
/// row in a feed where "2h ago" is what the reader wants and "Today" is not. Past
/// a week the elapsed figure stops being useful and [shortDate] takes over.
///
/// A timestamp ahead of [now] - a device clock running behind the server's -
/// reads as `Just now` rather than as a negative age.
String relativeMoment(DateTime value, {DateTime? now}) {
  final elapsed = (now ?? DateTime.now()).toUtc().difference(value.toUtc());

  if (elapsed.isNegative || elapsed.inMinutes < 1) {
    return 'Just now';
  }
  if (elapsed.inMinutes < 60) {
    return '${elapsed.inMinutes}m ago';
  }
  if (elapsed.inHours < 24) {
    return '${elapsed.inHours}h ago';
  }
  if (elapsed.inDays < 7) {
    return '${elapsed.inDays}d ago';
  }
  return shortDate(value);
}

/// Whether [value] falls on a later calendar day than [now].
bool isAfterToday(DateTime value, {DateTime? now}) =>
    daysUntil(value, now: now) > 0;

/// Whether [value] falls on an earlier calendar day than [now].
bool isBeforeToday(DateTime value, {DateTime? now}) =>
    daysUntil(value, now: now) < 0;

DateTime _startOfDay(DateTime value) =>
    DateTime(value.year, value.month, value.day);
