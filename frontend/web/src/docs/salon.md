# Salon

The **Salon** is where your boutique talks to Aveline. Each client can have their own Salon, and
one shared Salon belongs to the boutique itself. Aveline answers in the thread: it remembers what
you tell it about a client, sources pieces for a look, and can ask you to approve a commitment
before it goes ahead.

Open it from the sidebar at `/app/b/<your-boutique>/salon`. Every role can open the Salon.

---

## The two panes

The left pane lists the Salons. The right pane is the open thread.

### The list

Each row carries:

- The client's name, when the Salon belongs to a client.
- A thread that came in on a channel and has no client attached is named by its channel reference.
- The boutique's own, client-less thread is named **Aveline** and carries a **Concierge** badge.
- A client Salon with no name recorded reads **Unnamed client**.
- The time of the last message, or **No messages yet**.

The shared concierge thread is always pinned to the top of the list; the client threads keep their
newest-first order.

**New salon** opens the shared concierge thread, creating it if it does not exist yet.

### The thread

Messages from your team sit on the right; messages from an agent sit on the left with the agent's
name above the bubble. Each message carries its time, and an agent that had to work for its answer
also says how long it thought — *Thought for 1.20s*.

Above the thread, the header names the open Salon. For the shared thread it reads **Shared
concierge thread**; for a client thread it shows what Aveline is currently doing — **Thinking…**,
**Searching…**, **Working…**, **Using a tool…**, **Awaiting your decision…**, **Done** or
**Something went wrong**.

A message that has not reached the server yet reads **Sending…**, and one that failed reads
**Failed to send**.

## Talking to Aveline

- Type in the box at the bottom and press **Enter** to send; **Shift+Enter** starts a new line.
- You can refer to a client by name with `@name` and to a phone number with `#phone`. Mentions are
  resolved as you type them — there is no picker to choose from.
- Attach files with the paperclip. Attachments may be images or PDFs, up to 5 files per message and
  5 MB each. Photos are resized before upload; a PDF is sent as it is.
- While files are uploading, sending waits for them. A failed upload can be retried or removed
  without losing your text.

### The agents you will meet

Agent messages are labelled with the agent's name and tinted in that agent's colour:

| Name | Where you will see it |
|---|---|
| **Aveline** | Your boutique concierge — the agent in the shared thread, and the one that takes your requests. |
| **Ava** | Memory: client preferences, history and briefings. |
| **Elle** | Visual sourcing: garments, looks and pairings. |
| **Lina** | Commerce: orders, pricing and payment. |

Aveline's working indicator always carries the name Aveline, including while one of the specialists
is answering.

### Cards in a thread

Aveline does not only write prose. It can post a piece card, a look, an at-a-glance table, a
suggestion, a payment card or a delivery card. Photographs open in the thread, and a PDF attachment
offers **Open** and **Download**.

An inbound customer message — for example one that arrived on WhatsApp — is shown with a **Customer**
label, the WhatsApp badge and the customer's own words. It is recorded in the thread; it is not a
message your team wrote.

## Decisions Aveline asks you for

### Approval needed

When a commitment goes past a limit, Aveline posts an **Approval needed** card with the reason and
the amount, and two buttons: **Approve** and **Reject**. Deciding releases or refuses the
commitment. This needs the approval permission:

| Role | May decide a sign-off in the thread |
|---|---|
| Staff, Supervisor, Owner | Yes |
| Manager | No — the card's buttons are not shown |

Once you decide, the buttons disappear from the card. There is no separate confirmation message.

### Choosing a client

When Aveline is unsure which client a thread belongs to, it posts the candidates as buttons. Choose
one and Aveline picks the thread up with that client in context. In the slide-in Aveline chat these
options are not selectable — make the choice in the Salon.

---

## The Aveline chat drawer

The blossom button in the dashboard header opens **Aveline chat**: a slide-in panel for talking to
Aveline without leaving the section you are working in.

It is deliberately its own thread. Opening a client's Salon in the section does not move the
drawer, and pinning the drawer to Aveline does not move the section — so you can keep a client's
thread open while you ask Aveline something else.

## When something is not on screen

| What you see | What it means |
|---|---|
| **No salons yet. Start a conversation with Aveline.** | No thread exists yet. **New salon** opens the shared one. |
| **The Salon is quiet** | The open thread has no messages. *Ask Aveline anything about a customer, a piece, or a price.* |
| **Message not sent** | That message could not be sent. *Nothing was lost; your text and files are still here.* |
| **Some files were not attached** | The file was over 5 MB, was more than the fifth attachment, or is not an image or a PDF. Each reason is listed. |
| **Uploading…**, then **Ready** | A file is being stored, then is ready to send. A failed chip offers a retry. |
| An empty list or thread after a failure | A Salon read that fails renders the empty state rather than an error card. Use **Refresh** by reopening the section. |

## Limits

- **You cannot reply to a customer on WhatsApp from the Salon.** Inbound customer messages appear
  here; Aveline does not send WhatsApp replies, and there is no outbound send log.
- **Instagram messaging is not connected yet.**
- **Only the most recent messages are loaded.** There is no "load older" control and no search
  across history.
- **Salons are not created by hand.** A client Salon is created with the client; the shared
  concierge thread is created on first use.
- There is no unread badge on a thread row, and the list shows no message preview.
- There is no offline or reconnecting indicator in the Salon.
