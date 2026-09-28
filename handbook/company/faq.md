# Frequently Asked Questions

Short answers to the questions that cross more than one section of the product. Each answer names
where the detail lives in the product documentation under `/docs`.

## How do I invite a staff member?

Open **Team** (available to a Supervisor, Manager or Owner), mint an invitation code and share it.
A code is twelve characters and is shown once — copy it or send the link then.

You can mint up to ten codes at a time and choose how long each lasts: 24 hours, 7 days or 30 days.
The server accepts a lifetime between one hour and thirty days. Outstanding codes are listed under
pending codes and can be revoked, but **a code that has already been accepted cannot be revoked** —
remove the member instead. The person joining gets the role you choose, and a role change takes
effect on their next request.

## What can each role do?

Every person holds one role. In one line each:

- **Owner** — full authority. The only role that reaches Integrations and Settings, and the only one
  that can top up Blossoms.
- **Manager** — runs the shop day to day: clients, catalog, income, Team, and Billing.
- **Supervisor** — oversees the counter: clients, catalog, the income register, and the full set of
  approval decisions.
- **Staff** — the counter: clients, visits, orders, and the Salon. No income register, no Team, no
  Settings.

The full matrix is in the documentation under `/docs/roles-permissions`. The dashboard hides what a
role cannot use, but hiding is not the rule: every request is authorised again on the server.

## Can Aveline send messages to my clients?

Aveline **receives** inbound WhatsApp messages and shows them in the Salon. A card that a member of
staff **sends or forwards** from the Salon goes out to the client over the channel the boutique has
connected. Sending needs a connected channel and a phone number for the client.

Instagram and the payment gateway currently store and validate credentials only; live messaging and
live payment processing are not wired yet. Third-party messaging and gateway fees are the
boutique's, not Aveline's.

## How do I see what my plan actually includes?

Read the **Plan entitlements** card in Billing or Settings, not the plan name. It is the
authoritative list of the limits in force, with the source each one resolved from and the date it
took effect.

## What does "not measured" mean?

It means the server did not report the figure, and Aveline shows that in words rather than drawing a
zero. **`0` is a measurement; a blank is not.** A missing balance is unknown, not spent.

## What happens when a discount goes past my limit?

The order appears in **Approvals** with an over-threshold badge. Three verbs decide it:

- **Approve** — the order proceeds and the quoted discount stands. Staff, Supervisor and Owner.
- **Reject** — the order is cancelled, and cannot be undone from the dashboard. Supervisor and
  Owner.
- **Revise** — the order's discount and total are rewritten. Supervisor and Owner.

A Manager sees neither the section nor a verb: rejecting and revising are shown only to a role that
holds order management **and** the approval permission that opens Approvals.

The limit itself is the discount ceiling configured for your boutique, not a number Aveline decides.

## Where do I change my brand voice or business rules?

They are configured during setup. They are not part of Settings, and this handbook does not describe
changing them afterwards — contact the Aveline team and they will help.

## I cannot sign in, or my code never arrived

Sign-in is handled by Clerk, and two-factor sign-in is supported. If a verification code does not
arrive or you lose your second factor, contact the Aveline team rather than trying to work around
it.

## How long is my data kept, and can I export it?

Neither is described in the handbook yet. There is no self-service export or erasure; ask the
Aveline team. The limits of what is documented are listed under "What the Handbook Does Not Cover".
