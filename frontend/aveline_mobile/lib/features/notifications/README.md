# Feature: Notifications

> **Domain:** Notification Gateway — the inbox the gateway's dispatches land in

## Status

The tab reads as the boutique's own inbox: the shop's name in the title, the
unread count and a **Mark all read** action under it, an All / Unread narrowing,
and then the notifications themselves, newest first. Each tile carries three
gestures:

| Gesture | What it does | Why |
|---|---|---|
| **Tap the header** | Unfolds the notification in place | The inbox is a feed, not a list of links. Opening one closes the one before it, because an accordion that stacks panels turns a long inbox into a wall. |
| **Swipe right** | Marks it read, then springs back | A read notification is still a notification. Removing it would hide the very thing the associate just dealt with. |
| **Swipe left** | Deletes it, and offers an undo | The tile leaves at once, but the API is not told for five seconds. That is what makes Undo a real undo rather than a local lie. The offer expires with the toast, and there is no restore route: once the window lapses the row is gone. |

An unfolded tile shows the whole body, the payload the dispatcher attached
(order id, client, amount) as chips, the exact time, and its own actions:
**Open** (only when the notification addresses a client the app can open),
**Mark as read**, and **Delete**.

Read tiles step back rather than disappear: no shadow, a recessed fill, a
lighter title and no dot. That is what keeps a long inbox scannable.

The inbox is live: `app.dart` selects `ApiNotificationRepository(_dio)`, which
reads `GET /api/v1/notifications`, `/unread-count` and the read/dismiss routes
mapped in `Aveline.Api/Endpoints/NotificationEndpoints.cs`. `DemoNotificationRepository`
stays in the tree for tests and previews only, and the screen's provider-less
fallback is `EmptyNotificationRepository`, so a widget test or preview that mounts
the screen alone draws the real empty state rather than an invented inbox.

## Layers

### `presentation/screens/`
- `notifications_screen.dart` — the Notifications tab, reachable from the header's
  bell and from Home's quick actions, both routing to `AppRoutes.notifications`.
  It owns no inbox state: it reads the app-wide `NotificationsController` so the
  header's badge and this list are the same number, and a notification read here
  clears the mark that sent the associate here. The controller is injectable for
  tests and previews, and when no provider is above the screen the screen falls
  back to an empty inbox rather than throwing.

### `presentation/`
- `notifications_controller.dart` — owns the loaded inbox, the unread count, the
  paging, the stale-reply guard and every mutation. All mutations are optimistic
  and rolled back to the exact index they came from if the API refuses, because a
  swipe that appears to work and quietly did not is worse than one that visibly
  fails. Deletion is the exception that proves the rule: it is held for
  `dismissCommitDelay` so the Undo offer and the window it belongs to cannot
  drift apart.

### `presentation/widgets/`
- `notification_tile.dart` — one notification: the enclosure, the title, the
  kind and age, the preview, and the unfolded body. Both swipe gestures and the
  accordion live here, so the three cannot disagree about what a gesture means.
- `notification_kind_visuals.dart` — the icon and tint each kind wears. The eight
  accents are brand-adjacent rather than brand-exact: the design system names
  three agent states and the inbox has eight kinds to keep apart, so further hues
  are added. All stay warm and low-saturation.
- `notification_swipe_background.dart` — what a swipe uncovers, tinted rather
  than filled, because the gesture is a choice still being made.

### `data/`
- `notification_repository.dart` — the inbox contract: paged read with an
  `unreadOnly` narrowing, its own unread count, an idempotent mark-read, a
  mark-all-read and a soft-dismiss. Every call is scoped to the caller on the
  server side, so no user id appears in it.
- `api_notification_repository.dart` — the contract over `Dio`, following
  `docs/api/openapi.yaml` exactly. The shared auth interceptor attaches the Clerk
  token, so nothing here handles credentials.
- `demo_notification_repository.dart` — eleven notifications, one of every kind,
  a mix of read and unread, and two that address the same client so a thread of
  updates reads as one. Ages are measured from an injectable clock, so "20 minutes
  ago" means the same thing in a test as it does on screen.

### `domain/`
- `app_notification.dart` — one inbox row, mirroring `UserNotificationDto`. The
  `type` is kept as the raw string the API sent and the `kind` is derived from it,
  so the two cannot drift and an unknown type still round-trips.
- `notification_kind.dart` — the eight kinds the dispatcher sends, plus `unknown`.
  A newer backend can add a type without breaking an older app: an inbox that
  refuses to open is worse than one tile wearing a generic label.
- `notification_page.dart` — one page and the envelope's own word on whether the
  inbox continues. Measured from how far into the inbox the page reaches, because
  `items.length < total` would call a page past the end "more to come".

## Realtime

An arrival reaches an open app over the SignalR hub at `/hubs/notifications`
(`ReceiveNotification`). `RealtimeNotificationService` connects on sign-in and
registers its reconnect hooks **before** starting the socket. SignalR does not
preserve group membership across a reconnect, so on every reconnection the
service invokes the hub's idempotent `SubscribeAsync` to re-join `user:{id}` and
the active `org:{id}` groups, and then re-reads the inbox: anything that arrived
while the socket was down was never delivered and is only recoverable from the
API. A failed re-join is reported rather than swallowed, because a connection
that never re-joined looks exactly like a quiet inbox.

## Push

Push reaches a closed app through FCM. The `data` map carries the notification's
own keys plus two the app needs: `type` (the `NotificationType` name, so the app
renders the right kind) and `notificationId` (the inbox row, so a tap can mark
**that notification** read). Values are strings and the sender drops nulls, so
every key is optional to the consumer.

`PushMessageHandler` (in `core/notifications/`) owns the two behaviours:
a foreground message (`onMessage`) is fed to the same "an arrival happened" seam
the realtime path uses, so the badge and the list behave identically whichever
transport delivered it; and a tap (`onMessageOpenedApp`, and `getInitialMessage`
for a cold start) opens the app, issues `PATCH /api/v1/notifications/{id}/read`,
and routes through the one shared rule. The tap marks the **notification**, never
the conversation or the message: the thread owns its own read state and writes it
when the reader reaches the newest message.

A cold-start tap can arrive before the session is restored, so the mark-read is
best-effort and the route is still opened; the navigation is deferred to the next
frame because the router may not be mounted yet. Firebase is initialized
best-effort in `main()`; when it is unavailable the handler is simply not started.

## Where the count lives

`core/notifications/notification_provider.dart` holds the badge's number, not the
inbox. The controller reports its count up to it through `setUnreadCount`, and
`shared/widgets/notification_badge.dart` wears that count, falling back to a plain
dot on the last notification that arrived until the inbox has been counted. A
badge keyed off the last payload alone stays lit for good, however much has been
read since; the count is what clears it.

A live arrival carries the recipient's own count (`unreadCount`, computed by the
server after the row was written), which the controller applies immediately
through `applyUnreadCount`, so the badge moves before the list's reply lands. The
inbox is still re-read, because the rows remain the API's authority. A payload
without the field — an older server — falls back to the refresh alone, exactly as
before. The server's count is used rather than a local `+1` guess: it is cheaper
and truthful.

## Deferred

The inbox is complete for everything that exists today; the following is deliberately not
built, each waiting on the thing that unblocks it.

- **Conversation-grouped presentation (WhatsApp-style).** With one row per conversation the
  flat feed already reads correctly; grouping is a design decision to take with the
  `NewMessage` producer, not speculatively.
- **Rendering the per-row boutique label.** `UserNotificationDto` carries
  `organizationId`/`organizationName`, but the tile does not draw it. Every staff account is
  single-boutique today (Q1); the label earns its place only once an in-app org switcher
  exists for the deferred owner-with-several-boutiques account.
- **An org filter on the inbox.** The list is merged per user on purpose; filtering would hide
  work rather than label it.
- **A local-notification stack / app-styled foreground banners.** The OS draws the
  notification when the app is not foregrounded. `PushMessageHandler.onMessage` refreshes the
  inbox; it does not draw a banner of its own.
- **Undo after the toast.** The dismissal window is client-side (`dismissCommitDelay`, 5 s)
  and there is no restore route: once the toast expires the row is gone, by design.
- **Every notification producer.** `NewMessage`, `NewMatch`, `VipAtRisk`, `ApprovalNeeded` and
  `PaymentConfirmed` are dispatched by the modules that own their events. The inbox renders
  and deep-links each with no client change once they do; the server-side gates are recorded
  in `docs/backend/README.md` §"Deferred and not built by this work".
- **A ninth `NotificationType`.** It still renders as `unknown`, which is the documented
  tolerance rather than a gap: a newer backend must not break an older app.

## Related

- Plan: `.agents/plans/flutter-to-backend-notifications-implementation.ignore.md`
  (S0–S9, issues [#304](https://github.com/KavinduNirmal/aveline/issues/304)–[#313](https://github.com/KavinduNirmal/aveline/issues/313));
  design record: `…-implementation-strategy.md`.
- Contract: `docs/api/openapi.yaml`, the `Notifications` tag; `docs/api/README.md` §B.6.
- Gateway: `Aveline.Api/Modules/Notifications/`, including the `UserNotification`
  inbox rows this feature renders and the dispatch path behind them
  (`docs/ADR/ADR-013-notification-service-architecture.md`).
