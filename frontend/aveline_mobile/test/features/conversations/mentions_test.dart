import 'package:aveline_mobile/features/conversations/domain/mentions.dart';
import 'package:flutter_test/flutter_test.dart';

/// The rule this file exists to enforce: **a pill covers exactly what the resolver
/// read.**
///
/// The parser here mirrors the web's `lib/mentions.ts`, which mirrors
/// `agnet-service/app/customer_resolution/mentions.py`. The Salon draws a pill over
/// the span it returns, so if the two drift the UI starts claiming the lookup used
/// an entity it never saw — a mention that ends a word early, or swallows the prose
/// after a name. The cases below are the resolver's own examples plus the edges its
/// grammar turns on, copied from the web's `mentions.test.ts` so a change to either
/// parser fails on both platforms.
void main() {
  group('findMentions', () {
    test('captures a title-cased customer name', () {
      const text = '@Samantha Arias — any events?';
      final mention = findMentions(text).single;

      expect(mention.kind, MentionKind.customer);
      expect(mention.marker, '@');
      expect(mention.start, 0);
      expect(mention.value, 'Samantha Arias');
      // The em dash and the question are not part of the entity.
      expect(text.substring(mention.start, mention.end), '@Samantha Arias');
    });

    test('captures a lower-case name and drops the prose that follows it', () {
      // The ADR's own example: no capitalisation, no delimiter, and "dropped by" is
      // not a surname.
      const text = '@jason smith dropped by';
      final mention = findMentions(text).single;

      expect(mention.value, 'jason smith');
      expect(text.substring(mention.start, mention.end), '@jason smith');
    });

    test('stops a name at sentence punctuation', () {
      const text = '@Samantha Arias, any events?';
      final mention = findMentions(text).single;

      expect(mention.value, 'Samantha Arias');
      expect(mention.end, '@Samantha Arias'.length);
    });

    test('stops a name at the next mention token', () {
      final mentions = findMentions('@Sam #0771234567');

      expect(mentions, hasLength(2));
      expect(mentions[0].value, 'Sam');
      expect(mentions[1].kind, MentionKind.phone);
      expect(mentions[1].value, '0771234567');
      expect(mentions[1].start, '@Sam '.length);
    });

    test('keeps an apostrophe or a hyphen inside a name', () {
      expect(findMentions("@O'Brien").single.value, "O'Brien");
      expect(findMentions('@Mary-Jane').single.value, 'Mary-Jane');
      // "will" is a stop word, so the hyphenated name survives a trailing auxiliary.
      expect(findMentions('@Mary-Jane will').single.value, 'Mary-Jane');
    });

    test('joins a name the way the resolver joins it, whatever the spacing typed', () {
      const text = '@Samantha   Arias';
      final mention = findMentions(text).single;

      // The pill spans the raw text, but shows the captured name: the resolver's
      // lookup value.
      expect(mention.value, 'Samantha Arias');
      expect(text.substring(mention.start, mention.end), text);
    });

    test('reads a phone with or without a country code', () {
      final national = findMentions('reach #0771234567').single;
      expect(national.kind, MentionKind.phone);
      expect(national.value, '0771234567');

      expect(findMentions('#+94771234567').single.value, '+94771234567');
    });

    test('needs nine digits before a # is a phone', () {
      expect(findMentions('#07712345'), isEmpty);
      expect(findMentions('#0771234567890123').single.value, '077123456789');
    });

    test('treats a backslash-escaped token as literal text', () {
      expect(findMentions(r'\@Sam'), isEmpty);
      expect(findMentions(r'\#0771234567'), isEmpty);
      // An even run of backslashes escapes the backslash, not the token.
      expect(findMentions(r'\\@Sam').single.value, 'Sam');
    });

    test('finds every mention, not only the first', () {
      final mentions = findMentions('@Sam and @Jason came by');

      expect(
        mentions.map((mention) => mention.value).toList(),
        ['Sam', 'Jason'],
      );
    });

    test('ignores a token with no name behind it', () {
      expect(findMentions('@'), isEmpty);
      expect(findMentions('@ dropped by'), isEmpty);
      expect(findMentions('@ , and then'), isEmpty);
    });

    test('reads an address as the resolver reads it, even when that is a false positive', () {
      // `sam@example.com` really is a `@name` to this grammar, and the resolver
      // resolves "example" from it. The pill has to agree with the resolver rather
      // than be cleverer than it.
      expect(findMentions('sam@example.com').single.value, 'example');
    });

    test('returns nothing for text with no mentions', () {
      expect(findMentions(''), isEmpty);
      expect(
        findMentions('Any pinkish gowns for an evening reception?'),
        isEmpty,
      );
    });
  });

  group('splitMentions', () {
    test('splits a message into literal runs and mentions without losing a character', () {
      const text = 'Ask @Samantha Arias or #0771234567 about it.';

      final segments = splitMentions(text);

      expect(
        segments.map((segment) => segment is MentionEntity ? 'mention' : 'text'),
        ['text', 'mention', 'text', 'mention', 'text'],
      );

      // Reassembling the raw mentions has to give the original message back, exactly.
      final rebuilt = segments.map((segment) {
        if (segment is MentionLiteral) {
          return segment.text;
        }
        final mention = (segment as MentionEntity).mention;
        return text.substring(mention.start, mention.end);
      }).join();
      expect(rebuilt, text);
    });

    test('keeps an escaped token in the literal run', () {
      final segments = splitMentions(r'use \@ for a literal');

      expect(segments, hasLength(1));
      expect((segments.single as MentionLiteral).text, r'use \@ for a literal');
    });

    test('returns the whole message as one run when there is no mention', () {
      final segments = splitMentions('Hello there');

      expect(segments, hasLength(1));
      expect((segments.single as MentionLiteral).text, 'Hello there');
    });

    test('returns nothing for empty text', () {
      expect(splitMentions(''), isEmpty);
    });
  });
}
