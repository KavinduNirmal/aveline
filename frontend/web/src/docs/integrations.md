# Integrations

**Integrations** connect your boutique's own WhatsApp Business, Instagram and payment-gateway
accounts to Aveline. Credentials are encrypted and scoped to your boutique only.

Open it from the sidebar at `/app/b/<your-boutique>/integrations`. The section is available to the
**Owner** only.

---

> **Third-party costs are yours.** Connecting an integration may incur fees charged by the provider
> — Meta's WhatsApp messaging rates or your payment gateway's transaction fees, for example.
> **Aveline is not responsible for any third-party API or messaging costs.** Review each provider's
> pricing before connecting.

## What each integration does today

| Integration | State |
|---|---|
| **WhatsApp Business** | Inbound customer messages are received and shown in the Salon and in the activity list. Text a staff member **sends or forwards** from the Salon goes out to the client over WhatsApp. |
| **Instagram** | Credentials are stored and validated. **Instagram messaging is wired in a later release.** |
| **Payment Gateway** | Credentials are stored and validated. **Live payment processing is wired in a later release.** |

Aveline calls the provider with your credentials; your clients never call Meta or your gateway
directly.

## Connect an account

1. Choose the card and click **Connect**.
2. Enter the credentials for that provider:

   | Integration | Fields |
   |---|---|
   | **WhatsApp Business** | **Access Token**, **Phone Number ID**, **App Secret**, **Webhook Verify Token** |
   | **Instagram** | **App ID / Client ID**, **Client Secret**, **Access Token** |
   | **Payment Gateway** | **Secret Key**, **Publishable Key (optional)** |

3. Click **Test & Connect**. Aveline stores the credentials encrypted and validates them against
   the provider.

Every credential field is masked as you type, and nothing is stored in plaintext.

### Where the credentials come from

For WhatsApp, all four values come from the Meta App Dashboard for your WhatsApp Business account.
The **Webhook Verify Token** is a value **you** choose; you will enter the same value in Meta in
the next step.

## Point Meta at Aveline's webhook

This is a one-time step, and it is what makes inbound customer messages arrive in the Salon.

1. In the Meta App Dashboard, open **WhatsApp → Configuration → Webhook**.
2. Set the callback URL to:

   ```
   https://<your-aveline-api-host>/api/v1/webhooks/whatsapp/<your-boutique-id>
   ```

3. Set the verify token to the same value you entered as **Webhook Verify Token** in Aveline.
4. Verify and save, then subscribe to the **messages** field.

Aveline verifies every inbound request with Meta's `X-Hub-Signature-256` signature, so only
genuine Meta traffic is accepted.

> The callback URL is not shown on this page, and your boutique id is not displayed in the
> dashboard. Ask Aveline for your exact callback URL rather than guessing it, so the webhook is
> bound to the right boutique.

## Manage a connection

Each card shows a status:

| Status | Meaning |
|---|---|
| **Connected** | The credentials were validated and the connection is working. |
| **Pending** | The credentials are stored and validation has not finished. |
| **Error** | Validation failed. The card states the reason the provider gave. |
| **Expired** | The provider no longer accepts the credentials, so they need re-entering. |
| **Not connected** | No credentials are stored for this integration. |

A connected card also shows **Last connected** and a masked preview of the token, so you can tell
at a glance which credential Aveline is holding.

- **Test** re-validates a connected integration against its provider.
- **Disconnect** removes the stored credentials. The dialog states it plainly: *This removes the
  stored credentials. Inbound messages will stop flowing until you reconnect.*

## Activity

**Recent activity** lists the latest inbound messages received from customers, with the sender's
phone number masked. When nothing has arrived it reads **No inbound messages yet.** — *Once a
customer messages you on WhatsApp, activity appears here.*

Two counters sit above the cards: **Connected** (how many of the three are connected) and **Needs
attention** (how many need reconnecting).

## Security

- Credentials are encrypted with AES-256-GCM and scoped to your boutique. No other boutique can
  read or overwrite them.
- Provider secrets never leave the backend: the dashboard only ever shows a masked preview.
- Inbound webhooks are signature-verified and rate-limited.

See [Privacy & Security](/docs/privacy-security) for the wider picture.

## When something is not on screen

| What you see | What it means |
|---|---|
| **Not connected yet. Connect to enable messaging.** | No credentials are stored. |
| **This connection needs attention.** | The provider returned a failure and no reason was recorded. Reconnect the account. |
| Empty counters and **No inbound messages yet.** | The integrations read failed, so nothing is shown. Reload the section. |

## Limits

- Only these three integrations exist. There is no generic webhook or custom-API connection.
- Instagram messaging and live payment processing are not implemented yet, even when the
  credentials validate.
- Aveline sends as well as receives. **Send** and **Forward** in the [Salon](/docs/salon) deliver a
  card's words to the client over the connected WhatsApp channel, and the thread then records what
  went out.
