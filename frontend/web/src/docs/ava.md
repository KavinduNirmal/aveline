# Ava — Memory Concierge

**Ava** is Aveline's dedicated Customer Concierge and Memory agent. Ava guarantees that every client feels known, valued, and attentively remembered, regardless of which stylist assists them on the boutique floor.

---

## Capabilities & Architecture

Ava combines vector-embedded client profiles with temporal event tracking:

- **Unstructured Observation Capture:** Associates can record voice memos, quick notes, or casual remarks ("Prefers unlined linen jackets during July trips to Galle").
- **Preference Extraction:** Identifies silhouettes, preferred designers, dietary requirements, measurement profiles, and color aversions.
- **Milestone Tracking:** Flags upcoming birthdays, anniversaries, and seasonal wardrobing refresh cycles automatically.
- **Privacy Safeguards:** Client data is stored within your boutique's dedicated tenant container and never cross-pollinates across competing retailers.

---

## Memory Representation & Graph

When a stylist submits an observation, Ava decomposes it into verifiable traits:

```json
{
  "client_id": "cli_8921dfa9",
  "memory_type": "fabric_preference",
  "observation": "Enjoys lightweight silk-crepe blends, sensitive to coarse wools.",
  "confidence": 0.96,
  "last_verified": "2026-08-14T11:20:00Z"
}
```

---

## Best Practices for Stylists

1. **Be Specific in Notes:** Mention color tones and specific occasion contexts rather than generic statements.
2. **Review Client Profile Before Appointments:** Ava generates a 30-second "Daily Briefing" for confirmed appointments.
3. **Confirm Inferred Preferences:** When an associate verbally verifies a suggested note with the client, Ava marks it as verified.
