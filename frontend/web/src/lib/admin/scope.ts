import type { AdminUserDto } from "@/types/admin"

/**
 * What the `{user_Id}` URL segment means (strategy C1).
 *
 * `self`      the segment is the caller's own id — the equality case, no request needed.
 * `managed`   the segment resolved to **another user, by exact id**. Admin data is then
 *             shown scoped to that user.
 * `unknown`   the segment is not a GUID, or nothing resolved to it. Never treated as a
 *             guessed user.
 * `forbidden` the segment is well-formed and resolvable but the caller may not look.
 */
export type AdminScope =
  | { kind: "self"; userId: string }
  | { kind: "managed"; userId: string; user: AdminUserDto }
  | { kind: "unknown" }
  | { kind: "forbidden" }

/**
 * Q1 is **unproven**: `GET /admin/users?q=<GUID>` was attempted at A0 against the local API
 * and returned `401`, and no bearer token was available to confirm an exact-`id` hit. Per C1's
 * binding caveat the console therefore stays `self`-only: the segment is a restatement of the
 * caller. Flipping this to `true` is the whole of the change once the probe succeeds — the
 * resolver below is already written and tested for the exact-id rule.
 */
export const MANAGED_SCOPE_ENABLED = false

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** True when the value has the shape of a GUID. */
export function isGuidLike(value: string): boolean {
  return GUID_PATTERN.test(value)
}

export interface ResolveAdminScopeInput {
  segment: string | undefined
  sessionUserId: string | null
  /** Whether the caller holds `admin:users:read`, the resolver's dependency. */
  canReadUsers: boolean
  /** Looks up a user by the segment; must return `null` unless the `id` matches exactly. */
  findUserById: (segment: string) => Promise<AdminUserDto | null>
  /** Overrides `MANAGED_SCOPE_ENABLED`; used by tests and by the A2 probe. */
  managedEnabled?: boolean
}

/**
 * Resolves the segment. Pure and async, with the request injected, so every branch is unit
 * testable without React.
 *
 * Resolution order, with one deliberate deviation from the plan's wording: **equality is
 * checked before the GUID-shape check**, because a Clerk subject id is not a GUID and checking
 * shape first would make a caller's own console unresolvable.
 */
export async function resolveAdminScope(
  input: ResolveAdminScopeInput,
): Promise<AdminScope> {
  const segment = input.segment?.trim() ?? ""
  if (segment.length === 0) return { kind: "unknown" }

  if (input.sessionUserId !== null && segment === input.sessionUserId) {
    return { kind: "self", userId: segment }
  }

  if (!isGuidLike(segment)) return { kind: "unknown" }

  const managedEnabled = input.managedEnabled ?? MANAGED_SCOPE_ENABLED
  if (!managedEnabled) return { kind: "unknown" }

  if (!input.canReadUsers) return { kind: "forbidden" }

  // An exact `id` match is required. `q` is matched against email, name, username and
  // `clerkId` server-side (`UserRepository.cs:61-70`), so a fuzzy hit must **not** become
  // `managed`: showing admin data scoped to a guessed user is the same failure class as the
  // fabricated dashboard, in a quieter form.
  const user = await input.findUserById(segment)
  if (user === null || user.id !== segment) return { kind: "unknown" }

  return { kind: "managed", userId: segment, user }
}
