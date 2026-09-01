import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

import { SignOutButton } from '../components/SignOutButton'

/** 403 — signed in, but the account lacks owner/manager access. */
export function ForbiddenPage() {
  return (
    <div className="flex min-h-full items-center justify-center px-4 py-8">
      <Card className="w-full max-w-md text-center shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            Aveline
          </p>
          <CardTitle className="font-serif text-2xl font-medium">
            No access
          </CardTitle>
          <CardDescription>
            This account doesn't have owner or manager permissions for the
            dashboard.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex justify-center">
          <SignOutButton />
        </CardContent>
      </Card>
    </div>
  )
}
