import { SignUp } from '@clerk/react'

/** Clerk prebuilt sign-up page. */
export function SignUpPage() {
  return (
    <div className="auth-page">
      <SignUp fallbackRedirectUrl="/" />
    </div>
  )
}
