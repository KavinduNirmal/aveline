import { SignIn } from '@clerk/react'

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

/** Clerk prebuilt sign-in page. */
export function SignInPage() {
  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <Card className="w-full max-w-md shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader className="text-center">
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            Aveline
          </p>
          <CardTitle className="font-serif text-2xl font-medium">
            Welcome back
          </CardTitle>
          <CardDescription>Sign in to the owner dashboard</CardDescription>
        </CardHeader>
        <CardContent>
          <SignIn fallbackRedirectUrl="/" />
        </CardContent>
      </Card>
    </div>
  )
}
