import { UserPlus } from 'lucide-react'
import { useCallback, useState } from 'react'

import { Button } from '@/components/ui/button'
import { MembersTab } from '@/components/dashboard/team/MembersTab'
import { InvitationDrawer, type InvitationDrawerView } from '@/components/dashboard/team/InvitationDrawer'
import type { OrganizationProfileDto } from '@/types/organization'

interface TeamManagementProps {
  organization: OrganizationProfileDto
  role: string
  /** The caller's Aveline user id, so their own row can be disabled with a stated reason. */
  currentUserId: string
}

/**
 * The Team section: **who is on the team**.
 *
 * The member list is the page. Code generation lives in a side drawer opened from the header,
 * because minting an onboarding code is an occasional task rather than a standing view - keeping
 * the generator, its batch controls and its own metric row here pushed the roster below the fold
 * and made a page about people read as a page about codes.
 *
 * The page keeps exactly one number from the drawer, the pending-code count on its button, so an
 * owner can see at a glance that codes are outstanding without opening anything.
 */
export function TeamManagement({ organization, currentUserId }: TeamManagementProps) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [drawerView, setDrawerView] = useState<InvitationDrawerView>('generate')
  const [pendingCount, setPendingCount] = useState(0)

  const handlePendingCountChange = useCallback((count: number) => setPendingCount(count), [])

  const openDrawer = (view: InvitationDrawerView) => {
    setDrawerView(view)
    setDrawerOpen(true)
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            {organization.name}
          </p>
          <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">Team</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Who works in this boutique and what each person may do. Onboarding codes are generated
            from the drawer, and a role change takes effect on the member&apos;s next request.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            onClick={() => openDrawer('pending')}
            className="gap-1.5"
          >
            Pending codes{pendingCount > 0 ? ` (${pendingCount})` : ''}
          </Button>
          <Button type="button" onClick={() => openDrawer('generate')} className="gap-1.5">
            <UserPlus className="size-4" aria-hidden />
            Invite staff
          </Button>
        </div>
      </div>

      {/* The member list owns the section's only card: `MembersTab` renders its own header and
          content, so wrapping it in another card would nest two containers with the same title. */}
      <MembersTab organizationId={organization.id} currentUserId={currentUserId} />

      <InvitationDrawer
        // Remounts when the opener asks for the other half, so the drawer needs no view state.
        key={`${drawerOpen ? 'open' : 'closed'}-${drawerView}`}
        open={drawerOpen}
        onOpenChange={setDrawerOpen}
        organization={organization}
        view={drawerView}
        onViewChange={setDrawerView}
        onPendingCountChange={handlePendingCountChange}
      />
    </div>
  )
}
