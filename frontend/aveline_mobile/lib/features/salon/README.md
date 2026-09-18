# Feature: Salon (Conversations)

The unified conversation inbox ("The Salon") where staff and Aveline's agents
(Aveline, Ava, Elle, Lina) participate in one persistent thread per customer.

## Status

Static first pass. The full-screen Salon is opened from the dock's center
launcher (the dock is hidden there). It renders a seeded thread with persona
attribution and a local composer that appends staff notes client-side.

Realtime (`/hubs/conversations`) and the conversations data layer land in a
later pass (see `conversation_messaging_implementation.ignore.md`, Phase 7).

## Layers

### `domain/`
- `salon_message.dart` — lightweight UI message model (author kind, agent key,
  text, timestamp). Will be replaced by the realtime DTO.

### `presentation/screens/`
- `salon_screen.dart` — the full-screen Salon: app bar, message thread, composer.

### `presentation/widgets/`
- `message_bubble.dart` — a single message bubble; agent messages align left with
  their persona accent, staff messages align right in the primary colour.
- `salon_composer.dart` — the message input at the bottom of the Salon.
- `persona.dart` — persona metadata (Aveline/Ava/Elle/Lina) mirroring the web
  `persona.ts`.
