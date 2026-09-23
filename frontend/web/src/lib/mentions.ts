/**
 * Entity mentions in a Salon message (ADR-019).
 *
 * Staff mark an entity with a token so the resolver looks it up instead of guessing from prose:
 * `@Samantha Arias` names a customer, `#0771234567` names a number. This module finds those tokens
 * so the Salon can draw them as pills.
 *
 * **It is a mirror of the resolver's own grammar** (`agnet-service/app/customer_resolution/
 * mentions.py`), and it has to stay one: the pill claims "this is the entity the lookup read", so a
 * span that is wider or narrower than the resolver's would be a lie about what was resolved. The
 * word list below is copied from that module, and `mentions.test.ts` pins the shared cases.
 */

/** Which entity a mention names. */
export type MentionKind = 'customer' | 'phone'

/** One mention: where it sits, and what the resolver captured from it. */
export interface Mention {
  kind: MentionKind
  /** The token that introduced it. */
  marker: '@' | '#'
  /** Index of the marker in the source text. */
  start: number
  /** End of the mention (exclusive): the last word the resolver kept, trailing prose excluded. */
  end: number
  /** What the resolver captured: the joined name, or the phone digits without the marker. */
  value: string
}

/** A run of the message: either literal text or one mention. */
export type MentionSegment =
  | { kind: 'text'; text: string }
  | { kind: 'mention'; mention: Mention }

/**
 * Characters that end a `@name` region outright: sentence punctuation, and the other mention
 * tokens so `@Sam #077…` and `@Sam/Jane` do not run together. An apostrophe or hyphen is
 * deliberately absent, so a name keeps its own punctuation.
 */
const HARD_BOUNDARY = new Set([
  '@', '#', '+', '/', '\\', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}',
])

/**
 * Words that mean prose has resumed, so a greedy name capture drops them from its tail
 * (`@jason smith dropped by` is "jason smith"). Copied from the resolver's `_POST_NAME_STOPS`.
 */
const POST_NAME_STOPS = new Set([
  // movement / arrival / comms often after a name in staff notes
  'dropped', 'came', 'comes', 'coming', 'visited', 'arrived', 'called', 'messaged',
  'contacted', 'sent', 'messaging',
  // wants / actions
  'wants', 'needs', 'bought', 'purchased', 'asked', 'calling',
  // auxiliaries / copula
  'will', 'would', 'can', 'could', 'should', 'shall', 'is', 'are', 'was', 'were',
  'has', 'had', 'have', 'do', 'does', 'did',
  // function words that resume prose (incl. pronouns/prepositions)
  'and', 'with', 'about', 'next', 'this', 'that', 'his', 'her', 'their', 'the', 'a',
  'an', 'on', 'for', 'at', 'to', 'in', 'of', 'by', 'later', 'then', 'please',
  'us', 'me', 'him', 'them', 'you', 'my', 'your', 'our',
  // temporal / domain cues
  'today', 'tomorrow', 'yesterday', 'recently', 'last', 'week', 'month', 'soon',
  // interrogatives / intent cues
  'any', 'event', 'events', 'occasion', 'what', 'when', 'how', 'who', 'check',
])

/** The phone a `#` introduces: an optional country-code `+` and 9–12 digits. */
const PHONE = /^\+?[0-9]{9,12}/

/**
 * True when the character at `index` is escaped by an odd run of backslashes, so `\@` is a literal
 * at-sign. Mirrors the resolver's `_is_escaped`, which counts the run rather than only looking at
 * the character before.
 */
function isEscapedByRun(text: string, index: number): boolean {
  let backslashes = 0
  for (let i = index - 1; i >= 0 && text[i] === '\\'; i -= 1) backslashes += 1
  return backslashes % 2 === 1
}

/** Reads the phone a `#` at `at` introduces, or null when no 9-digit run follows it. */
function readPhone(text: string, at: number): Mention | null {
  const match = PHONE.exec(text.slice(at + 1))
  if (!match) return null
  return {
    kind: 'phone',
    marker: '#',
    start: at,
    end: at + 1 + match[0].length,
    value: match[0],
  }
}

/**
 * Reads the customer a `@` at `at` introduces, or null when no name follows it.
 *
 * The name is greedy: word tokens are taken to the end of the region and trailing prose words are
 * then dropped, which is what lets `@jason smith dropped by` end at "smith" without the caller
 * having to type a delimiter.
 */
function readCustomer(text: string, at: number): Mention | null {
  const rest = text.slice(at + 1)

  let region = rest
  for (let i = 0; i < rest.length; i += 1) {
    if (HARD_BOUNDARY.has(rest[i])) {
      region = rest.slice(0, i)
      break
    }
  }

  const words: { value: string; end: number }[] = []
  // A fresh regex per call: a shared `g` pattern carries `lastIndex` from one call to the next.
  const word = /[A-Za-z][A-Za-z'’-]*/g
  let match: RegExpExecArray | null
  while ((match = word.exec(region)) !== null) {
    words.push({ value: match[0], end: match.index + match[0].length })
  }

  while (words.length > 0 && POST_NAME_STOPS.has(words[words.length - 1].value.toLowerCase())) {
    words.pop()
  }
  if (words.length === 0) return null

  return {
    kind: 'customer',
    marker: '@',
    start: at,
    end: at + 1 + words[words.length - 1].end,
    // Joined, exactly as the resolver joins them, so the pill shows the captured name and not the
    // spacing someone happened to type.
    value: words.map((word) => word.value).join(' '),
  }
}

/** Every mention in `text`, in the order it appears. */
export function findMentions(text: string): Mention[] {
  const mentions: Mention[] = []
  let i = 0

  while (i < text.length) {
    if (text[i] === '#' && text[i - 1] !== '\\') {
      const phone = readPhone(text, i)
      if (phone) {
        mentions.push(phone)
        i = phone.end
        continue
      }
    }
    if (text[i] === '@' && !isEscapedByRun(text, i)) {
      const customer = readCustomer(text, i)
      if (customer) {
        mentions.push(customer)
        i = customer.end
        continue
      }
    }
    i += 1
  }

  return mentions
}

/**
 * The message as alternating literal runs and mentions, so a renderer can lift each mention into a
 * pill without touching the prose around it. An escaped token (`\@`) stays in the literal run: it
 * is the text someone typed, and the resolver read it as text too.
 */
export function splitMentions(text: string): MentionSegment[] {
  const segments: MentionSegment[] = []
  let cursor = 0

  for (const mention of findMentions(text)) {
    if (mention.start > cursor) {
      segments.push({ kind: 'text', text: text.slice(cursor, mention.start) })
    }
    segments.push({ kind: 'mention', mention })
    cursor = mention.end
  }

  if (cursor < text.length) segments.push({ kind: 'text', text: text.slice(cursor) })
  return segments
}
