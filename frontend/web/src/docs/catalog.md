# Catalog

**Catalog** is everything you sell and everything you are having made: the pieces on the floor,
the looks you have styled from them, the bespoke commissions in progress, and the ateliers you
work with.

Open it from the sidebar at `/app/b/<your-boutique>/catalog`.

---

## What the section shows

Four figures sit across the top. Each one reads **not measured** when the server did not return
the list behind it — an empty catalog reads `0`, because nobody stocked it is a real answer.

| Figure | What it counts |
|---|---|
| **Pieces** | Pieces in the catalog, with how many units you hold in total. |
| **Stock value** | Your recorded retail price multiplied by the quantity on hand. |
| **Low stock** | Pieces with two units or fewer left. |
| **Sourcing** | Sourcing tickets that are neither fulfilled nor archived. |

Below the figures, one switcher holds the four views. Each carries its own count, and a count
shows a dash when that list was not returned.

| View | What it holds |
|---|---|
| **Pieces** | Every piece, with its stock state and the attributes Aveline read from its photograph. |
| **Lookbooks** | Styled ensembles you have composed and saved. |
| **Sourcing** | Bespoke commissions tracked across five stages. |
| **Ateliers** | The partner suppliers on file, with their lead times and minimum orders. |

---

## Pieces

### Find a piece

- **Search** matches the piece name, SKU, colour and fabric.
- The **availability** list narrows to All Availability, In Stock Only, Low Stock (≤2) or Reserved.
- The category pills narrow to All, Sarees, Lehengas, Gowns, Kurtas & Tunics, Outerwear or
  Drapes & Shawls.

Each card shows the garment photograph, the stock state as a word, the name, the SKU and
category, the price, the attributes Aveline read, and the available sizes. Two buttons lead on
from the card: **VIP matches** and **Style look**. Editing, the floor tag and removal sit behind
the card's menu so they do not compete with them.

Stock states are:

| Shown | Meaning |
|---|---|
| **In stock · n** | `n` units on hand. |
| **Low stock · n** | Two units or fewer. |
| **Reserved** | No units on hand, so the piece stops being offered. |
| **Archived** | The piece is out of the active list. |

Click a piece to open its own page at
`/app/b/<your-boutique>/catalog/<piece-id>`. That page is a normal address: you can bookmark it,
share it with a colleague, and it survives a refresh. It repeats the retail price, the atelier
cost, the margin and the record of what was added.

### Add a piece

Requires the Manager, Supervisor or Owner role.

1. Click **Add piece**. A drawer opens from the right with six numbered steps.
2. **The photograph** — upload a file (JPEG, PNG, WebP or HEIC) or paste an image URL. Aveline
   reads the garment and fills the attributes in; **Re-analyze** runs it again.
3. **The piece** — **Item name** (required), **SKU code**, **Category**, **Retail price (LKR)**
   and **Atelier cost (LKR)**. The cost is your own price, not the client's.
4. **What Aveline read** — cloth, dominant colour, fabric and style or pattern, with the
   confidence of the reading. Every value stays editable; nothing here is locked.
5. **Stock and sizes** — **Initial stock quantity** and the available sizes, separated by commas.
6. **Description** — what the piece is and how to style it. **Generate with AI** drafts it from
   the photograph.
7. **Floor tag** — build and download the tag's QR code.
8. Click **Add to Catalog**.

An empty item name is refused with *Item name is required*. A quantity of `0` saves the piece as
**Reserved**, and a quantity of one or two saves it as **Low stock**, so the state is always the
count you typed.

### Edit, sell and adjust a piece

- **Edit piece** (Manager, Supervisor or Owner) reopens the same drawer, and **Delete Piece**
  removes the piece from the active catalog after a confirmation. There is no confirmation on
  Edit.
- **Record a sale** (any role with Catalog access) takes **Pieces sold**, **Unit price (LKR)** and
  an optional **Note**. It reduces the stock and writes the sale into your takings journal, so the
  money appears under **Income** as well.
- **Reduce stock** and **Mark out of stock** (Manager, Supervisor or Owner) correct the count for
  damage, loss or a returned piece. They edit the catalog count only and record no sale.

### Print a floor tag

From a piece card's menu choose **Floor tag QR**, or open the piece and click **Floor tag**. The tag
carries a QR code in one of three encodings:

- Everything a scanner needs, including your boutique.
- A link that opens the piece's page in your boutique.
- The SKU alone, for a point-of-sale system you already run.

Download it as **PNG (600px)** or **Vector SVG**, or print the ready-formatted tag. If your browser
blocks the print window, allow pop-ups for Aveline and try again.

---

## Lookbooks

A lookbook is a styled ensemble: a hero piece, the pieces that go with it, and Elle's notes on
the styling.

1. Click **Compose Look with Elle** (or **Compose First Look** when the list is empty).
2. Choose the **Primary Hero Piece** and the **Target Occasion**.
3. Click **Generate Look**. Elle assembles the complementary pieces and writes the notes.
4. Click **Save to Lookbook**.

Filter the saved looks by occasion. **Edit** lets you rename a look, change its occasion and
rewrite the notes — the item composition is not editable after the look is saved. **Delete**
removes the look and its composition; the catalog pieces it named are not deleted.

## Sourcing

Sourcing tracks bespoke client commissions through five stages: **Pending Quote**, **Quoted by
Atelier**, **Approved**, **Ordered from Atelier** and **Fulfilled**. Each ticket shows the target
retail price, the atelier cost and the resulting margin.

1. Click **New Sourcing Ticket**.
2. Fill in the **Client Name**, **Category**, **Target Color**, **Bespoke Requirements /
   Description**, the target retail price, the estimated atelier cost, and the partner atelier to
   assign.
3. Click **Create Ticket**.

Move a ticket between stages with the stage list on its card. Archiving a ticket takes it off the
board and into **Archived**; the confirmation offers an **Undo**, and restoring an archived ticket
always puts it back at **Pending Quote**.

## Ateliers

**Ateliers** is a directory of your partner suppliers: craft specialty, contact details, lead time
in days and minimum order. **View Atelier Catalog** opens their sample list. The directory is a
reference — suppliers and their samples are not added or edited from here.

---

## Who can do what

| Action | Role needed |
|---|---|
| Open the Catalog and browse pieces, lookbooks, sourcing and ateliers | Any role with Catalog access (Staff, Manager, Supervisor, Owner) |
| Record a sale, compose a look, run VIP matches, print a floor tag, work the sourcing board | Any role with Catalog access |
| Add, edit or remove a piece; reduce or zero out stock; edit or delete a lookbook | Manager, Supervisor or Owner |

If your role does not include the write half, the save is refused by the server and the toast says
so — the list keeps showing the server's state rather than a change that did not happen.

## When something is not on screen

| What you see | What it means |
|---|---|
| **No pieces found** | Nothing matches the current search, category or availability. **Clear Filters** resets the view. |
| **This piece is not in the catalog** | The piece was removed, or the link names a piece this boutique does not hold. |
| **No Lookbooks Found** | Nothing styled yet. **Compose First Look** starts one. |
| **No tickets in this stage** | That sourcing stage is empty. |
| **No Partner Ateliers Found** | No suppliers are on file for this boutique. |
| **No VIP matches yet** | Nothing in this piece's colour and fabric lines up with a client's taste profile yet. |
| **not measured** | The server did not return that figure. It is not a zero. |

## Limits

- There is no camera or barcode scanner in the Catalog. Photographs come from a file or a URL, and
  scanning is what the printed floor tag enables for a scanner you already use.
- A card carries no confidence figure for the visual reading. The list does not measure one, and a
  number nobody measured is worse than no number.
- There is no export, no bulk import and no paging control on the pieces list.
- Archived pieces cannot be reached or restored from the dashboard.
- Composing a look can return a hero piece plus the next best match when Elle finds no stronger
  combination; review the look before saving it.
