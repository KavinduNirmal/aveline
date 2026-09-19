import {
  Activity,
  BarChart3,
  Building2,
  Coins,
  FileCheck2,
  FileText,
  LayoutDashboard,
  Radio,
  Scale,
  ShieldCheck,
  UserRound,
  Users,
  type LucideIcon,
} from "lucide-react"

import type { Permission } from "@/lib/admin/permissions"
import { ROLE_POLICIES, type Gate } from "@/lib/admin/role-policies"

/**
 * The console's domains. Domains exist so the navigation groups itself by **what the operator
 * is doing**, not by which endpoint a page happens to call.
 */
export type AdminDomain =
  | "overview"
  | "people"
  | "organizations"
  | "operations"
  | "observability"
  | "statistics"

export const ADMIN_DOMAINS: readonly AdminDomain[] = [
  "overview",
  "people",
  "organizations",
  "operations",
  "observability",
  "statistics",
]

export interface AdminRouteDef {
  id: string
  subPath: string
  label: string
  description: string
  icon: LucideIcon
  domain: AdminDomain
  /**
   * The requirement for the entry. `null` is reserved for the Overview domain, which every
   * admitted operator may open.
   */
  gate: Gate | null
  /**
   * False for a route the plan specifies but no slice has built yet. It is registered so the
   * navigation's shape and the registry invariants are settled before the page lands, and it is
   * never linked.
   */
  enabled: boolean
  /** False for a detail route reached from a list, never from the panel. */
  navigable?: boolean
}

/**
 * THE registry. One source of truth for the panel, the router and the registry invariants
 * (`routes.test.ts`). The delivered console had a nav list and a route list that could drift;
 * this cannot.
 */
export const ADMIN_ROUTES: readonly AdminRouteDef[] = [
  {
    id: "dashboard",
    subPath: "dashboard",
    label: "Dashboard",
    description: "System pulse and administrative operations overview",
    icon: LayoutDashboard,
    domain: "overview",
    gate: null,
    enabled: true,
  },
  {
    id: "roles",
    subPath: "roles",
    label: "Roles & Matrix",
    description: "Read-only canonical role-to-permission catalog",
    icon: ShieldCheck,
    domain: "overview",
    gate: { kind: "role", anyOf: ROLE_POLICIES.AdminReview.anyOf },
    enabled: true,
  },

  {
    id: "users",
    subPath: "users",
    label: "Users & Accounts",
    description: "Search users, audit records, and manage lifecycle states",
    icon: Users,
    domain: "people",
    gate: { kind: "permission", permission: "admin:users:read" },
    enabled: true,
  },
  {
    id: "requests",
    subPath: "requests",
    label: "Access Requests",
    description: "Review and approve pending administrator access requests",
    icon: FileCheck2,
    domain: "people",
    gate: { kind: "role", anyOf: ROLE_POLICIES.AdminReview.anyOf },
    enabled: true,
  },
  {
    id: "user-detail",
    subPath: "users/:userId",
    label: "User detail",
    description: "A single user's account state, roles and audit trail",
    icon: UserRound,
    domain: "people",
    gate: { kind: "permission", permission: "admin:users:read" },
    enabled: false,
    navigable: false,
  },

  {
    id: "orgs",
    subPath: "orgs",
    label: "Boutiques & Orgs",
    description: "Multi-tenant boutique overview, tiers, and entitlements",
    icon: Building2,
    domain: "organizations",
    gate: { kind: "permission", permission: "admin:orgs:read" },
    enabled: true,
  },
  {
    id: "org-detail",
    subPath: "orgs/:organizationId",
    label: "Organization detail",
    description: "One organization's entitlements, overrides and ledger",
    icon: Building2,
    domain: "organizations",
    gate: { kind: "permission", permission: "admin:orgs:read" },
    enabled: false,
    navigable: false,
  },

  {
    id: "blossoms",
    subPath: "blossoms",
    label: "Blossom Ledger",
    description: "Idempotent credit, debit, and revoke ledger operations",
    icon: Coins,
    domain: "operations",
    gate: { kind: "permission", permission: "billing:adjust" },
    enabled: true,
  },
  {
    id: "pricing",
    subPath: "pricing",
    label: "Pricing & Rules",
    description: "Pricing book and effective date-window rules",
    icon: Scale,
    domain: "operations",
    gate: { kind: "role", anyOf: ROLE_POLICIES.PricingAdminRead.anyOf },
    enabled: true,
  },
  {
    id: "price-book",
    subPath: "pricing/price-book",
    label: "Price book",
    description: "The resolved price book with effective windows",
    icon: Scale,
    domain: "operations",
    gate: { kind: "role", anyOf: ROLE_POLICIES.PricingAdminRead.anyOf },
    enabled: false,
    navigable: false,
  },

  {
    id: "logs",
    subPath: "logs",
    label: "Real-time Logs",
    description: "Streaming audit event log with pause and filter capabilities",
    icon: Radio,
    domain: "observability",
    gate: { kind: "role", anyOf: ROLE_POLICIES.AuditView.anyOf },
    enabled: true,
  },
  {
    id: "audit",
    subPath: "audit",
    label: "Audit Explorer",
    description: "Comprehensive historical business action ledger",
    icon: FileText,
    domain: "observability",
    gate: { kind: "role", anyOf: ROLE_POLICIES.AuditView.anyOf },
    enabled: true,
  },
  {
    id: "system",
    subPath: "system",
    label: "System Health & Alerts",
    description: "Dependency readiness checks, queues, error rates, and alerts",
    icon: Activity,
    domain: "observability",
    gate: { kind: "role", anyOf: ROLE_POLICIES.StatsSystem.anyOf },
    enabled: true,
  },

  {
    id: "statistics-agents",
    subPath: "statistics/agents",
    label: "Agent Statistics",
    description: "Agent runs, reliability and step attribution",
    icon: BarChart3,
    domain: "statistics",
    gate: { kind: "role", anyOf: ROLE_POLICIES.StatsSystem.anyOf },
    enabled: true,
  },
  {
    id: "statistics-api",
    subPath: "statistics/api",
    label: "API Statistics",
    description: "Request volume, error classes and latency percentiles",
    icon: BarChart3,
    domain: "statistics",
    gate: { kind: "role", anyOf: ROLE_POLICIES.StatsSystem.anyOf },
    enabled: true,
  },
]

/** Every permission any entry declares, for the registry invariant and the drift test. */
export function declaredPermissions(): Permission[] {
  return ADMIN_ROUTES.flatMap((route) =>
    route.gate?.kind === "permission" ? [route.gate.permission] : [],
  )
}

export function routesForDomain(domain: AdminDomain): AdminRouteDef[] {
  return ADMIN_ROUTES.filter((route) => route.domain === domain)
}

/** Entries the navigation panel should link. */
export function navigableRoutes(): AdminRouteDef[] {
  return ADMIN_ROUTES.filter((route) => route.enabled && route.navigable !== false)
}

export function findRouteById(id: string): AdminRouteDef | undefined {
  return ADMIN_ROUTES.find((route) => route.id === id)
}

export function findRouteBySubPath(subPath: string): AdminRouteDef | undefined {
  return ADMIN_ROUTES.find((route) => route.subPath === subPath)
}
