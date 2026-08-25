---
trigger: always_on
---

## 1. Project Context

### 1.1 User Information

Before performing development work, read:

`USER_INFORMATION_FOR_AGENT.md`

This document contains information about the student/developer working with the agent and may be required for project documentation and AI-usage logging.

Do not modify this file unless explicitly instructed.

### 1.2 Existing Project Documentation

Before making architectural or structural changes:

- Inspect the relevant project documentation.
- Inspect existing code before introducing new patterns.
- Follow established project conventions where they do not conflict with these rules.
- Do not introduce a new architectural pattern merely because it is preferred by the agent.

The existing project is the source of truth for implementation conventions unless explicitly overridden by these rules.

# 2. AI Usage Logging

AI usage logging is mandatory.

At the beginning of every development session:

1. Read `docs/ai-usage/README.md`.
2. Read the relevant existing student log if one exists.
3. Create or update:

`docs/ai-usage/<student-name>.md`

Record the beginning of the session and the intended work.

At the end of every development session, update the same file with:

- Work performed
- Files created or modified
- Tests created or modified
- Important architectural decisions
- Problems encountered
- Verification performed
- Remaining work, if any

Do not bypass AI usage logging.

If the session ends unexpectedly before the final update can be written, update the log at the beginning of the next session with the missing information when possible.

# 3. Package Management

The preferred JavaScript/TypeScript package manager is:

`bun`

Use:

- `bun`
- `bunx`

If Bun is unavailable:

1. Check for `pnpm`.
2. Use `pnpm` if available.
3. Fall back to `npm` only when neither Bun nor pnpm is available.

Do not mix package managers within the same project.

Respect the project's existing lockfile.

Never delete or regenerate a lockfile unnecessarily.

Before installing a dependency, determine whether the functionality can reasonably be implemented using:

- Existing project dependencies
- The standard library
- Existing framework functionality

Avoid unnecessary dependencies.

# 4. Skills and Agent Resources

Project-specific agent skills are available under:

`.agents/skills/`

Before implementing a non-trivial feature, determine whether a relevant skill exists.

Relevant skills may include:

- .NET
- Dart
- Flutter
- Testing
- Architecture
- Database
- API development
- Security
- DevOps
- Documentation

Do not blindly inspect every available skill.

Search for and read skills relevant to the current task.

Project-specific skills take precedence over generic assumptions when they provide explicit project guidance.

# 5. Architecture

The project must maintain a clear separation of responsibilities.

The architecture follows a layered structure:

`Presentation → Application → Domain → Infrastructure`

The exact naming may vary depending on the technology, but the dependency direction must remain consistent.

### Presentation

Responsible for:

- UI
- Controllers
- Routes
- Request/response models
- User interaction
- Presentation-specific validation

Presentation must not contain business rules or directly access persistence.

### Application

Responsible for:

- Use cases
- Application workflows
- Orchestration
- Application-level validation
- Coordinating domain operations

Application code should not depend directly on presentation concerns.

### Domain

Responsible for:

- Business rules
- Domain entities
- Value objects
- Domain services
- Domain-specific invariants

The domain should remain independent of frameworks and infrastructure wherever practical.

### Infrastructure

Responsible for:

- Database access
- External APIs
- File systems
- Messaging systems
- Authentication providers
- Framework-specific persistence implementations

Infrastructure must implement abstractions defined by the appropriate inner layer rather than forcing business logic to depend on infrastructure details.

### Dependency Rule

A layer must not bypass the layer immediately below it to access another layer.

For example:

- Presentation must not directly access repositories.
- Presentation must not directly access the database.
- Domain must not depend on controllers.
- Domain must not depend on database implementations.
- Business logic must not be placed inside UI components merely for convenience.

If an exception is genuinely necessary, document the reason.

# 6. Single Responsibility

Every class, method, function, component, and module should have a clear and focused responsibility.

Avoid:

- God classes
- God functions
- Massive controllers
- Massive UI components
- Business logic inside database repositories
- Database logic inside business services
- Validation duplicated across unrelated layers
- Utility classes containing unrelated functionality

A function should generally perform one coherent operation.

If a function requires extensive branching because it performs several unrelated responsibilities, consider decomposing it.

Do not split code into arbitrary micro-abstractions merely to satisfy a rule.

Prefer meaningful cohesion over excessive fragmentation.

# 7. Test-Driven Development

Development should follow a test-first approach.

For new behavior:

1. Define the expected behavior.
2. Write the test.
3. Run the test and confirm that it fails for the expected reason.
4. Implement the minimum code necessary to satisfy the test.
5. Run the test again.
6. Refactor while keeping the tests passing.

Do not write implementation first and create superficial tests afterward.

Tests should verify observable behavior rather than implementation details.

Not every trivial change requires a new test, but every meaningful behavioral change must have appropriate test coverage.

When fixing a bug:

1. Reproduce the bug with a test where practical.
2. Confirm the test fails.
3. Implement the fix.
4. Confirm the test passes.
5. Run relevant regression tests.

# 8. Testing Standards

Tests should be:

- Deterministic
- Independent
- Repeatable
- Readable
- Focused
- Fast where practical

Avoid tests that depend unnecessarily on:

- Real external services
- Current time
- Random values
- Network availability
- Shared mutable state
- Execution order

Use integration or end-to-end tests where behavior crosses architectural boundaries and unit tests would provide insufficient confidence.

Do not mock everything automatically.

Mock external dependencies when necessary, but prefer testing real internal behavior.

Tests should clearly communicate:

- Given the initial state
- When an operation occurs
- Then the expected behavior occurs

# 9. Code Quality

Code must prioritize:

- Readability
- Maintainability
- Consistency
- Explicitness
- Testability
- Correctness

Follow the language's established conventions.

Prefer simple solutions over clever solutions.

Avoid premature abstraction.

Avoid premature optimization unless performance is an explicit requirement.

Do not introduce patterns, frameworks, abstractions, or dependencies without a concrete reason.

Code should be understandable by another student/developer joining the project.

# 10. Naming

Names must communicate intent.

Prefer:

- `calculateTotalPrice()`
- `getCustomerOrders()`
- `validateApplication()`

Over vague names such as:

- `process()`
- `handle()`
- `doStuff()`
- `data`
- `manager`

Avoid unnecessary abbreviations.

Use the project's established naming conventions consistently.

Names should reflect the domain language used by the project.

# 11. Comments and Documentation

Comments must explain intent, reasoning, constraints, or non-obvious behavior.

Do not write comments that merely restate the code.

Bad:

```

// Increment counter
counter++;

```

Good:

```

// Retry only transient failures because permanent validation failures
// should not trigger another request.

```

Complex algorithms, architectural decisions, workarounds, and externally imposed constraints should be documented.

Public APIs and important domain abstractions should have appropriate documentation according to the language/framework conventions.

Update documentation when behavior or architecture changes.

# 12. Error Handling

Errors must be handled intentionally.

Do not:

- Swallow exceptions
- Use empty catch blocks
- Return meaningless error messages
- Expose internal implementation details to users
- Use exceptions for ordinary control flow where inappropriate

Errors should be handled at the appropriate architectural boundary.

User-facing errors should be understandable.

Developer-facing logs should contain enough information to diagnose failures without exposing sensitive information.

# 13. Validation

Validate input at appropriate boundaries.

Distinguish between:

- Input validation
- Business-rule validation
- Infrastructure failures

Do not duplicate the same business rule across multiple unrelated layers.

Business invariants should ultimately be protected by the domain/application layer rather than relying exclusively on UI validation.

Never trust client-side validation as the only security or correctness mechanism.

# 14. Database and Persistence

Persistence concerns must remain isolated from business logic.

Do not place business rules inside:

- SQL queries
- ORM configuration
- Repository implementations
- Database models

Database schemas, migrations, and persistence models must be changed deliberately.

Do not modify or delete existing data through destructive operations unless explicitly authorized.

When changing database structure:

1. Update the appropriate migration/schema.
2. Update persistence models.
3. Update affected application logic.
4. Update tests.
5. Verify the migration against a safe environment.

# 15. API Design

APIs must be:

- Consistent
- Predictable
- Validated
- Properly documented
- Versioned when necessary

Use appropriate HTTP semantics where applicable.

Do not expose internal domain entities directly when doing so couples the API to internal implementation details.

Use dedicated request/response DTOs where appropriate.

API error responses should follow one consistent project-wide structure.

# 16. Security

Never commit:

- Passwords
- API keys
- Access tokens
- Private keys
- Database credentials
- Personal secrets

Use environment variables or the project's approved secret-management mechanism.

Do not log sensitive information.

Treat all external input as untrusted.

Use parameterized database queries or the framework's safe query mechanisms.

Do not disable authentication, authorization, TLS, validation, or security controls merely to make development easier.

If a security-sensitive change is required, explicitly identify and review its implications.

# 17. Configuration

Environment-specific configuration must not be hardcoded.

Separate:

- Development configuration
- Testing configuration
- Production configuration

Do not commit local machine paths, credentials, or environment-specific secrets.

Use the project's existing configuration system.

# 18. Git and Change Management

Keep changes focused.

A task should not unnecessarily modify unrelated files.

Do not:

- Rewrite unrelated code
- Reformat the entire project without reason
- Rename unrelated files
- Upgrade dependencies without justification
- Remove working functionality merely because it is stylistically different

Before completing a task, inspect the diff.

Ensure that generated files, build artifacts, secrets, and temporary files are not accidentally included.

Use clear commit messages when commits are requested.
