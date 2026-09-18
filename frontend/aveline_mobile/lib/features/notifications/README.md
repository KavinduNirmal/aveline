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
| **Swipe left** | Deletes it, and offers an undo | The tile leaves at once, but the API is not told for five seconds. That is what makes Undo a real undo rather than a local lie. |

An unfolded tile shows the whole body, the payload the dispatcher attached
(order id, client, amount) as chips, the exact time, and its own actions:
**Open** (only when the notification addresses a client the app can open),
**Mark as read**, and **Delete**.

Read tiles step back rather than disappear: no shadow, a recessed fill, a
lighter title and no dot. That is what keeps a long inbox scannable.

The notifications are a demo inbox (`demo_notification_repository.dart`)
standing in for the notification endpoints, which are specified in
`docs/api/openapi.yaml` but not yet mapped on the server. Swapping
`DemoNotificationRepository()` for `ApiNotificationRepository(_dio)` in
`app.dart` is the whole change.

## Layers

### `presentation/screens/`
- `notifications_screen.dart` — the Notifications tab, reachable from the header's
  bell and from Home's quick actions, both routing to `AppRoutes.notifications`.
  It owns no inbox state: it reads the app-wide `NotificationsController` so the
  header's badge and this list are the same number, and a notification read here
  clears the mark that sent the associate here. The controller is injectable for
  tests and previews, and when no provider is above the screen the screen falls
  back to a demo inbox rather than throwing.

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
- `notification_kind_visuals.dart` — the icon and tint each kind wears. The six
  accents are brand-adjacent rather than brand-exact: the design system names
  three agent states and the inbox has six kinds to keep apart, so four hues are
  added. All stay warm and low-saturation.
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
- `demo_notification_repository.dart` — nine notifications, one of every kind,
  a mix of read and unread, and two that address the same client so a thread of
  updates reads as one. Ages are measured from an injectable clock, so "20 minutes
  ago" means the same thing in a test as it does on screen.

### `domain/`
- `app_notification.dart` — one inbox row, mirroring `UserNotificationDto`. The
  `type` is kept as the raw string the API sent and the `kind` is derived from it,
  so the two cannot drift and an unknown type still round-trips.
- `notification_kind.dart` — the six kinds the dispatcher sends, plus `unknown`.
  A newer backend can add a type without breaking an older app: an inbox that
  refuses to open is worse than one tile wearing a generic label.
- `notification_page.dart` — one page and the envelope's own word on whether the
  inbox continues. Measured from how far into the inbox the page reaches, because
  `items.length < total` would call a page past the end "more to come".

## Where the count lives

`core/notifications/notification_provider.dart` holds the badge's number, not the
inbox. The controller reports its count up to it through `setUnreadCount`, and
`shared/widgets/notification_badge.dart` wears that count, falling back to a plain
dot on the last notification that arrived until the inbox has been counted. A
badge keyed off the last payload alone stays lit for good, however much has been
read since; the count is what clears it.

## Related

- Plan: `.agents/plans/notification_implementation.ignore.md` (issues #89–#93).
- Contract: `docs/api/openapi.yaml`, the `Notifications` tag.
- Gateway: `Aveline.Api/Modules/Notifications/`, including the `UserNotification`
  inbox rows this feature renders.
