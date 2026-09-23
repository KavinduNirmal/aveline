# Settings

**Settings** is your boutique profile — the details clients see — together with the limits your
plan grants and the machine credentials this shop can use.

Open it from the sidebar at `/app/b/<your-boutique>/settings`, or from the account menu at the
bottom of the sidebar. The section is available to the **Owner** only.

---

## Boutique profile

Edit any of these fields and click **Save changes**:

| Field | Example |
|---|---|
| **Boutique name** | Your boutique's trading name. |
| **Contact email** | `hello@boutique.lk` |
| **Billing email** | `accounts@boutique.lk` |
| **Phone** | `+94 77 000 0000` |
| **Address** | Your physical address. |
| **Currency** | `LKR` |
| **Time zone** | `Asia/Colombo` |

Two behaviours are worth knowing:

- **Only the fields you change are sent.** If a colleague edits the profile while your form is
  open, saving will not overwrite their change with your older values.
- **Saving is disabled until something changes**, and the card says **No changes yet.** until you
  edit a field. The confirmation reads **Settings saved.**

If the server refuses a change — a name collision, for example — its own message is shown under
the form.

## Plan entitlements

The **Plan entitlements** card lists the effective limits for this boutique, with the source each
one resolved from and the date it took effect. It is the authoritative version of what your plan
grants. [Plan & Billing](/docs/billing) explains the same table alongside your billing periods and
Blossom statement.

## API keys

API keys let another system talk to Aveline on your boutique's behalf — a point of sale, for
example. The panel shows every key with its **Name**, **Prefix**, **Environment**, **Status** and
**Requests** count.

### Create a key

1. Click **New key**.
2. Give it a **Name**, such as `Point of sale`.
3. Enter its **Scopes**, separated by commas — for example `catalog:read, customers:read`. Scopes
   decide what the key may call.
4. Choose the **Environment**, `test` or `live`.
5. Click **Create key**.

> **The secret is shown once, at creation.** Aveline stores only a hash, so it can never be shown
> again. Copy it when the panel offers **Copy secret** and keep it somewhere safe; if it is lost,
> revoke the key and create a new one.

### Revoke or delete a key

- **Revoke** stops an active key from working. The confirmation reads **Key revoked.**
- **Delete** removes the key from the list. The confirmation reads **Key deleted.**

Neither asks for confirmation, so check the row before you click. Revoking is the safer of the two
when you only want to stop the key being used.

---

## What is not in Settings

**Integrations is deliberately not part of Settings.** The WhatsApp, Instagram and payment-gateway
credentials live in their own section, behind the same Owner-only permission. Keeping them
separate is what allows a Manager to run the shop — including staff and the catalog — without ever
reaching your channel credentials. See [Integrations](/docs/integrations).

## When something is not on screen

| What you see | What it means |
|---|---|
| **Could not load the boutique settings.** | The read failed. Press **Try again**. |
| **No changes yet.** | Nothing has been edited, so there is nothing to save. |
| **No entitlements are recorded for this plan, so no limits are shown rather than assumed.** | No limits came back for the plan. |
| **Could not load API keys.** | The read failed. Press **Try again**. |
| **No API keys have been created for this boutique.** | No key exists yet. |
| **read-only** | Your role can see the keys but not create or revoke them. |
