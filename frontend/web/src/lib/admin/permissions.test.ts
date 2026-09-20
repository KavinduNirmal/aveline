import { describe, expect, it } from "vitest"
import {
  ALL_PERMISSIONS,
  ROLE_PERMISSIONS,
  hasAdminPermission,
  resolvePermissions,
} from "./permissions"

describe("Admin Permissions Catalog Mirror", () => {
  it("contains exactly 28 canonical permissions", () => {
    expect(ALL_PERMISSIONS).toHaveLength(28)
    const unique = new Set(ALL_PERMISSIONS)
    expect(unique.size).toBe(28)
  })

  it("mirrors admin role omitting exactly the named denials", () => {
    // `pricing:backdate` is the original denial (slice A6 gates the recompute control on it);
    // `revenue:refund` joins it because sending money back is not the same authority as
    // correcting the ledger. Both are named in `PermissionsDeniedToAdmin` on the server.
    const adminPerms = ROLE_PERMISSIONS["admin"]
    expect(adminPerms).toHaveLength(26)
    expect(adminPerms).not.toContain("pricing:backdate")
    expect(adminPerms).not.toContain("revenue:refund")
    expect(adminPerms).toContain("revenue:read")
    expect(adminPerms).toContain("revenue:manage")
    expect(adminPerms).toContain("audit:view")
    expect(adminPerms).toContain("admin:users:manage")
  })

  it("mirrors owner role granting all permissions", () => {
    const ownerPerms = ROLE_PERMISSIONS["owner"]
    expect(ownerPerms).toHaveLength(28)
    expect(ownerPerms).toContain("pricing:backdate")
    expect(ownerPerms).toContain("revenue:refund")
  })

  it("mirrors moderator permissions accurately", () => {
    const modPerms = ROLE_PERMISSIONS["moderator"]
    expect(modPerms).toContain("admin:orgs:read")
    expect(modPerms).toContain("stats:view")
    expect(modPerms).toContain("analytics:business:read")
    // A moderator reads revenue and never moves it.
    expect(modPerms).toContain("revenue:read")
    expect(modPerms).not.toContain("revenue:manage")
    expect(modPerms).not.toContain("revenue:refund")
    expect(modPerms).not.toContain("admin:users:read")
    expect(modPerms).not.toContain("audit:view")
  })

  it("resolves combined permissions for multiple roles", () => {
    const perms = resolvePermissions(["moderator", "org:boutique_manager"])
    expect(perms.has("admin:orgs:read")).toBe(true)
    expect(perms.has("catalog:manage")).toBe(true)
    expect(perms.has("pricing:view")).toBe(true)
    expect(perms.has("audit:view")).toBe(false)
  })

  it("correctly checks hasAdminPermission", () => {
    expect(hasAdminPermission(["admin"], "audit:view")).toBe(true)
    expect(hasAdminPermission(["moderator"], "audit:view")).toBe(false)
    expect(hasAdminPermission(["moderator"], "admin:orgs:read")).toBe(true)
    expect(hasAdminPermission([], "catalog:view")).toBe(false)
    expect(hasAdminPermission(null, "catalog:view")).toBe(false)
  })
})
