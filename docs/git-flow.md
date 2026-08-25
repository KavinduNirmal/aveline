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
  └── feature/slice3-commerce-margin-calculator
```

### Core Branches
- **`main` / `master`**: Production-ready code. Every commit on `main` represents a stable, deployable release.
- **`development`**: Main integration branch. All feature branches branch off from `development` and merge back into `development`.

### Supporting Branches
- **`feature/<slice>-<description>`**: New features and non-critical enhancements.
- **`fix/<slice>-<description>`**: Bug fixes for issues found in development.
- **`release/<version>`**: Final release stabilization (version bumps, release notes, final testing). Merges to both `main` and `development`.
- **`hotfix/<description>`**: Critical production fixes branched directly from `main`. Merges to both `main` and `development`.

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
- Fill out the Pull Request template checklist.
- Ensure all CI workflow jobs pass green.
- Request review from a teammate.
