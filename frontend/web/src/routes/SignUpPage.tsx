import { SignUp } from '@clerk/react'

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

/** Clerk prebuilt sign-up page. */
export function SignUpPage() {
  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <Card className="w-full max-w-md shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader className="text-center">
          <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
            Aveline
          </p>
          <CardTitle className="font-serif text-2xl font-medium">
            Create an account
          </CardTitle>
          <CardDescription>Set up your owner dashboard</CardDescription>
        </CardHeader>
        <CardContent>
          <SignUp fallbackRedirectUrl="/" />
        </CardContent>
      </Card>
    </div>
  )
}
