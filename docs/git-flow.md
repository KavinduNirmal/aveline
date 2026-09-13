# Git Flow Guide — Aveline

This project follows the **Git Flow** branching model adapted for multi-developer team collaboration and automated CI validation.

---

## 1. Branch Architecture

```
main (Production releases)
  │
  ├── hotfix/* (Emergency production patches)
  │
  └── release/* (Release preparation)
        │
development (Integration branch — default for all active work)
  │
  ├── feature/slice1-customer-memory-search
  ├── feature/slice2-image-attribute-extractor
  ├── feature/slice3-commerce-margin-calculator
  │
  └── fix/salon-chat-ui             (branched off development)
        │
        └── fix/salon-agent-state   (branched off the fix above — chained, see §1.1)
```

### Core Branches
- **`main` / `master`**: Production-ready code. Every commit on `main` represents a stable, deployable release.
- **`development`**: Main integration branch. All feature branches branch off from `development` and merge back into `development`.

### Supporting Branches
- **`feature/<slice>-<description>`**: New features and non-critical enhancements.
- **`fix/<slice>-<description>`**: Bug fixes for issues found in development. Branch from `development`, unless the fix depends on a fix that has not merged yet — then chain it (§1.1).
- **`release/<version>`**: Final release stabilization (version bumps, release notes, final testing). Merges to both `main` and `development`.
- **`hotfix/<description>`**: Critical production fixes branched directly from `main`. Merges to both `main` and `development`.

---

## 1.1 Chained Fix Branches

A bug fix sometimes **depends on another fix that has not merged yet**. Branching from
`development` in that case silently drops the dependency: the new branch builds, but the fix it
relies on is simply absent, so the original bug reproduces (or a new crash appears) and the
cause looks unrelated to the change being made.

**Rule:** branch a dependent fix from the **tip of the fix branch it depends on**, not from
`development`.

```bash
# Independent fix: branch from development
git checkout development
git pull origin development
git checkout -b fix/salon-activity-bubble

# Dependent fix: branch from the tip of the fix it needs
git checkout fix/salon-chat-ui
git pull                          # only if that branch is already pushed
git checkout -b fix/salon-agent-state
```

Consequences to keep in mind:

- **The later branch contains the earlier one.** `fix/salon-agent-state` includes every commit
  of `fix/salon-chat-ui`, which in turn includes its own base. Confirm the dependency really is
  present before building, installing or reviewing anything:
  ```bash
  git log --oneline origin/development..HEAD
  ```
- **One pull request carries the whole chain.** Open it from the chain tip against
  `development` and list the fixes it carries in the description. Do **not** also open pull
  requests for the intermediate branches, or the same commits get reviewed and merged twice.
- **Intermediate branches can be deleted once the tip is pushed.** Their commits stay reachable
  from the tip, so nothing is lost. `git branch -d fix/salon-chat-ui` only succeeds while the
  tip contains that branch — the refusal is the safety net telling you it does not.
- **Keep the chain linear, and only rewrite it while it is unpushed.** Rebase a fix onto its
  dependency rather than merging. Once a branch has been pushed, do not rebase or force-push
  it: teammates may already have based work on it.
- **Merging the chain merges every fix in it.** A chain is a packaging choice, not a promise
  that the fixes belong together, so only chain fixes that should land together.

---

## 2. Branch Naming Conventions

Always use kebab-case with descriptive names:

| Type | Format | Example |
|------|--------|---------|
| **Feature** | `feature/<slice>-<name>` | `feature/slice1-whatsapp-webhook` |
| **Bugfix** | `fix/<slice>-<name>` | `fix/slice3-payment-callback-null` |
| **Release** | `release-v<X.Y.Z>` | `release-v1.0.0` |
| **Hotfix** | `hotfix/<name>` | `hotfix/clerk-jwt-expiry-patch` |
| **Chore** | `chore/<name>` | `chore/update-nuget-packages` |

---

## 3. Daily Workflow

### 1. Starting a Feature
```bash
# Switch to development and pull latest
git checkout development
git pull origin development

# Create your feature branch
git checkout -b feature/slice1-customer-profile
```

### 2. Developing and Committing
- Keep commits small, logical, and focused.
- Use **Conventional Commits**:
  - `feat(customer): add customer profile repository`
  - `test(customer): add unit tests for profile lookup`
  - `fix(customer): handle missing phone number format`
- The Husky pre-commit hook will run 6 automated checks before allowing the commit.

### 3. Staying Up-to-Date
Pull changes from `development` frequently:
```bash
git fetch origin development
git rebase origin/development
```

### 4. Opening a Pull Request
- Target branch: **`development`** (never merge features directly into `main`).
- Open the PR from the **tip** of a chained `fix/*` branch, and list the fixes the chain
  carries in the description (§1.1).
- Fill out the Pull Request template checklist.
- Ensure all CI workflow jobs pass green.
- Request review from a teammate.
