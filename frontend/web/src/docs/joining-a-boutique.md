# Joining a Boutique

If you are joining a boutique that already uses Aveline, your boutique owner sends you an
**invitation code** — twelve characters — or a link that carries it. The code decides the role you
join with, and the role decides what you can open. Nobody can change it for you afterwards from
your side; the owner can change your role from the Team section once you are in.

---

## Before you start

Ask your boutique owner for one of these:

- The **invitation code**, for example `AB7K2M9Q4XTZ`.
- The **invitation link** they copied from Aveline, which looks like
  `https://<your-aveline-host>/invite?code=AB7K2M9Q4XTZ`.

The link only works while the code is valid and unused, and it is issued for a fixed lifetime —
24 hours, 7 days or 30 days, whichever your owner chose.

## Join with an invitation link

1. **Sign in or create your account first.** The invitation link only accepts a signed-in account,
   and signing in sends you to the dashboard rather than back to the link.
2. Once you are signed in, **open the invitation link again**. Aveline accepts it automatically and
   shows *Joining your boutique…* for a moment.
3. You land in your boutique's dashboard.

There is no confirmation screen, and no second acceptance step.

> If you open the link before signing in, nothing is lost — you are sent to sign-in, and the link
> simply needs opening again afterwards.

## Join from inside setup

You can join straight from the sign-up flow instead of using a link:

1. Create your account and choose **Staff member** at sign-up, or choose **I'm Staff** on the first
   screen of the setup guide.
2. Enter the code in **Invitation Code**.
3. Click **Join Boutique & Enter Dashboard**.

You go straight to the dashboard.

## When a code does not work

| What you see | What it means |
|---|---|
| **This invitation link is missing its code.** | The link was truncated. Ask your owner for the full link or the code itself. |
| **The invitation was not found.** | The code does not exist. Check for a mistyped character. |
| **The invitation cannot be accepted: expired** | The code's lifetime ran out. Ask for a new one. |
| **The invitation cannot be accepted: revoked** | Your owner cancelled the code. Ask for a new one. |
| **The invitation cannot be accepted: already accepted** | The code has been used. A code admits one person. |
| **This invitation was issued for a different recipient.** | The code was addressed to another email address. Ask your owner to issue one for you. |
| **The user is already a member of this organization.** | You already belong to this boutique, so there is nothing to accept. |
| **Too many invitation attempts. Please wait a minute and try again.** | Too many codes were tried in a short time. Wait a minute. |

If none of that applies, the message in the red panel is the server's own explanation, and
**Go to onboarding** takes you back into setup.

## What happens after you join

- Your account becomes active and your role — Staff, Supervisor or Manager — is set from the code.
- **Staff** sees clients, the catalog, the Salon and the usage balance, and can approve orders.
- **Supervisor** adds the income register and reject/revise decisions.
- **Manager** runs the shop, including Billing, without reaching your channel credentials.
- [Roles & Permissions](/docs/roles-permissions) has the full table.

## Limits

- A code admits one person and cannot be reused.
- Codes are stored hashed, so your owner cannot look one up after generating it. If it is lost, the
  owner revokes it and issues a new one.
- You cannot choose your own role, and you cannot join a boutique without a valid code.
