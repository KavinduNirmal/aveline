import { useClerk, useUser } from '@clerk/react'
import {
  Bell,
  ChevronsUpDown,
  ClipboardCheck,
  CreditCard,
  LayoutDashboard,
  LogOut,
  Plus,
  Settings,
  Share2,
  Shirt,
  Sparkles,
  Store,
  Users,
  UserPlus,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'

import { Blossom } from '@/components/auth/Blossom'
import { Overview } from '@/components/dashboard/Overview'
import { SectionPlaceholder } from '@/components/dashboard/SectionPlaceholder'
import { TeamManagement } from '@/components/dashboard/TeamManagement'

import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'
import { fetchMyOrganizations } from '@/lib/organizations'
import { hasPermission, type Permission } from '@/lib/permissions'
import type {
  OrganizationMembership,
  OrganizationProfileDto,
  OrganizationUsageSummary,
} from '@/types/organization'

type SectionId =
  | 'overview'
  | 'customers'
  | 'catalog'
  | 'approvals'
  | 'integrations'
  | 'team'
  | 'settings'

interface SectionDef {
  id: SectionId
  label: string
  icon: typeof LayoutDashboard
  placeholder?: string
  /** Permission required to view the section; undefined means any admitted role may see it. */
  permission?: Permission
}

const SECTIONS: SectionDef[] = [
  { id: 'overview', label: 'Overview', icon: LayoutDashboard },
  { id: 'customers', label: 'Customers', icon: Users, placeholder: 'Customer concierge & memory', permission: 'customers:view' },
  { id: 'catalog', label: 'Catalog', icon: Shirt, placeholder: 'Visual intelligence & sourcing', permission: 'catalog:view' },
  { id: 'approvals', label: 'Approvals', icon: ClipboardCheck, placeholder: 'Commerce approvals', permission: 'approvals:approve' },
  { id: 'integrations', label: 'Integrations', icon: Share2, placeholder: 'Channel connections', permission: 'settings:manage' },
  { id: 'team', label: 'Team', icon: UserPlus, placeholder: 'Staff & invitations', permission: 'settings:manage' },
  { id: 'settings', label: 'Settings', icon: Settings, placeholder: 'Boutique & plan settings', permission: 'settings:manage' },
]

interface DashboardShellProps {
  organization: OrganizationProfileDto
  usage: OrganizationUsageSummary | null
  /** The caller's boutique role, used to gate nav sections by permission. */
  role: string
}

/** Initials helper for avatar fallbacks (org or user). */
function initialsOf(...parts: Array<string | null | undefined>): string {
  const text = parts.filter(Boolean).join(' ').trim()
  if (!text) return 'A'
  return text
    .split(/\s+/)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

/**
 * Tenant-scoped app shell: a persistent sidebar (boutique identity + navigation + a
 * bottom user card with sign-out) and a top bar carrying the plan and Blossom balance.
 * The Overview section is functional; the remaining sections render placeholders.
 */
export function DashboardShell({ organization, usage, role }: DashboardShellProps) {
  const navigate = useNavigate()
  const { user } = useUser()
  const { signOut } = useClerk()
  const [section, setSection] = useState<SectionId>('overview')
  const [boutiques, setBoutiques] = useState<OrganizationMembership[]>([])

  const allowedSections = SECTIONS.filter(
    (item) => !item.permission || hasPermission(role, item.permission),
  )

  const activeSection =
    section !== 'overview' && !allowedSections.some((s) => s.id === section)
      ? 'overview'
      : section

  // Fetch the caller's other active boutiques so an owner/manager with several can switch
  // tenants from the top bar.
  useEffect(() => {
    let mounted = true
    fetchMyOrganizations()
      .then((memberships) => {
        if (mounted) {
          setBoutiques(
            memberships.filter(
              (m) => m.status === 'Active' && m.slug && m.slug !== organization.slug,
            ),
          )
        }
      })
      .catch(() => {
        /* switcher is best-effort */
      })
    return () => {
      mounted = false
    }
  }, [organization.slug])

  const orgInitials = initialsOf(organization.name)
  const userName = user?.fullName ?? user?.username ?? 'Account'
  const userEmail = user?.primaryEmailAddress?.emailAddress
  const userImage = user?.imageUrl

  const handleSignOut = () => signOut(() => navigate('/sign-in'))

  return (
    <div className="flex min-h-screen bg-background">
      <aside className="sticky top-0 flex h-screen w-64 shrink-0 flex-col border-r bg-background/60 backdrop-blur-sm">
        {/* Boutique identity */}
        <div className="flex items-center gap-3 border-b px-4 py-5">
          <div className="relative size-10 shrink-0 overflow-hidden rounded-full ring-1 ring-primary/20">
            {organization.logoUrl ? (
              <img
                src={organization.logoUrl}
                alt=""
                className="size-full object-cover"
              />
            ) : (
              <span className="flex size-full items-center justify-center bg-primary/10 text-sm font-semibold text-primary">
                {orgInitials}
              </span>
            )}
          </div>
          <div className="min-w-0">
            <p className="truncate font-serif text-lg font-medium leading-tight">
              {organization.name}
            </p>
            <p className="truncate text-xs text-muted-foreground">
              aveline.app/b/{organization.slug}
            </p>
          </div>
        </div>

        {/* Navigation */}
        <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
          {allowedSections.map((item) => {
            const Icon = item.icon
            const active = activeSection === item.id
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => setSection(item.id)}
                className={cn(
                  'flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm font-medium transition-colors',
                  active
                    ? 'bg-primary/10 text-primary'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                )}
              >
                <Icon className="size-4 shrink-0" aria-hidden />
                {item.label}
              </button>
            )
          })}
        </nav>

        {/* User details footer: avatar + account, with an action menu (shadcn-style) */}
        <footer className="border-t px-3 py-3">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className="flex w-full items-center gap-2.5 rounded-lg px-1 py-1.5 text-left transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <div className="relative size-8 shrink-0 overflow-hidden rounded-full ring-1 ring-border">
                  {userImage ? (
                    <img src={userImage} alt="" className="size-full object-cover" />
                  ) : (
                    <span className="flex size-full items-center justify-center bg-primary/10 text-xs font-semibold text-primary">
                      {initialsOf(user?.firstName, user?.lastName)}
                    </span>
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium leading-tight">{userName}</p>
                  <p className="truncate text-xs text-muted-foreground">
                    {userEmail ?? 'Signed in'}
                  </p>
                </div>
                <ChevronsUpDown
                  className="size-4 shrink-0 text-muted-foreground"
                  aria-hidden
                />
              </button>
            </DropdownMenuTrigger>

            <DropdownMenuContent align="start" side="top" className="w-64">
              <DropdownMenuLabel className="px-2 py-2">
                <div className="flex items-center gap-2.5">
                  <div className="relative size-9 shrink-0 overflow-hidden rounded-full ring-1 ring-border">
                    {userImage ? (
                      <img src={userImage} alt="" className="size-full object-cover" />
                    ) : (
                      <span className="flex size-full items-center justify-center bg-primary/10 text-sm font-semibold text-primary">
                        {initialsOf(user?.firstName, user?.lastName)}
                      </span>
                    )}
                  </div>
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold leading-tight">{userName}</p>
                    <p className="truncate text-xs text-muted-foreground">
                      {userEmail ?? 'Signed in'}
                    </p>
                  </div>
                </div>
              </DropdownMenuLabel>

              <DropdownMenuSeparator />

              {hasPermission(role, 'settings:manage') && (
                <DropdownMenuItem onClick={() => setSection('settings')}>
                  <Settings className="size-4" aria-hidden />
                  Settings
                </DropdownMenuItem>
              )}
              <DropdownMenuItem onClick={() => navigate('/plans')}>
                <CreditCard className="size-4" aria-hidden />
                Billing &amp; plan
              </DropdownMenuItem>

              <DropdownMenuSeparator />

              <DropdownMenuItem variant="destructive" onClick={handleSignOut}>
                <LogOut className="size-4" aria-hidden />
                Sign out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </footer>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-10 flex h-16 items-center justify-between gap-4 border-b bg-background/80 px-6 backdrop-blur-sm">
          <div className="flex min-w-0 items-center gap-3">
            {boutiques.length > 0 && (
              <label className="flex items-center gap-1.5 text-sm">
                <Store className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                <span className="sr-only">Switch boutique</span>
                <select
                  value=""
                  aria-label="Switch boutique"
                  onChange={(event) => {
                    const slug = event.target.value
                    if (slug) navigate(`/app/b/${slug}`)
                  }}
                  className="rounded-lg border border-border bg-background px-2 py-1 text-sm"
                >
                  <option value="" disabled>
                    Switch boutique…
                  </option>
                  {boutiques.map((b) => (
                    <option key={b.organizationId} value={b.slug ?? ''}>
                      {b.organizationName ?? b.slug}
                    </option>
                  ))}
                </select>
              </label>
            )}

            {/* Plan information as a pill */}
            <span className="inline-flex items-center gap-1.5 rounded-full border border-primary/15 bg-primary/5 px-3 py-1 text-xs font-medium text-primary">
              <Sparkles className="size-3.5" aria-hidden />
              {organization.planTier} · Blossom plan
            </span>
          </div>

          {/* Blossom balance + top up + notifications */}
          <div className="flex items-center gap-2.5">
            {usage ? (
              <span
                title={`${usage.blossomRemaining.toLocaleString()} of ${usage.monthlyBlossomLimit.toLocaleString()} Blossoms left`}
                className="inline-flex items-center gap-2 rounded-full bg-gradient-to-r from-[#8b2e42] to-[#c05267] px-4 py-1.5 text-sm font-semibold text-white shadow-sm"
              >
                <Blossom className="size-4 text-white" />
                {usage.blossomRemaining.toLocaleString()} Blossoms
              </span>
            ) : (
              <span className="inline-flex items-center gap-2 rounded-full border border-border bg-muted px-4 py-1.5 text-sm font-medium text-muted-foreground">
                Demo mode
              </span>
            )}

            <Button
              variant="outline"
              size="sm"
              className="gap-1.5 rounded-full"
              onClick={() =>
                toast('Top-ups are in demo mode', {
                  description: 'We’ll contact you about billing when payments go live.',
                })
              }
            >
              <Plus className="size-4" aria-hidden />
              Top up
            </Button>

            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" aria-label="Notifications">
                  <Bell className="size-5" aria-hidden />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-80">
                <DropdownMenuLabel>Notifications</DropdownMenuLabel>
                <DropdownMenuSeparator />
                <div className="px-3 py-8 text-center text-sm text-muted-foreground">
                  <p>You're all caught up.</p>
                  <p className="mt-1 text-xs">
                    Approvals, Blossom usage, and team activity will appear here.
                  </p>
                </div>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </header>

        <main className="flex-1 px-6 py-8">
          {activeSection === 'overview' ? (
            <Overview organization={organization} usage={usage} role={role} />
          ) : activeSection === 'team' ? (
            <TeamManagement organization={organization} role={role} />
          ) : (
            (() => {
              const def = allowedSections.find((s) => s.id === activeSection)
              return (
                <SectionPlaceholder
                  title={def?.label ?? ''}
                  icon={def?.icon}
                  description={def?.placeholder}
                />
              )
            })()
          )}
        </main>

      </div>
    </div>
  )
}
