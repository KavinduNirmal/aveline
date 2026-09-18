# Integrations

Connect your boutique's own WhatsApp, Instagram, and payment gateway accounts so Aveline can
act on your behalf. Aveline never stores your credentials in plaintext — they are encrypted
at rest and scoped to your boutique only.

---

## How Integrations Work

Each boutique owner supplies their own third-party credentials. Aveline acts as the
**integration gateway**:

- **Credentials are encrypted** with AES-256-GCM and scoped per-organization. No other
  boutique can read or overwrite yours.
- **All traffic flows through Aveline's backend.** Your clients never call Meta or your
  payment provider directly.
- **Status is tracked.** Each integration shows whether it is `Connected`, `Pending`,
  `Error`, or `Expired`, along with the last time it was validated.

> **Important — third-party costs:** Connecting an integration may incur fees charged by the
> third-party provider (for example Meta's WhatsApp Business messaging rates or your payment
> gateway's transaction fees). **Aveline is not responsible for any third-party API or
> messaging costs.** These are solely your responsibility as the account holder. Review each
> provider's pricing before connecting.

---

## Connecting WhatsApp Business

WhatsApp lets Aveline message customers and receive inbound customer messages on your
WhatsApp Business number.

### 1. Gather your Meta credentials

From the Meta App Dashboard for your WhatsApp Business account, collect:

| Field | Where to find it |
|---|---|
| **Access Token** | Meta App Dashboard → WhatsApp → API Setup (a permanent/system-user token) |
| **Phone Number ID** | Meta App Dashboard → WhatsApp → API Setup |
| **App Secret** | Meta App Dashboard → App Settings → Basic |
| **Webhook Verify Token** | A token **you** choose and set in the Meta webhook configuration |

### 2. Connect in Aveline

1. Go to **Settings → Integrations**.
2. On the **WhatsApp Business** card, click **Connect**.
3. Enter the four credentials above.
4. Click **Test & Connect**. Aveline validates the token against Meta and marks the
   integration `Connected`.

### 3. Point Meta at Aveline's webhook (one-time)

For inbound customer messages to reach Aveline, subscribe Meta to Aveline's webhook:

1. In the Meta App Dashboard, open **WhatsApp → Configuration → Webhook**.
2. Set the **Callback URL** to:
   `https://<your-api-host>/api/v1/webhooks/whatsapp/<your-organization-id>`
3. Set the **Verify Token** to the same value you entered in Aveline.
4. Click **Verify and Save**, then subscribe to the **messages** field.

> Aveline verifies every inbound request with Meta's `X-Hub-Signature-256` signature, so only
> genuine Meta traffic is accepted.

---

## Connecting Instagram

Instagram connects your Meta/Instagram business account for social commerce. Enter your
**App ID / Client ID**, **Client Secret**, and a long-lived **Access Token**. Instagram
messaging is wired in a later release; today Aveline stores and validates the connection.

---

## Connecting a Payment Gateway

The payment gateway lets customers pay securely through your boutique gateway. Enter your
**Secret Key** (and optional **Publishable Key**). Live payment processing is wired in a later
release.

---

## Managing Connections

- **Test** — re-validates a connected integration against its provider.
- **Disconnect** — removes the stored credentials. Inbound messages stop until you reconnect.
- **Expired** — if a WhatsApp token expires, Aveline marks the integration `Expired` and
  notifies the boutique owner so they can reconnect.

---

## Security

- Credentials are encrypted at rest (AES-256-GCM) and never returned over the API — only a
  masked preview is shown.
- Webhooks are signature-verified and rate-limited.
- Every integration is isolated per boutique (tenant isolation).

See [Privacy & Security](/docs/privacy-security) for the full security model.
