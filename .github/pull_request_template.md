## Description
<!-- Provide a brief description of the changes introduced by this pull request. -->

## Git Flow Classification
- [ ] `feature/*` — New feature or enhancement (target: `development`)
- [ ] `fix/*` — Bug fix (target: `development`)
- [ ] `hotfix/*` — Critical fix for production (target: `main` / `master`)
- [ ] `release/*` — Release preparation (target: `main` / `master` & `development`)
- [ ] `chore/*` — Dependency, tooling, or scaffolding changes

## Vertical Slice
- [ ] Slice 1: Customer Concierge & Memory (Student 1)
- [ ] Slice 2: Visual Intelligence & Sourcing (Student 2)
- [ ] Slice 3: Commerce Validation & Optimization (Student 3)
- [ ] Shared Infrastructure / Cross-Cutting

## Quality Checklist
- [ ] My branch is branched from and targeting `development` (unless hotfix/release).
- [ ] I have pulled and rebased the latest changes from `development`.
- [ ] No non-bun lockfiles (`package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`) are committed.
- [ ] No `.env` or sensitive credentials are included.
- [ ] Pre-commit hooks (`.husky/pre-commit`) passed locally.
- [ ] `dotnet build` succeeds for backend changes.
- [ ] Flutter analyze passes for mobile changes.
- [ ] Python syntax and linting checks pass for agent changes.
- [ ] Updated AI usage log in `docs/ai-usage/<student-name>.md`.
