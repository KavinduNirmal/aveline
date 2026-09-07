import { Link } from 'react-router-dom'

import { SignInForm } from '@/components/auth/SignInForm'
import { AuthSplitLayout } from '@/components/auth/AuthSplitLayout'

/** Custom Clerk sign-in page — split layout with brand panel. */
export function SignInPage() {
  return (
    <AuthSplitLayout
      mode="signin"
      footer={
        <div className="flex flex-col gap-1.5">
          <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-[13px]">
            <Link
              to="/sign-up"
              className="text-white/55 transition-colors hover:text-white/90"
            >
              New to Aveline?{' '}
              <span className="font-medium text-rose-200/90">Create an account</span>
            </Link>
            <span className="flex items-center gap-2.5 text-white/40">
              <Link
                to="/sign-up/admin"
                className="transition-colors hover:text-white"
              >
                Admin sign-up
              </Link>
              <span className="text-white/20">·</span>
              <Link to="/terms" className="transition-colors hover:text-white">
                Terms
              </Link>
              <span className="text-white/20">·</span>
              <Link to="/terms#privacy" className="transition-colors hover:text-white">
                Privacy
              </Link>
            </span>
          </div>
          <p className="text-[11px] text-white/30">
            © 2026 Aveline · contact@aveline.lk · Colombo, Sri Lanka
          </p>
        </div>
      }
    >
      <SignInForm />
    </AuthSplitLayout>
  )
}
