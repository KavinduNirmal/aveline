import { describe, expect, it } from 'vitest'

import { hasPermission, isTenantAdmin } from './permissions'

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

  it('grants approvals:approve to the supervisor but not the manager', () => {
    expect(hasPermission('org:boutique_supervisor', 'approvals:approve')).toBe(true)
    expect(hasPermission('org:boutique_manager', 'approvals:approve')).toBe(false)
  })

  it('denies permissions to an unknown or empty role', () => {
    expect(hasPermission('', 'catalog:view')).toBe(false)
    expect(hasPermission(null, 'catalog:view')).toBe(false)
    expect(hasPermission('org:made_up', 'catalog:view')).toBe(false)
  })
})

describe('isTenantAdmin', () => {
  it('admits owner, manager, supervisor and team admin roles', () => {
    expect(isTenantAdmin('org:boutique_owner')).toBe(true)
    expect(isTenantAdmin('org:boutique_manager')).toBe(true)
    expect(isTenantAdmin('org:boutique_supervisor')).toBe(true)
    expect(isTenantAdmin('owner')).toBe(true)
    expect(isTenantAdmin('admin')).toBe(true)
  })

  it('excludes staff and unknown/empty roles', () => {
    expect(isTenantAdmin('org:boutique_staff')).toBe(false)
    expect(isTenantAdmin('staff')).toBe(false)
    expect(isTenantAdmin('')).toBe(false)
    expect(isTenantAdmin(null)).toBe(false)
    expect(isTenantAdmin(undefined)).toBe(false)
  })
})
