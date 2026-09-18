# Feature: Settings

> **Domain:** The associate's own account and preferences, plus the shop's block

## Status

The settings page is one column, read top to bottom, in the order an associate
meets it: **themselves, the shop they belong to, and the way out.**

**Account comes first, and it is the retired Profile tab.** The two were merged
rather than placed side by side, because an account is one of the things an
associate sets: a dock tab per setting would have made the side panel a list of
nouns instead of a list of places. The card carries the same identity the old
screen did - the face, the name, the email, the two roles - and one action,
**Edit details**, which opens the two fields the API lets a user change about
themselves.

Under it, **Notifications & contact**: the push switch and where the boutique
should reach the associate first. Then **Boutique**, for the roles that may manage
the shop. Last, **Session**, holding the one destructive action on the page.

## One destination, two audiences

The side panel row that opens this page is deliberately **not** permission-gated,
while the Boutique block inside it is gated on `settings:manage`.

That split is the point of the merge. A staff member without a single shop-wide
grant still owns their account and their preferences, and before Settings existed
the only way to sign out or read the roles on their profile was a tab that had no
other reason to exist. The shop's own settings are a different question, and they
answer to a different grant: `Permissions.anyGranted([userRole, organizationRole],
Permissions.settingsManage)`, which today means a boutique owner or an Aveline
admin.

## The controls read the record, not themselves

Every control on this page is a view of the record `UserProvider` holds, and every
change goes through `UserProvider.updateProfile`, which sends `PATCH
/api/v1/users/me` and **replaces the held record with the answer the server
gives**.

That is what makes a refused save put itself back. There is no local switch state
to undo and no optimistic value to reconcile: the switch was never anything but
the record, so a save that failed leaves the record where it was and the switch
follows. All the screen keeps is which change is in flight, and only to stop a
second one racing the first.

A refusal is reported through `updateErrorMessage`, kept apart from
`errorMessage`. The router sends the associate to the retry screen on a profile
that could not be **read**; a rejected toggle has to say why without moving them
off the page they are on.

Three smaller decisions are worth naming:

- **The picker's dismissal is not a choice.** `showContactPreferenceSheet` returns
  `null` when the sheet is dismissed and a `ContactPreference` when one is chosen,
  so closing the sheet leaves the stored preference alone instead of being read as
  `None`.
- **The sheet's actions do not scroll away.** The options and the detail fields
  scroll; **Cancel** and **Save** sit outside the scroll view. A sheet that hides
  the way out is a sheet that traps.
- **A save with nothing to change is not sent.** Editing the details and changing
  nothing closes the sheet without a request, and `updateProfile` refuses to send
  an empty body.

## What each group says, and why

| Group | Rows | Notes |
|---|---|---|
| Account | identity card, **Edit details** | Email and username come from sign-in and cannot be changed here, and the note says so rather than leaving the associate to find out |
| Notifications & contact | push switch, preferred contact | The switch carries what push is for; the group's note says that turning it off leaves things in the inbox rather than losing them |
| Boutique | boutique, store role | Only for `settings:manage`. Its note names what is not built yet instead of pretending the block is complete |
| Session | sign out | Not buried inside the identity card: a destructive action reads better where it is not mistaken for part of the profile |

## What the API carries today

`PATCH /api/v1/users/me` is implemented (`Aveline.Api/Endpoints/UserEndpoints.cs`)
and takes `UpdateUserProfileRequest`: `firstName`, `lastName`, `displayName`,
`phoneNumber`, `profileImageUrl`, `contactPreference`, `pushNotificationsEnabled`.
Only the fields that are present are changed, which is why the provider sends the
narrowest body it can.

`ContactPreferences` on the wire is exactly five strings - `Email`, `Phone`,
`SMS`, `WhatsApp`, `None` - and `ContactPreference` speaks them, so the picker
shows a label and answers in the API's own spelling. It lives beside `AvelineUser`
in `features/auth/domain/` rather than in this feature, because parsing a field on
the user entity is the domain's job rather than a widget's.

## Layers

### `presentation/screens/`
- `settings_screen.dart` - the Settings dock tab, at `AppRoutes.settings`. It owns
  no account state: the controls read `UserProvider` and write through it. It reads
  the boutique name and the account defensively, so a bare test mount degrades
  rather than throwing.

### `presentation/widgets/`
- `settings_section.dart` - one named group as one card: an overline, the rows, and
  an optional closing note. The hairline between rows belongs to the group, so no
  row has to know what it sits next to.
- `settings_row.dart` - one line: what it is about, what it is set to, and where it
  leads. A chevron appears only when a tap does something, because a chevron on an
  inert row is the row lying about itself.
- `settings_switch_row.dart` - one switchable line. The switch reads its value from
  above and reports the flip; `onChanged: null` is how a caller refuses the control
  outright, which is not what this screen does - it keeps the row's shape steady and
  refuses a second change inside `_save`, so nothing shifts under the thumb.
- `account_card.dart` - the merged profile: face, name, email, the number on file
  when there is one, the role chips, and the edit action.
- `edit_profile_sheet.dart` - the two fields the associate owns, returning a
  `ProfileDetails` record or `null` when dismissed.
- `contact_preference_sheet.dart` - the picker, returning the chosen preference or
  `null` when dismissed.

### `domain/` (shared with the auth feature)
- `features/auth/domain/contact_preference.dart` - the enum the wire strings parse
  into: `wireValue`, `label`, `description`, and a tolerant `fromWire`.
- `AvelineUser.preferredContact` - the stored string as that enum.
- `AvelineUser.nameForDisplay` - the chosen display name, or the name parts
  joined, with the caller's fallback for an account that has neither. The side
  panel's user card and the account card both read it, so a name cannot be one
  thing in the panel and another on the page it opens; only the words for an
  account with no name at all are the caller's.

### `core/providers/`
- `UserProvider.updateProfile` - the write side of the profile: narrowest body,
  server's answer replaces the held record, refusal reported rather than thrown.

## Shared with this slice

- `shared/widgets/brand_section_title.dart` and
  `shared/widgets/section_overline.dart` - the same heading pair every dock tab
  uses.
- `shared/widgets/blossom.dart` - the fallback mark when no picture is set.
- `shared/widgets/app_toast.dart` - how a refusal is said out loud.
- `core/auth/app_roles.dart`'s `labelFor` - the words for a role. The claims carry
  `org:boutique_owner`, and a wire value is not what an associate should read about
  themselves; the account card's chips, the Boutique row and the side panel's user
  card all name the role through it.

## Related

- The retired Profile tab's content now lives here; `AppRoutes.profile` forwards to
  `AppRoutes.settings` so the header's avatar and any stored link still resolve.
- Contract: `docs/api/openapi.yaml`, the `Users` tag (`getCurrentUser`,
  `updateCurrentUser`, `listSessions`, `revokeAllSessions`).
- Backend: `Aveline.Api/Endpoints/UserEndpoints.cs` and
  `Modules/Shared/Services/UserService.cs`.
- The web shell's own settings entry and user footer:
  `frontend/web/src/components/dashboard/DashboardShell.tsx`.

## Known gaps

- **No "where you're signed in".** `GET /api/v1/users/me/sessions` and
  `POST .../sessions/revoke-all` are implemented and would slot into the Session
  group, but they proxy the Clerk admin API and answer `502` when that call fails,
  so they need their own failure states rather than a row that blanks.
- **The shop's block carries only what the app already holds.** `GET
  /api/v1/orgs/{id}/settings` is the endpoint that would fill it, and it needs
  reconciling first: the code answers `{ settings, entitlements }` while
  `docs/api/openapi.yaml` documents `{ organization, brandVoice, businessRules,
  preferredColorsFabrics, customerPreferences, entitlements }`.
- **The picture is not editable.** `UpdateProfileRequest` takes `profileImageUrl`,
  but there is no upload surface on mobile and the Clerk picture is what the
  header shows; changing it from here would need a decision about which wins.
- **No account deletion.** `DELETE /api/v1/users/me` exists and is destructive
  (soft delete, memberships revoked); it deserves a confirmation flow of its own
  rather than a row that could be mis-tapped.
- First and last name are not editable here, only the display name and the number.
  They are Clerk's fields, and the profile screen never showed them in the first
  place.
- The phone number is length-limited to the API's 20 characters but not otherwise
  validated, so anything that fits is sent and the API decides.
- `shared/widgets/floating_dock.dart` still lists a `profile` tab. It is unmounted
  dead code today; if the dock is ever wired up, that tab belongs to Settings.
