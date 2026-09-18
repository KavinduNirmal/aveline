/// How the boutique grades a client, strongest first.
///
/// A level is the shop's own ladder rather than the API's lifecycle: a client's
/// status records where they sit in the relationship (`new`, `returning`, `vip`,
/// `dormant`), while the level is the grade the floor works with. The two are
/// shown together — this ladder is what the level row filters by, and a client's
/// status rides on their card.
///
/// Nothing here is a wire value: `CustomerProfileDto` carries a status but no
/// level yet, so the grade a client holds comes from the boutique's own grading
/// when the book is wired.
///
/// Declaration order is the order the row lists them, so the ladder reads the way
/// the boutique ranks it.
enum CustomerLevel {
  vip('VIP'),
  level3('LVL 3'),
  level2('LVL 2'),
  level1('LVL 1');

  const CustomerLevel(this.label);

  /// What the pill and the badge print.
  final String label;
}
