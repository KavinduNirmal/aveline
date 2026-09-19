import {
  Activity,
  Building2,
  Coins,
  FileCheck2,
  FileText,
  LayoutDashboard,
  Radio,
  Scale,
  ShieldCheck,
  Users,
  type LucideIcon,
} from "lucide-react"
import type { Permission } from "@/lib/admin/permissions"

export type AdminRouteGroup =
  | "Overview"
  | "People"
  | "Organizations"
  | "Operations"
  | "Observability"
  | "Configuration"

export interface AdminRouteDef {
  id: string
  subPath: string
  label: string
  icon: LucideIcon
  group: AdminRouteGroup
  permission?: Permission
  description?: string
}

export const ADMIN_ROUTES: AdminRouteDef[] = [
  {
    id: "dashboard",
    subPath: "dashboard",
    label: "Dashboard",
    icon: LayoutDashboard,
    group: "Overview",
    description: "System pulse and administrative operations overview",
  },
  {
    id: "users",
    subPath: "users",
    label: "Users & Accounts",
    icon: Users,
    group: "People",
    permission: "admin:users:read",
    description: "Search users, audit records, and manage lifecycle states",
  },
  {
    id: "requests",
    subPath: "requests",
    label: "Access Requests",
    icon: FileCheck2,
    group: "People",
    permission: "approvals:approve",
    description: "Review and approve pending administrator access requests",
  },
  {
    id: "orgs",
    subPath: "orgs",
    label: "Boutiques & Orgs",
    icon: Building2,
    group: "Organizations",
    permission: "admin:orgs:read",
    description: "Multi-tenant boutique overview, tiers, and entitlements",
  },
  {
    id: "blossoms",
    subPath: "blossoms",
    label: "Blossom Ledger",
    icon: Coins,
    group: "Operations",
    permission: "billing:adjust",
    description: "Idempotent credit, debit, and revoke ledger operations",
  },
  {
    id: "pricing",
    subPath: "pricing",
    label: "Pricing & Rules",
    icon: Scale,
    group: "Operations",
    permission: "pricing:view",
    description: "Pricing book and effective date-window rules",
  },
  {
    id: "logs",
    subPath: "logs",
    label: "Real-time Logs",
    icon: Radio,
    group: "Observability",
    permission: "audit:view",
    description: "Streaming audit event log with pause and filter capabilities",
  },
  {
    id: "audit",
    subPath: "audit",
    label: "Audit Explorer",
    icon: FileText,
    group: "Observability",
    permission: "audit:view",
    description: "Comprehensive historical business action ledger",
  },
  {
    id: "system",
    subPath: "system",
    label: "System Health & Alerts",
    icon: Activity,
    group: "Observability",
    permission: "stats:system",
    description: "Dependency readiness checks, queues, error rates, and alerts",
  },
  {
    id: "roles",
    subPath: "roles",
    label: "Roles & Matrix",
    icon: ShieldCheck,
    group: "Configuration",
    description: "Read-only canonical role-to-permission security catalog",
  },
]
