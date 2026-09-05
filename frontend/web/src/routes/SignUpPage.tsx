import { useState } from 'react'
import { Building2, type LucideIcon, Users } from 'lucide-react'
import { Link } from 'react-router-dom'

import { SignUpForm } from '@/components/auth/SignUpForm'
import { AuthSplitLayout } from '@/components/auth/AuthSplitLayout'
import { cn } from '@/lib/utils'

type AccountType = 'owner' | 'staff'

const ACCOUNT_TYPES: {
  value: AccountType
  icon: LucideIcon
  title: string
  blurb: string
}[] = [
  {
    value: 'owner',
    icon: Building2,
    title: 'Boutique owner',
    blurb: 'I run a boutique and want to open it on Aveline.',
  },
  {
    value: 'staff',
    icon: Users,
    title: 'Staff member',
    blurb: 'My owner invited me — I’ll join with an invitation code.',
  },
]

function AccountTypeStep({ onSelect }: { onSelect: (v: AccountType) => void }) {
  return (
    <div className="flex flex-col gap-2.5">
      {ACCOUNT_TYPES.map(({ value, icon: Icon, title, blurb }) => (
        <button
          key={value}
          type="button"
          onClick={() => onSelect(value)}
          className={cn(
            'group flex w-full items-center gap-3 rounded-2xl border-2 border-dashed border-white/20 bg-white/[0.04] p-3.5 text-left transition-colors',
            'hover:border-rose-200/50 hover:bg-white/[0.07]',
          )}
        >
          <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[#7a303f]/40 text-rose-100">
            <Icon className="size-5" aria-hidden />
          </span>
          <span className="flex min-w-0 flex-col">
            <span className="text-[15px] font-semibold text-white">{title}</span>
            <span className="text-[13px] leading-snug text-white/50">{blurb}</span>
          </span>
        </button>
      ))}
      <p className="pt-1 text-center text-xs leading-relaxed text-white/35">
        Administrator access is provisioned by the Aveline team.
      </p>
    </div>
  )
}

function SignUpFooter({
  accepted,
  onChangeAccepted,
}: {
  accepted: boolean
  onChangeAccepted: (v: boolean) => void
}) {
  return (
    <div className="flex flex-col gap-3">
      <label className="flex cursor-pointer items-start gap-3 text-[13px] leading-snug text-white/60">
        <input
          type="checkbox"
          checked={accepted}
          onChange={(e) => onChangeAccepted(e.target.checked)}
          className="mt-0.5 size-4 shrink-0 accent-[#ffb2bc]"
        />
        <span>
          I agree to the{' '}
          <Link to="/terms" className="font-medium text-rose-200/90 hover:underline">
            Terms &amp; Conditions
          </Link>{' '}
          and acknowledge the{' '}
          <Link to="/terms#privacy" className="font-medium text-rose-200/90 hover:underline">
            Privacy Policy
          </Link>
          .
        </span>
      </label>
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-[13px]">
        <Link
          to="/sign-in"
          className="text-white/50 transition-colors hover:text-white/90"
        >
          Already have an account?{' '}
          <span className="font-medium text-rose-200/90">Sign in</span>
        </Link>
        <Link
          to="/sign-up/admin"
          className="text-white/35 transition-colors hover:text-white/70"
        >
          Administrator sign-up
        </Link>
      </div>
    </div>
  )
}

/** Custom Clerk sign-up page — split layout, account type step, terms gate. */
export function SignUpPage() {
  const [accountType, setAccountType] = useState<AccountType | null>(null)
  const [termsAccepted, setTermsAccepted] = useState(false)

  return (
    <AuthSplitLayout
      mode="signup"
      footer={
        <SignUpFooter
          accepted={termsAccepted}
          onChangeAccepted={setTermsAccepted}
        />
      }
    >
      {accountType === null ? (
        <AccountTypeStep onSelect={setAccountType} />
      ) : (
        <SignUpForm
          accountType={accountType}
          onResetAccountType={() => setAccountType(null)}
          canSubmit={termsAccepted}
        />
      )}
    </AuthSplitLayout>
  )
}
