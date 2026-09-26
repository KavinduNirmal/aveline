/// Entity mentions in a Salon message (ADR-019).
///
/// Staff mark an entity with a token so the resolver looks it up instead of
/// guessing from prose: `@Samantha Arias` names a customer, `#0771234567` names a
/// number. This library finds those tokens so the Salon can draw them as pills.
///
/// **It is a mirror of the web's `lib/mentions.ts`, which is itself a mirror of the
/// resolver's own grammar** (`agent-service/app/customer_resolution/mentions.py`),
/// and it has to stay one: the pill claims "this is the entity the lookup read", so
/// a span wider or narrower than the resolver's would be a lie about what was
/// resolved. The word list below is copied from that module, and `mentions_test.dart`
/// pins the shared cases.
library;

/// Which entity a mention names.
enum MentionKind {
  /// An `@name` the resolver reads as a customer.
  customer,

  /// A `#number` the resolver reads as a phone.
  phone,
}

/// One mention: where it sits, and what the resolver captured from it.
class Mention {
  const Mention({
    required this.kind,
    required this.marker,
    required this.start,
    required this.end,
    required this.value,
  });

  /// Which entity the mention names.
  final MentionKind kind;

  /// The token that introduced it: `@` or `#`.
  final String marker;

  /// Index of the marker in the source text.
  final int start;

  /// End of the mention (exclusive): the last word the resolver kept, trailing
  /// prose excluded.
  final int end;

  /// What the resolver captured: the joined name, or the phone digits without
  /// the marker.
  final String value;

  @override
  String toString() => 'Mention(${kind.name}, $marker$value, $start..$end)';
}

/// A run of the message: either literal text or one mention.
sealed class MentionSegment {
  const MentionSegment();
}

/// Literal text between mentions, including an escaped token (`\@`): it is the
/// text someone typed, and the resolver read it as text too.
final class MentionLiteral extends MentionSegment {
  const MentionLiteral(this.text);

  final String text;

  @override
  String toString() => 'MentionLiteral($text)';
}

/// One entity the resolver read.
final class MentionEntity extends MentionSegment {
  const MentionEntity(this.mention);

  final Mention mention;

  @override
  String toString() => 'MentionEntity($mention)';
}

/// Characters that end a `@name` region outright: sentence punctuation, and the
/// other mention tokens so `@Sam #077…` and `@Sam/Jane` do not run together. An
/// apostrophe or hyphen is deliberately absent, so a name keeps its own
/// punctuation.
const Set<String> _hardBoundary = {
  '@', '#', '+', '/', r'\', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']',
  '{', '}',
};

/// Words that mean prose has resumed, so a greedy name capture drops them from
/// its tail (`@jason smith dropped by` is "jason smith"). Copied from the
/// resolver's `_POST_NAME_STOPS`.
const Set<String> _postNameStops = {
  // movement / arrival / comms often after a name in staff notes
  'dropped', 'came', 'comes', 'coming', 'visited', 'arrived', 'called',
  'messaged', 'contacted', 'sent', 'messaging',
  // wants / actions
  'wants', 'needs', 'bought', 'purchased', 'asked', 'calling',
  // auxiliaries / copula
  'will', 'would', 'can', 'could', 'should', 'shall', 'is', 'are', 'was',
  'were', 'has', 'had', 'have', 'do', 'does', 'did',
  // function words that resume prose (incl. pronouns/prepositions)
  'and', 'with', 'about', 'next', 'this', 'that', 'his', 'her', 'their', 'the',
  'a', 'an', 'on', 'for', 'at', 'to', 'in', 'of', 'by', 'later', 'then',
  'please', 'us', 'me', 'him', 'them', 'you', 'my', 'your', 'our',
  // temporal / domain cues
  'today', 'tomorrow', 'yesterday', 'recently', 'last', 'week', 'month', 'soon',
  // interrogatives / intent cues
  'any', 'event', 'events', 'occasion', 'what', 'when', 'how', 'who', 'check',
};

/// The phone a `#` introduces: an optional country-code `+` and 9–12 digits.
final RegExp _phone = RegExp(r'^\+?[0-9]{9,12}');

/// Word token: letters plus an internal apostrophe/hyphen (O'Brien, Mary-Jane).
final RegExp _word = RegExp(r"[A-Za-z][A-Za-z'’-]*");

/// True when the character at [index] is escaped by an odd run of backslashes,
/// so `\@` is a literal at-sign. Mirrors the resolver's `_is_escaped`, which
/// counts the run rather than only looking at the character before.
bool _isEscapedByRun(String text, int index) {
  var backslashes = 0;
  for (var i = index - 1; i >= 0 && text[i] == r'\'; i -= 1) {
    backslashes += 1;
  }
  return backslashes.isOdd;
}

/// Reads the phone a `#` at [at] introduces, or null when no 9-digit run follows
/// it.
Mention? _readPhone(String text, int at) {
  final match = _phone.firstMatch(text.substring(at + 1));
  if (match == null) {
    return null;
  }
  final value = match.group(0)!;
  return Mention(
    kind: MentionKind.phone,
    marker: '#',
    start: at,
    end: at + 1 + value.length,
    value: value,
  );
}

/// Reads the customer a `@` at [at] introduces, or null when no name follows it.
///
/// The name is greedy: word tokens are taken to the end of the region and
/// trailing prose words are then dropped, which is what lets
/// `@jason smith dropped by` end at "smith" without the caller having to type a
/// delimiter.
Mention? _readCustomer(String text, int at) {
  final rest = text.substring(at + 1);

  var region = rest;
  for (var i = 0; i < rest.length; i += 1) {
    if (_hardBoundary.contains(rest[i])) {
      region = rest.substring(0, i);
      break;
    }
  }

  final words = <({String value, int end})>[
    for (final match in _word.allMatches(region))
      (value: match.group(0)!, end: match.end),
  ];

  while (words.isNotEmpty &&
      _postNameStops.contains(words.last.value.toLowerCase())) {
    words.removeLast();
  }
  if (words.isEmpty) {
    return null;
  }

  return Mention(
    kind: MentionKind.customer,
    marker: '@',
    start: at,
    end: at + 1 + words.last.end,
    // Joined, exactly as the resolver joins them, so the pill shows the captured
    // name and not the spacing someone happened to type.
    value: words.map((word) => word.value).join(' '),
  );
}

/// Every mention in [text], in the order it appears.
///
/// The `#` and `@` escapes are read exactly as the resolver reads them: a `#` is
/// escaped by a single preceding backslash, while a `@` is escaped by an odd run
/// of them. That asymmetry is the resolver's own (`(?<!\\)#` beside
/// `_is_escaped`), and the pill has to agree with the lookup rather than be
/// tidier than it.
List<Mention> findMentions(String text) {
  final mentions = <Mention>[];
  var i = 0;

  while (i < text.length) {
    if (text[i] == '#' && (i == 0 || text[i - 1] != r'\')) {
      final phone = _readPhone(text, i);
      if (phone != null) {
        mentions.add(phone);
        i = phone.end;
        continue;
      }
    }
    if (text[i] == '@' && !_isEscapedByRun(text, i)) {
      final customer = _readCustomer(text, i);
      if (customer != null) {
        mentions.add(customer);
        i = customer.end;
        continue;
      }
    }
    i += 1;
  }

  return mentions;
}

/// The message as alternating literal runs and mentions, so a renderer can lift
/// each mention into a pill without touching the prose around it. An escaped
/// token (`\@`) stays in the literal run.
List<MentionSegment> splitMentions(String text) {
  final segments = <MentionSegment>[];
  var cursor = 0;

  for (final mention in findMentions(text)) {
    if (mention.start > cursor) {
      segments.add(MentionLiteral(text.substring(cursor, mention.start)));
    }
    segments.add(MentionEntity(mention));
    cursor = mention.end;
  }

  if (cursor < text.length) {
    segments.add(MentionLiteral(text.substring(cursor)));
  }
  return segments;
}
