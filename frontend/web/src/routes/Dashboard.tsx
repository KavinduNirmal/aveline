import { useUser } from '@clerk/react'

import { Badge } from '@/components/ui/badge'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'

/** Post-auth landing page for owners and managers. */
export function Dashboard() {
  const { user } = useUser()

  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          Owner overview
        </p>
        <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">
          Good day, {user?.firstName ?? user?.id}
        </h1>
      </div>

      <Separator />

      <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <CardTitle className="font-serif text-2xl font-medium">
              Approvals
            </CardTitle>
            <Badge variant="outline">Commerce</Badge>
          </div>
          <CardDescription>
            Pending order approvals land here once the commerce slice is built
            out.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">No pending approvals.</p>
        </CardContent>
      </Card>
    </div>
  )
}
