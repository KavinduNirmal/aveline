import { Link } from 'react-router-dom'

import { SignInForm } from '@/components/auth/SignInForm'
import { AuthSplitLayout } from '@/components/auth/AuthSplitLayout'

/** Custom Clerk sign-in page — split layout with brand panel. */
export function SignInPage() {
  return (
    <AuthSplitLayout
      mode="signin"
      footer={
        <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-[13px]">
          <Link
            to="/sign-up"
            className="text-white/50 transition-colors hover:text-white/90"
          >
            New to Aveline?{' '}
            <span className="font-medium text-rose-200/90">Create an account</span>
          </Link>
          <Link
            to="/sign-up/admin"
            className="text-white/35 transition-colors hover:text-white/70"
          >
            Administrator sign-up
          </Link>
        </div>
      }
    >
      <SignInForm />
    </AuthSplitLayout>
  )
}
