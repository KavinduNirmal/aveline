import { SignUpForm } from '@/components/auth/SignUpForm'
import { AuthShell } from '@/components/auth/AuthShell'

/** Custom Clerk sign-up page with the flower/aurora brand treatment. */
export function SignUpPage() {
  return (
    <AuthShell mode="signup">
      <SignUpForm />
    </AuthShell>
  )
}
