# ADR-019: Entity Mentions for Explicit Customer Resolution

## Status
Accepted

## Context

Staff resolve customers by typing free text into the Salon (e.g. "Any events for samantha
arias?"). The shared customer resolver (`agnet-service/app/customer_resolution/`, Issue #161)
extracts a name or phone from the message deterministically, but free-text extraction is
ambiguous:

- Lower/mixed-case names ("samantha arias") are not detected because name extraction only trusts
  title-cased runs, so resolution returns `no_signal` and Ava silently skips.
- It cannot reliably tell a name ("samantha arias") from ordinary prose ("come by next week").

Asking a human to always capitalize or to bind a customer first is friction. Aveline already
renders rich Salon content; an explicit, typed **mention** gives the resolver an unambiguous
anchor while staying deterministic and cheap (no LLM needed at resolution time).

## Options Considered

1. **Heuristic free-text name extraction (status quo, improved)** — broaden detection to
   lower-case runs. Rejected: still ambiguous ("come by next week"), risks false positives, and
   is not reliable enough to be the primary path.
2. **Explicit entity mentions in chat (`@name`, `#phone`, …)** — the user marks the entity, so
   the resolver does an exact lookup instead of guessing. Chosen for customer resolution now;
   the grammar generalizes to products and events later.
3. **Web autocomplete chip binding (replace mention with `customer_id`)** — best UX but requires
   UI + API work; deferred (documented as a future phase). The existing `select-customer` binding
   (#161) already binds a `CustomerId` to a Salon conversation.

## Decision

Introduce a lightweight **entity-mention** layer that runs *before* free-text extraction in the
shared customer resolver.

Grammar (v1 — customers + phones):

| Entity | Token | Example | Name captured |
|---|---|---|---|
| Customer | `@` | `@Samantha Arias — any events?` | `Samantha Arias` |
| Customer (prose) | `@` | `@jason smith dropped by` | `jason smith` |
| Phone | `#` | `reach #0771234567` | `0771234567` |

- `@` starts a **greedy** customer name capture: consecutive alphabetic word tokens (spaces and
  apostrophes/hyphens within a token allowed) are taken until a hard delimiter (sentence
  punctuation `.,;:!?()[]` or another mention token `@ # + /`) **or** a trailing prose word in a
  small post-name stop set ("dropped", "came", "will", "any", "events", …). This lets
  `@jason smith dropped by` yield `jason smith` without an explicit end character.
- `#` is followed by an optional phone digit run (validated/normalized by the existing E.164
  phone logic).
- A backslash escapes a literal token (`\@`, `\#`).
- Mentions are **hints, not silent commands**: `resolve_customer` still returns the discriminated
  `CustomerResolution` (`resolved`/`ambiguous`/`not_found`/`no_signal`), so ambiguous matches show
  the existing customer-choice block and not-found still asks for a phone.

Resolution precedence in `resolve_customer`:
1. Explicit `customer_id` in org context (a conversation already bound via `select-customer`).
2. An explicit mention: `@name` (customer lookup) or `#phone` (phone lookup).
3. Context `phone`, then free-text `extract_phone` / `extract_customer_name` (unchanged fallback).

No API/UI change is required for this phase: staff type the mention in the raw message text and
the resolver consumes it. Rendering is unchanged.

## Consequences

- Customer resolution becomes deterministic for staff who use a mention; the free-text fallback
  remains for convenience.
- The parser is a **pure function** in `app/customer_resolution/` (fully unit-testable, no LLM).
- Future namespaces reuse the same grammar: `+`/`$` for products (Visual, Slice 2) and `/` for
  command-style hints (e.g. `/events`). Separate tokens avoid the `@`-customer vs `@`-product
  collision.
- **Deferred / future UI:** autocomplete `@` into a bound customer chip (so no string-guessing on
  the round-trip) and render customer/product/tool references as pills in the Salon. Until then the
  mention travels as raw text and only the resolver interprets it.
- Risk: the post-name stop list is heuristic and may mis-trim unusual names. Mitigated by keeping
  the stop list small, by the `no_signal`/`not_found` → ask-phone fallback, and by the future chip
  UI making mentions self-contained.

## Related
- [ADR-016](ADR-016-conversation-inbox.md) — the Salon rich-content inbox.
- Issue #161 — shared customer resolution + `select-customer` binding.
