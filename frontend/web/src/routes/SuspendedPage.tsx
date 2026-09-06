import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

import { SignOutButton } from '../components/SignOutButton'

/** Account is suspended — the API rejects all authenticated access. */
export function SuspendedPage() {
  return (
    <div className="flex min-h-full items-center justify-center px-4 py-8">
      <Card className="w-full max-w-md text-center shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader>
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            Aveline
          </p>
          <CardTitle className="font-serif text-2xl font-medium">
            Account suspended
          </CardTitle>
          <CardDescription>
            This account has been suspended. Contact support to restore access.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex justify-center">
          <SignOutButton />
        </CardContent>
      </Card>
    </div>
  )
}
