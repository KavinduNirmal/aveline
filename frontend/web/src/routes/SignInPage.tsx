import { SignInForm } from '@/components/auth/SignInForm'
import { AuthShell } from '@/components/auth/AuthShell'

/** Custom Clerk sign-in page with the flower/aurora brand treatment. */
export function SignInPage() {
  return (
    <AuthShell mode="signin">
      <SignInForm />
    </AuthShell>
  )
}
