# Features — Flutter Feature Modules

This folder contains one sub-folder per domain slice. Each feature is self-contained with
its own `data/`, `domain/`, and `presentation/` layers following clean architecture.

## Structure

```
features/
├── customers/      # Slice 1 — Customer profiles, interactions, memory
├── inventory/      # Slice 2 — Inventory browsing, image analysis, sourcing
└── commerce/       # Slice 3 — Orders, payments, approvals, delivery
```

## Layer responsibilities per feature

| Layer | Responsibility |
|---|---|
| `data/` | API client calls, local data sources, repository implementations, response models |
| `domain/` | Entities, abstract repository interfaces, use cases (business logic) |
| `presentation/` | Screens, widgets, state management (Provider/Riverpod/BLoC — TBD) |

## Rules

- Features should NOT import from each other's `presentation/` or `domain/` layers
- Shared UI components go in `lib/shared/widgets/`, not inside a feature
- Network setup goes in `lib/core/network/`, not inside `data/`
