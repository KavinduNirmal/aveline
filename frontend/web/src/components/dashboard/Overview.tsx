import { useUser } from '@clerk/react'
import { CheckCircle2 } from 'lucide-react'

import { Blossom } from '@/components/auth/Blossom'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { hasPermission } from '@/lib/permissions'
import type {
  OrganizationProfileDto,
  OrganizationUsageSummary,
} from '@/types/organization'

interface OverviewProps {
  organization: OrganizationProfileDto
  usage: OrganizationUsageSummary | null
  role: string
}

/**
 * Default landing section of the tenant dashboard: boutique identity, plan, and
 * Blossom balance, plus a placeholder for the (not-yet-built) approvals queue.
 */
export function Overview({ organization, usage, role }: OverviewProps) {
  const { user } = useUser()
  const canApprove = hasPermission(role, 'approvals:approve')

  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          {organization.name}
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">
          Good day, {user?.firstName ?? 'there'}
        </h1>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Badge className="capitalize">{organization.planTier} plan</Badge>
        {organization.logoUrl ? (
          <Badge variant="outline" className="gap-1">
            <CheckCircle2 className="size-3" aria-hidden /> Boutique active
          </Badge>
        ) : null}
      </div>

      {organization.description ? (
        <p className="max-w-2xl text-sm text-muted-foreground">{organization.description}</p>
      ) : null}

      <Separator />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
          <CardHeader>
            <div className="flex items-center justify-between gap-4">
              <CardTitle className="font-serif text-lg font-medium">Blossoms</CardTitle>
              <Badge variant="outline" className="gap-1">
                <Blossom className="size-3 text-primary" /> {organization.planTier}
              </Badge>
            </div>
            <CardDescription>Remaining this billing period.</CardDescription>
          </CardHeader>
          <CardContent>
            {usage ? (
              <div className="flex items-baseline gap-2">
                <span className="font-serif text-4xl font-medium">
                  {usage.blossomRemaining.toLocaleString()}
                </span>
                <span className="text-sm text-muted-foreground">
                  of {usage.monthlyBlossomLimit.toLocaleString()}
                </span>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">Usage data unavailable.</p>
            )}
          </CardContent>
        </Card>

        {canApprove && (
          <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
            <CardHeader>
              <CardTitle className="font-serif text-lg font-medium">Approvals</CardTitle>
              <CardDescription>
                Pending order approvals land here once the commerce slice is built out.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-muted-foreground">No pending approvals.</p>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  )
}
