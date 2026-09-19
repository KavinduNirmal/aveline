import type { Permission } from "@/lib/admin/permissions"

/**
 * A navigation or control gate. Two kinds, because the console has two kinds of requirement:
 * a permission the catalogue can express, and a **role policy** the catalogue cannot.
 *
 * `audit:view`, `stats:system` and `pricing:view` are registered as permission policies for
 * every entry in `Permissions.All` (`AuthorizationConfiguration.cs:265-268`) and referenced by
 * **no endpoint**, while the routes that own those names use `RequireRole(Owner, Admin)`
 * (`:258,260,262`). A gate built on the catalogue alone therefore cannot answer "may this user
 * open Audit?" — not because of the moderator case, but in general.
 */
export type Gate =
  | { kind: "permission"; permission: Permission }
  | { kind: "role"; anyOf: readonly string[] }

/**
 * Mirrors the four `RequireRole(...)` registrations in
 * `AuthorizationConfiguration.cs:167-168,258,260,262` **by name**. `role-policies.sync.test.ts`
 * is the mechanism that keeps it true.
 */
export const ROLE_POLICIES = {
  AdminReview: { policy: "AdminReview", anyOf: ["moderator", "admin", "owner"] },
  StatsSystem: { policy: "StatsSystem", anyOf: ["owner", "admin"] },
  AuditView: { policy: "AuditView", anyOf: ["owner", "admin"] },
  PricingAdminRead: { policy: "PricingAdminRead", anyOf: ["owner", "admin"] },
} as const

export type RolePolicyName = keyof typeof ROLE_POLICIES

export interface GateContext {
  roles: readonly string[]
  can: (permission: Permission) => boolean
}

/** The four mirrored policy names, in registration order. */
export function policyNames(): RolePolicyName[] {
  return Object.keys(ROLE_POLICIES) as RolePolicyName[]
}

/** Whether a mirrored role policy admits the caller. */
export function resolveRolePolicy(
  policy: { readonly anyOf: readonly string[] },
  context: GateContext,
): boolean {
  const held = new Set(context.roles.map((role) => role.toLowerCase()))
  return policy.anyOf.some((role) => held.has(role.toLowerCase()))
}

/** Whether the caller may open what the gate protects. */
export function canOpenGate(gate: Gate, context: GateContext): boolean {
  return gate.kind === "permission"
    ? context.can(gate.permission)
    : gate.anyOf.some((role) =>
        context.roles.some((held) => held.toLowerCase() === role.toLowerCase()),
      )
}
