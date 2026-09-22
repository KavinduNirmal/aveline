import { useClerk, useUser } from '@clerk/react'
import {
  BarChart3,
  ChevronsUpDown,
  ClipboardCheck,
  CreditCard,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Plus,
  Settings,
  Share2,
  Shirt,
  Sparkles,
  Store,
  Users,
  UserPlus,
  Coins,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { toast } from 'sonner'

import { Blossom } from '@/components/auth/Blossom'
import { AvelineChatDrawer } from '@/components/conversation/AvelineChatDrawer'
import { AvelineChatLauncher } from '@/components/conversation/AvelineChatLauncher'
import { SalonPanel } from '@/components/conversation/SalonPanel'
import { Overview } from '@/components/dashboard/Overview'
import { ApprovalsPanel } from '@/components/dashboard/ApprovalsPanel'
import { CustomersPanel } from '@/components/dashboard/CustomersPanel'
import { IncomePanel } from '@/components/dashboard/IncomePanel'
import { BillingPanel } from '@/components/dashboard/billing/BillingPanel'
import { SettingsPanel } from '@/components/dashboard/settings/SettingsPanel'
import { SectionPlaceholder } from '@/components/dashboard/SectionPlaceholder'
import { TeamManagement } from '@/components/dashboard/TeamManagement'
import { UpgradePanel } from '@/components/dashboard/UpgradePanel'
import { IntegrationsPanel } from '@/components/dashboard/IntegrationsPanel'
import { NotificationBell } from '@/components/dashboard/NotificationBell'
import { UsagePanel } from '@/components/dashboard/UsagePanel'
import { CatalogPanel } from '@/components/catalog/CatalogPanel'
import { ConversationsProvider } from '@/contexts/ConversationsContext'

import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { cn } from '@/lib/utils'
import { fetchMyOrganizations } from '@/lib/organizations'
import { hasPermission, type Permission } from '@/lib/permissions'
import { DASHBOARD_WINDOWS, useDashboardWindow } from '@/hooks/useDashboardWindow'
import type {
  OrganizationMembership,
  OrganizationProfileDto,
  OrganizationUsageSummary,
} from '@/types/organization'

type SectionId =
  | 'overview'
  | 'salon'
  | 'customers'
  | 'catalog'
  | 'income'
  | 'approvals'
  | 'integrations'
  | 'team'
  | 'usage'
  | 'billing'
  | 'settings'
  // Not a nav entry: `/app/b/:slug/upgrade` is reached from the Usage and Billing CTAs.
  | 'upgrade'

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
  { id: 'salon', label: 'Salon', icon: MessageSquare },
  { id: 'customers', label: 'Customers', icon: Users, placeholder: 'Customer concierge & memory', permission: 'customers:view' },
  { id: 'catalog', label: 'Catalog', icon: Shirt, placeholder: 'Visual intelligence & sourcing', permission: 'catalog:view' },
  // The shop's own takings. `reports:view` is the permission the grant map already gave manager,
  // supervisor and owner; staff see a reduced card on Overview instead of this register.
  { id: 'income', label: 'Income', icon: Coins, placeholder: 'Takings, register & reconciliation', permission: 'reports:view' },
  { id: 'approvals', label: 'Approvals', icon: ClipboardCheck, placeholder: 'Commerce approvals', permission: 'approvals:approve' },
  { id: 'integrations', label: 'Integrations', icon: Share2, placeholder: 'Channel connections', permission: 'settings:manage' },
  // `team:manage`, not `settings:manage`: a manager may manage staff without also reaching
  // Integrations, where the WhatsApp and payment-gateway credentials live (T0a / TD5.5).
  { id: 'team', label: 'Team', icon: UserPlus, placeholder: 'Staff & invitations', permission: 'team:manage' },
  { id: 'usage', label: 'Usage', icon: BarChart3 },
  // "invoices" was a promise the repository cannot keep: no Invoice entity, no payment-provider
  // client and no currency column exist (TD8). The section is a statement of account.
  { id: 'billing', label: 'Billing', icon: CreditCard, placeholder: 'Plan, statement & payment methods', permission: 'billing:view' },
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
  // The section is part of the URL (`/app/b/:slug/:section`), not component state, so a section
  // is linkable and survives a refresh. The bare slug route redirects here with `overview`.
  const { section: sectionParam, itemId: catalogItemId } = useParams<{
    section?: string
    itemId?: string
  }>()
  // One window for the whole shell: every KPI panel reads this value, so two panels on the same
  // screen cannot describe different periods.
  const dashboardWindow = useDashboardWindow('30d')
  const [boutiques, setBoutiques] = useState<OrganizationMembership[]>([])
  const [currentUserId, setCurrentUserId] = useState<string | null>(null)
  const [chatOpen, setChatOpen] = useState(false)

  const allowedSections = SECTIONS.filter(
    (item) => !item.permission || hasPermission(role, item.permission),
  )

  // An unknown segment falls back to `overview` rather than rendering nothing, and a section the
  // role may not open is refused by the same `allowedSections` filter the nav uses — so a
  // hand-typed URL cannot render a panel the nav hides.
  //
  // The piece route (`/app/b/:slug/catalog/:itemId`) spells `catalog` as a literal, so it carries an
  // `itemId` param and **no** `section` param. Reading only `section` therefore sent a piece URL to
  // `overview`; an `itemId` is what says the catalog is the section being viewed.
  const section: SectionId = catalogItemId
    ? 'catalog'
    : SECTIONS.some((item) => item.id === sectionParam) || sectionParam === 'upgrade'
      ? (sectionParam as SectionId)
      : 'overview'

  const activeSection =
    section !== 'overview' && !allowedSections.some((s) => s.id === section)
      ? 'overview'
      : section

  const goToSection = (next: SectionId) => navigate(`/app/b/${organization.slug}/${next}`)

  // Fetch the caller's other active boutiques so an owner/manager with several can switch
  // tenants from the top bar. The same response carries the caller's own membership id, which the
  // Team section needs to disable their own row with a stated reason rather than letting the server
  // answer with a 409.
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
          setCurrentUserId(
            memberships.find((m) => m.organizationId === organization.id)?.userId ?? null,
          )
        }
      })
      .catch(() => {
        /* switcher is best-effort */
      })
    return () => {
      mounted = false
    }
  }, [organization.id, organization.slug])

  const orgInitials = initialsOf(organization.name)
  const userName = user?.fullName ?? user?.username ?? 'Account'
  const userEmail = user?.primaryEmailAddress?.emailAddress
  const userImage = user?.imageUrl

  const handleSignOut = () => signOut(() => navigate('/sign-in'))

  return (
    <ConversationsProvider organizationId={organization.id}>
      <div className="flex min-h-screen bg-background">
        <aside className="sticky top-0 flex h-screen w-64 shrink-0 flex-col border-r bg-background/60 backdrop-blur-sm">
        {/* Boutique identity */}
        <div className="flex h-16 items-center gap-3 border-b px-4">
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
            <span className="mt-1 inline-flex items-center gap-1 rounded-full border border-primary/15 bg-primary/5 px-2 py-0.5 text-[11px] font-medium text-primary">
              <Sparkles className="size-3" aria-hidden />
              {organization.planTier} · Blossom plan
            </span>
          </div>
        </div>

        {/* Navigation */}
        <nav className="flex-1 gap-1 overflow-y-auto px-3 py-4">
          {allowedSections.map((item) => {
            const Icon = item.icon
            const active = activeSection === item.id
            return (
              <Button
                key={item.id}
                type="button"
                variant="ghost"
                onClick={() => goToSection(item.id)}
                className={cn(
                  'flex h-auto w-full items-center justify-start gap-3 rounded-lg px-3 py-2 text-left text-sm font-medium',
                  active
                    ? 'bg-primary/10 text-primary hover:bg-primary/10 hover:text-primary'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                )}
              >
                <Icon className="size-4 shrink-0" aria-hidden />
                {item.label}
              </Button>
            )
          })}
        </nav>

        {/* User details footer: avatar + account, with an action menu (shadcn-style) */}
        <footer className="border-t px-3 py-3">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                type="button"
                variant="ghost"
                asChild={false}
                className="h-auto w-full justify-start gap-2.5 rounded-lg px-1 py-1.5 text-left"
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
              </Button>
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
                <DropdownMenuItem onClick={() => goToSection('settings')}>
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
              <Select
                value=""
                onValueChange={(slug) => {
                  if (slug) navigate(`/app/b/${slug}`)
                }}
              >
                <SelectTrigger
                  aria-label="Switch boutique"
                  className="h-8 w-[13rem] gap-1.5 rounded-lg text-sm"
                >
                  <Store className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                  <SelectValue placeholder="Switch boutique…" />
                </SelectTrigger>
                <SelectContent>
                  {boutiques.map((b) => (
                    <SelectItem key={b.organizationId} value={b.slug ?? ''}>
                      {b.organizationName ?? b.slug}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>

          {/* Blossom balance + top up + notifications */}
          <div className="flex items-center gap-2.5">
            {usage ? (
              <span
                title={`${usage.blossomRemaining.toLocaleString()} of ${usage.monthlyBlossomLimit.toLocaleString()} Blossoms left`}
                className="inline-flex items-center gap-2 rounded-full bg-gradient-to-r from-primary to-primary/70 px-4 py-1.5 text-sm font-semibold text-white shadow-sm"
              >
                <Blossom className="size-4 text-white" />
                {usage.blossomRemaining.toLocaleString()} Blossoms
              </span>
            ) : (
              // "Demo mode" was a claim about the product, not a state of the data. A missing
              // balance means the API did not measure one, so the chip says exactly that.
              <span className="inline-flex items-center gap-2 rounded-full border border-border bg-muted px-4 py-1.5 text-sm font-medium text-muted-foreground">
                Balance unavailable
              </span>
            )}

            <Button
              variant="outline"
              size="sm"
              className="gap-1.5 rounded-full"
              onClick={() => {
                goToSection('billing')
                toast('Choose a Blossom top-up pack', {
                  description:
                    'Top-ups are recorded grants until a payment provider is connected.',
                })
              }}
            >
              <Plus className="size-4" aria-hidden />
              Top up
            </Button>

            <Select
              value={dashboardWindow.window}
              onValueChange={(value) => dashboardWindow.setWindow(value as typeof dashboardWindow.window)}
            >
              <SelectTrigger aria-label="Reporting window" className="h-8 w-[10.5rem] rounded-full text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DASHBOARD_WINDOWS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <NotificationBell />

            {/* Always-available Aveline chat launcher (header). */}
            <AvelineChatLauncher open={chatOpen} onOpen={() => setChatOpen(true)} />
          </div>
        </header>

        <main className="flex-1 px-6 py-8">
          {activeSection === 'overview' ? (
            <Overview
              organization={organization}
              usage={usage}
              role={role}
              window={dashboardWindow.window}
            />
          ) : activeSection === 'salon' ? (
            <SalonPanel />
          ) : activeSection === 'customers' ? (
            <CustomersPanel organization={organization} role={role} />
          ) : activeSection === 'income' ? (
            <IncomePanel organizationId={organization.id} organizationName={organization.name} />
          ) : activeSection === 'catalog' ? (
            <CatalogPanel
              organization={organization}
              role={role}
              openItemId={catalogItemId ?? null}
              onOpenItem={(item) => navigate(`/app/b/${organization.slug}/catalog/${item.id}`)}
              onCloseItem={() => navigate(`/app/b/${organization.slug}/catalog`)}
              onOpenSalonForCustomer={(_id, _name) => goToSection('salon')}
            />
          ) : activeSection === 'team' ? (
            <TeamManagement
              organization={organization}
              role={role}
              currentUserId={currentUserId ?? ''}
            />
          ) : activeSection === 'integrations' ? (
            <IntegrationsPanel organization={organization} />
          ) : activeSection === 'usage' ? (
            <UsagePanel
              organization={organization}
              role={role}
              window={dashboardWindow.window}
              onUpgrade={() => goToSection('upgrade')}
            />
          ) : activeSection === 'billing' ? (
            <BillingPanel
              organization={organization}
              role={role}
              onUpgrade={() => goToSection('upgrade')}
            />
          ) : activeSection === 'approvals' ? (
            <ApprovalsPanel organization={organization} role={role} />
          ) : activeSection === 'upgrade' ? (
            <UpgradePanel organization={organization} />
          ) : activeSection === 'settings' ? (
            <SettingsPanel organization={organization} role={role} />
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

      {/* Slide-in Aveline chat panel, rendered at the shell root so it spans full height. */}
      <AvelineChatDrawer open={chatOpen} onClose={() => setChatOpen(false)} />
    </ConversationsProvider>
  )
}
