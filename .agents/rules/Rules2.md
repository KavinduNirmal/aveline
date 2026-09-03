---
trigger: always_on
---

# 19. Dependency Changes

Before adding or upgrading a dependency, evaluate:

- Why it is needed
- Whether existing dependencies already provide the functionality
- Maintenance status
- Security implications
- Project compatibility
- License implications
- Long-term complexity

Do not add a dependency for trivial functionality that can reasonably be implemented without one.

Do not perform unrelated dependency upgrades while implementing a feature.

# 20. Performance

Correctness comes before optimization.

Do not optimize based solely on assumptions.

When performance matters:

1. Identify the bottleneck.
2. Measure it.
3. Make the change.
4. Measure again.
5. Ensure correctness remains intact.

Avoid premature caching, concurrency, abstraction, or distributed architecture.

# 21. Concurrency and Async Code

Use asynchronous operations where appropriate.

Do not introduce concurrency merely for the appearance of performance.

Avoid:

- Blocking asynchronous operations
- Uncontrolled parallelism
- Race conditions
- Shared mutable state without synchronization
- Fire-and-forget operations without proper error handling

Cancellation and timeout behavior should be considered for long-running or external operations.

# 22. UI Development

UI components should primarily handle presentation and user interaction.

Do not place significant business logic inside UI components.

Separate:

- Presentation state
- Application state
- Business logic
- Persistence

Reusable components should be genuinely reusable and should not contain assumptions specific to a single page unless those assumptions are part of their defined purpose.

# 23. Generated Code

Generated code must not be manually modified unless the project explicitly requires it.

Determine the source/template/configuration responsible for generated code and modify that instead.

Generated files should only be committed when the project's workflow requires them.

# 24. Before Implementing

Before writing code, the agent should:

1. Understand the requested behavior.
2. Inspect relevant existing code.
3. Identify the appropriate architectural layer.
4. Check relevant `.agents/skills`.
5. Identify existing abstractions that can be reused.
6. Determine the appropriate tests.
7. Identify potential side effects.
8. Only then begin implementation.

Do not immediately start creating files simply because a feature was requested.

# 25. Before Declaring a Task Complete

The agent must verify the work.

At minimum:

- Run relevant tests.
- Run the project's formatter/linter when available.
- Run type checking/build validation when applicable.
- Inspect the final diff.
- Confirm that no unrelated files were modified.
- Confirm that no secrets or credentials were introduced.
- Confirm that documentation is updated when necessary.
- Update the AI usage log.

Never claim that a test, build, migration, or command succeeded unless it was actually executed and verified.

If verification cannot be performed, explicitly state what could not be verified.

# 26. Agent Autonomy and Safety

The agent may independently perform normal development operations necessary to complete the task.

However, the agent must ask for confirmation before performing destructive or irreversible operations, including:

- Deleting important files
- Dropping databases/tables
- Destructive migrations
- Removing major functionality
- Force-resetting Git history
- Force-pushing
- Deleting branches with potentially useful work
- Overwriting user-authored work

Do not assume that "fixing the problem" authorizes destructive actions.

Prefer reversible operations.

# 27. Scope Control

Stay within the scope of the requested task.

If unrelated problems are discovered:

- Do not silently fix them.
- Mention them separately.
- Fix them only if they are necessary for the requested task or explicitly requested.

Avoid turning a small feature into an unrelated refactoring project.

# 28. Existing Code vs. New Code

Prefer extending existing abstractions when they are appropriate.

Before creating:

- A new service
- A new repository
- A new utility
- A new component
- A new DTO
- A new abstraction
- A new dependency

Search the project for an existing equivalent.

Do not create duplicate functionality.

If an existing abstraction is poorly designed and must be replaced, explain why and migrate its consumers deliberately.

# 29. Documentation of Architectural Decisions

Significant architectural decisions should be documented.

Examples include:

- Choosing a database technology
- Introducing a new architectural pattern
- Changing authentication strategy
- Introducing a new external service
- Changing state-management architecture
- Introducing asynchronous messaging
- Changing deployment architecture

Use the project's designated documentation location and follow its existing format.

# 30. General Principle

The agent should optimize for:

Correctness → Security → Maintainability → Testability → Simplicity → Performance

Do not sacrifice architectural integrity merely to produce a faster implementation.

When uncertain, inspect the existing project, its documentation, tests, and relevant skills before making assumptions.

# AI Decision Transparency

The agent must distinguish between:

- Requirements explicitly provided by the developer
- Existing project conventions
- Decisions inferred from the codebase
- Decisions proposed by the agent

When an implementation requires a significant design decision that was not specified, the agent should explain the decision and its alternatives rather than silently treating its assumption as a project requirement.

The agent must not fabricate requirements, test results, documentation, or implementation details.

# 31. UI Components and Design System (shadcn/ui)

When developing or modifying the web frontend:

- **Do NOT create custom UI components or write raw styled HTML elements as a first choice.**
- Always reference and follow the `shadcn` skill ([.agents/skills/shadcn/SKILL.md](file:///run/media/kavindu/Development/Development/3-1/SEF/aveline/.agents/skills/shadcn/SKILL.md)) and its architectural guidelines.
- Always check the list of installed components under `src/components/ui/` before building UI.
- When a required component is not yet installed, add it using the project package runner (`bunx --bun shadcn@latest add <component>`) rather than hand-rolling custom HTML elements or styled divs.
- Compose interfaces using shadcn/ui primitives:
  - **Form Controls**: Use `Input`, `Textarea`, `Label`, `Switch`, `ToggleGroup`, `Checkbox`, `Select` instead of raw `<input>`, `<textarea>`, `<label>`, or custom toggle divs.
  - **Feedback & States**: Use `Alert` for warnings/errors, `Skeleton` for loading states, `Badge` for status tags.
  - **Layout & Surfaces**: Use `Card`, `Avatar`, `Separator`, `Button` instead of unstyled containers or generic tags.
- Always use semantic theme tokens (`bg-background`, `text-foreground`, `text-muted-foreground`, `border-border`, `text-destructive`) to preserve design consistency across themes and prevent hardcoded color literals.

