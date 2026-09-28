import { describe, expect, it } from 'vitest'

import {
  BOUTIQUE_ROLES,
  canOpenTenantDashboard,
  hasPermission,
  ROLE_PERMISSIONS,
} from './permissions'

describe('permissions (mirror of Aveline.Api Permissions.cs)', () => {
  it('grants the full permission set to the boutique owner', () => {
    expect(hasPermission('org:boutique_owner', 'settings:manage')).toBe(true)
    expect(hasPermission('org:boutique_owner', 'approvals:approve')).toBe(true)
    expect(hasPermission('org:boutique_owner', 'payments:refund')).toBe(true)
  })

  it('denies settings:manage to supervisors and managers', () => {
    expect(hasPermission('org:boutique_supervisor', 'settings:manage')).toBe(false)
    expect(hasPermission('org:boutique_manager', 'settings:manage')).toBe(false)
  })

  it('grants approvals:approve to the supervisor and the staff member but not the manager', () => {
    expect(hasPermission('org:boutique_supervisor', 'approvals:approve')).toBe(true)
    expect(hasPermission('org:boutique_staff', 'approvals:approve')).toBe(true)
    expect(hasPermission('org:boutique_manager', 'approvals:approve')).toBe(false)
  })

  it('grants the three new write permissions to management and not to staff', () => {
    for (const permission of ['customers:manage', 'team:manage', 'orders:manage'] as const) {
      expect(hasPermission('org:boutique_manager', permission), permission).toBe(true)
      expect(hasPermission('org:boutique_supervisor', permission), permission).toBe(true)
      expect(hasPermission('org:boutique_owner', permission), permission).toBe(true)
      expect(hasPermission('org:boutique_staff', permission), permission).toBe(false)
    }
  })

  it('denies the agentic statistics permission to every boutique role', () => {
    // The boutique's usage unit is the Blossom. Agent runs, tokens and provider cost are agent
    // internals, and no tenant role holds the permission that would reach them.
    for (const role of BOUTIQUE_ROLES) {
      expect(hasPermission(role, 'stats:view:agent'), role).toBe(false)
    }
  })

  it('denies permissions to an unknown or empty role', () => {
    expect(hasPermission('', 'catalog:view')).toBe(false)
    expect(hasPermission(null, 'catalog:view')).toBe(false)
    expect(hasPermission('org:made_up', 'catalog:view')).toBe(false)
  })

  it('does not carry the phantom org:principal role', () => {
    expect(ROLE_PERMISSIONS).not.toHaveProperty('org:principal')
    expect(hasPermission('org:principal', 'catalog:view')).toBe(false)
  })
})

describe('canOpenTenantDashboard', () => {
  it('admits the four canonical boutique roles', () => {
    for (const role of BOUTIQUE_ROLES) {
      expect(canOpenTenantDashboard(role), role).toBe(true)
    }
  })

  it('refuses a role the backend grants nothing, rather than a hand-listed team role', () => {
    expect(canOpenTenantDashboard('staff')).toBe(false)
    expect(canOpenTenantDashboard('owner')).toBe(false)
    expect(canOpenTenantDashboard('admin')).toBe(false)
    expect(canOpenTenantDashboard('org:principal')).toBe(false)
  })

  it('excludes unknown/empty roles', () => {
    expect(canOpenTenantDashboard('')).toBe(false)
    expect(canOpenTenantDashboard(null)).toBe(false)
    expect(canOpenTenantDashboard(undefined)).toBe(false)
  })
})
