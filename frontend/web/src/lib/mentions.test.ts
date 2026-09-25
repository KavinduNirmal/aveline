import { describe, expect, it } from 'vitest'

import { findMentions, splitMentions } from './mentions'

/**
 * The rule this file exists to enforce: **a pill covers exactly what the resolver read.**
 *
 * The parser here is a mirror of `agent-service/app/customer_resolution/mentions.py`, and the Salon
 * draws a pill over the span it returns. If the two drift, the UI starts claiming the lookup used
 * an entity it never saw — a mention that ends a word early, or swallows the prose after a name.
 * The cases below are the resolver's own examples plus the edges its grammar turns on.
 */
describe('findMentions', () => {
  it('captures a title-cased customer name', () => {
    const [mention] = findMentions('@Samantha Arias — any events?')

    expect(mention).toMatchObject({ kind: 'customer', marker: '@', start: 0, value: 'Samantha Arias' })
    // The em dash and the question are not part of the entity.
    expect('@Samantha Arias — any events?'.slice(mention.start, mention.end)).toBe('@Samantha Arias')
  })

  it('captures a lower-case name and drops the prose that follows it', () => {
    // The ADR's own example: no capitalisation, no delimiter, and "dropped by" is not a surname.
    const [mention] = findMentions('@jason smith dropped by')

    expect(mention.value).toBe('jason smith')
    expect('@jason smith dropped by'.slice(mention.start, mention.end)).toBe('@jason smith')
  })

  it('stops a name at sentence punctuation', () => {
    const [mention] = findMentions('@Samantha Arias, any events?')

    expect(mention.value).toBe('Samantha Arias')
    expect(mention.end).toBe('@Samantha Arias'.length)
  })

  it('stops a name at the next mention token', () => {
    const [customer, phone] = findMentions('@Sam #0771234567')

    expect(customer.value).toBe('Sam')
    expect(phone.value).toBe('0771234567')
    expect(phone.start).toBe('@Sam '.length)
  })

  it('keeps an apostrophe or a hyphen inside a name', () => {
    expect(findMentions("@O'Brien")[0].value).toBe("O'Brien")
    expect(findMentions('@Mary-Jane')[0].value).toBe('Mary-Jane')
    // "will" is a stop word, so the hyphenated name survives a trailing auxiliary.
    expect(findMentions('@Mary-Jane will')[0].value).toBe('Mary-Jane')
  })

  it('joins a name the way the resolver joins it, whatever the spacing typed', () => {
    const text = '@Samantha   Arias'
    const [mention] = findMentions(text)

    // The pill spans the raw text, but shows the captured name: the resolver's lookup value.
    expect(mention.value).toBe('Samantha Arias')
    expect(text.slice(mention.start, mention.end)).toBe(text)
  })

  it('reads a phone with or without a country code', () => {
    expect(findMentions('reach #0771234567')[0]).toMatchObject({
      kind: 'phone',
      value: '0771234567',
    })
    expect(findMentions('#+94771234567')[0].value).toBe('+94771234567')
  })

  it('needs nine digits before a # is a phone', () => {
    expect(findMentions('#07712345')).toEqual([])
    expect(findMentions('#0771234567890123')[0].value).toBe('077123456789')
  })

  it('treats a backslash-escaped token as literal text', () => {
    expect(findMentions('\\@Sam')).toEqual([])
    expect(findMentions('\\#0771234567')).toEqual([])
    // An even run of backslashes escapes the backslash, not the token.
    expect(findMentions('\\\\@Sam')[0].value).toBe('Sam')
  })

  it('finds every mention, not only the first', () => {
    const mentions = findMentions('@Sam and @Jason came by')

    expect(mentions.map((mention) => mention.value)).toEqual(['Sam', 'Jason'])
  })

  it('ignores a token with no name behind it', () => {
    expect(findMentions('@')).toEqual([])
    expect(findMentions('@ dropped by')).toEqual([])
    expect(findMentions('@ , and then')).toEqual([])
  })

  it('reads an address as the resolver reads it, even when that is a false positive', () => {
    // `sam@example.com` really is a `@name` to this grammar, and the resolver resolves "example"
    // from it. The pill has to agree with the resolver rather than be cleverer than it.
    const [mention] = findMentions('sam@example.com')

    expect(mention.value).toBe('example')
  })

  it('returns nothing for text with no mentions', () => {
    expect(findMentions('')).toEqual([])
    expect(findMentions('Any pinkish gowns for an evening reception?')).toEqual([])
  })
})

describe('splitMentions', () => {
  it('splits a message into literal runs and mentions without losing a character', () => {
    const text = 'Ask @Samantha Arias or #0771234567 about it.'

    const segments = splitMentions(text)

    expect(segments.map((segment) => segment.kind)).toEqual([
      'text',
      'mention',
      'text',
      'mention',
      'text',
    ])
    // Reassembling the raw mentions has to give the original message back, exactly.
    const rebuilt = segments
      .map((segment) =>
        segment.kind === 'text'
          ? segment.text
          : `${segment.mention.marker}${text.slice(segment.mention.start + 1, segment.mention.end)}`,
      )
      .join('')
    expect(rebuilt).toBe(text)
  })

  it('keeps an escaped token in the literal run', () => {
    expect(splitMentions('use \\@ for a literal')).toEqual([
      { kind: 'text', text: 'use \\@ for a literal' },
    ])
  })

  it('returns the whole message as one run when there is no mention', () => {
    expect(splitMentions('Hello there')).toEqual([{ kind: 'text', text: 'Hello there' }])
  })

  it('returns nothing for empty text', () => {
    expect(splitMentions('')).toEqual([])
  })
})
