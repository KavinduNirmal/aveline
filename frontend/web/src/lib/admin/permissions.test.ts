import { describe, expect, it } from "vitest"
import {
  ALL_PERMISSIONS,
  ROLE_PERMISSIONS,
  hasAdminPermission,
  resolvePermissions,
} from "./permissions"

describe("Admin Permissions Catalog Mirror", () => {
  it("contains exactly 25 canonical permissions", () => {
    expect(ALL_PERMISSIONS).toHaveLength(25)
    const unique = new Set(ALL_PERMISSIONS)
    expect(unique.size).toBe(25)
  })

  it("mirrors admin role omitting only pricing:backdate", () => {
    const adminPerms = ROLE_PERMISSIONS["admin"]
    expect(adminPerms).toHaveLength(24)
    expect(adminPerms).not.toContain("pricing:backdate")
    expect(adminPerms).toContain("audit:view")
    expect(adminPerms).toContain("admin:users:manage")
  })

  it("mirrors owner role granting all permissions", () => {
    const ownerPerms = ROLE_PERMISSIONS["owner"]
    expect(ownerPerms).toHaveLength(25)
    expect(ownerPerms).toContain("pricing:backdate")
  })

  it("mirrors moderator permissions accurately", () => {
    const modPerms = ROLE_PERMISSIONS["moderator"]
    expect(modPerms).toContain("admin:orgs:read")
    expect(modPerms).toContain("stats:view")
    expect(modPerms).toContain("analytics:business:read")
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
