# Privacy & Security

Your client book is your business. Aveline's job is to keep it inside your boutique: separate from
every other boutique on the platform, reachable only by people you have admitted, and never handed
to anyone acting on your behalf without your credentials.

---

## Your data stays inside your boutique

Every request the dashboard makes names the boutique it is about, and the Aveline server checks
three things before it answers:

1. **Who you are** — resolved from your signed-in identity.
2. **Which boutique the request names** — taken from the request itself.
3. **Whether your membership in that boutique is active and carries the permission** the request
   needs.

The server reads your membership fresh from its own records rather than trusting what your session
claims about which boutique you belong to, so a session that is still valid cannot be used to reach
a boutique you are no longer in.

The practical consequences:

- A client, a piece, an order or a conversation belongs to one boutique and is never returned to
  another.
- A client belonging to a different boutique is **indistinguishable from a removed client**: the
  dashboard says *This client is not in this boutique* rather than confirming that the person exists
  somewhere else.
- There is no cross-boutique search, directory or comparison anywhere in the product.

## Identity and sign-in

Sign-in is handled by **Clerk**, our identity provider. Aveline's API validates your session token
against Clerk on every request and then authorises the request against your membership.

- **Two-factor sign-in** is supported: when your account needs it, the sign-in screen asks for a
  **Two-factor code**.
- **Session length is Clerk's.** The dashboard sets no idle timeout of its own.
- **Signing out** from the account menu ends your session for that browser.

## What your role decides

Your role — Owner, Supervisor, Manager or Staff — determines which sections and buttons the
dashboard shows you. That is a convenience, not the boundary: the same permission is enforced again
on the server for every request. See [Roles & Permissions](/docs/roles-permissions).

## Credentials you connect

When you connect WhatsApp Business, Instagram or a payment gateway, you are supplying your own
provider credentials.

- They are **encrypted with AES-256-GCM** before they are stored.
- They are **scoped to your boutique**. No other boutique can read or overwrite them.
- They are **never returned in plaintext**. The dashboard shows a masked preview and the date the
  connection was last validated, nothing more.
- **Disconnecting** removes the stored credentials, and inbound messages stop until you reconnect.

## API keys

API keys let another system act for your boutique.

- The secret is **shown once**, when the key is created. Aveline stores only a hash, so it can never
  be shown again.
- A key is limited to the scopes you give it, and to the environment you choose.
- **Revoke** stops a key working; **Delete** removes it.

## Removing a client

Removing a client from your client book hides their record, and their orders stay readable. The
removal is not offered as a permanent erasure tool, and there is no self-service export of your
client book. Talk to Aveline if you need either.

## What Aveline does not do

- **No impersonation.** There is no feature — no endpoint, token or screen — that lets anyone sign
  in as one of your staff and act as them.
- **No cross-boutique learning.** One boutique's clients are never used to answer another
  boutique's questions.
- **No silent export.** Nothing copies your client book anywhere you did not ask for.

## Where to read more

| Topic | Page |
|---|---|
| Who can see and do what | [Roles & Permissions](/docs/roles-permissions) |
| The credentials you connect | [Integrations](/docs/integrations) |
| API keys | [Settings](/docs/settings) |
| What the client book stores | [Customers](/docs/customers) |
