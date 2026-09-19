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
 * Managed scope is **enabled**, with an exact-`id` confirmation and a safe fallback.
 *
 * Q1's probe (`GET /admin/users?q=<GUID>`) is still unproven from this environment — it returned
 * `401` without a bearer token — so the resolver must not be the only thing standing between an
 * administrator and their own console. Two rules make that safe:
 *
 * 1. `managed` resolves **only** on an exact `id` match; a fuzzy name or email hit is `unknown`.
 * 2. The guard treats `unknown` as *"fall back to the caller's own console"*, not as a dead end.
 *    That is C1's option (c): when the segment cannot be resolved, it becomes a restatement of the
 *    caller rather than a guessed user.
 */
export const MANAGED_SCOPE_ENABLED = true

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** True when the value has the shape of a GUID. */
export function isGuidLike(value: string): boolean {
  return GUID_PATTERN.test(value)
}

export interface ResolveAdminScopeInput {
  segment: string | undefined
  /**
   * The caller's Clerk subject (`sub`), as `/auth/claims` returns it.
   *
   * **This is not the id the URL carries.** `AuthEndpoints` resolves `userId` from
   * `ClaimTypes.NameIdentifier ?? "sub"`, which is the Clerk id (`user_…`), while the console's
   * URLs are built from the application user's **database id** — a UUIDv7 `Guid`
   * (`User.cs:11`) returned by `GET /users/me`. Comparing the segment against only this value can
   * therefore never match a real console URL.
   */
  sessionUserId: string | null
  /**
   * Every id that means "this is the caller". At minimum the application user's database id and
   * the Clerk subject, because both appear in the wild.
   */
  selfUserIds?: readonly string[]
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

  const selfIds = new Set(
    [input.sessionUserId, ...(input.selfUserIds ?? [])].filter(
      (value): value is string => typeof value === "string" && value.length > 0,
    ),
  )
  if (selfIds.has(segment)) {
    return { kind: "self", userId: segment }
  }

  if (!isGuidLike(segment)) return { kind: "unknown" }

  const managedEnabled = input.managedEnabled ?? MANAGED_SCOPE_ENABLED
  if (!managedEnabled) return { kind: "unknown" }

  if (!input.canReadUsers) return { kind: "forbidden" }

  // An exact `id` match is required. `q` is matched against email, name, username and
  // `clerkId` server-side (`UserRepository.cs:61-70`) — **not** against `id` — so a hit must be
  // confirmed by re-reading `user.id`, and a fuzzy match must never become `managed`.
  const user = await input.findUserById(segment)
  if (user === null || user.id !== segment) return { kind: "unknown" }

  return { kind: "managed", userId: segment, user }
}
