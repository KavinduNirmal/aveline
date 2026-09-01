import { SignIn } from '@clerk/react'

/** Clerk prebuilt sign-in page. */
export function SignInPage() {
  return (
    <div className="auth-page">
      <SignIn fallbackRedirectUrl="/" />
    </div>
  )
}
