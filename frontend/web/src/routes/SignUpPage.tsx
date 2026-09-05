import { useState } from 'react'
import { Building2, type LucideIcon, Users } from 'lucide-react'

import { SignUpForm } from '@/components/auth/SignUpForm'
import { AuthShell } from '@/components/auth/AuthShell'
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
      <p className="pb-1 text-center text-xs uppercase tracking-[0.22em] text-white/40">
        Choose your account type
      </p>
      {ACCOUNT_TYPES.map(({ value, icon: Icon, title, blurb }) => (
        <button
          key={value}
          type="button"
          onClick={() => onSelect(value)}
          className={cn(
            'group flex w-full items-center gap-3 rounded-2xl border border-white/10 bg-white/5 p-3.5 text-left transition-all',
            'hover:border-rose-200/40 hover:bg-white/10',
          )}
        >
          <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-[#7a303f]/30 text-rose-100 transition-colors group-hover:bg-[#7a303f]/50">
            <Icon className="size-5" aria-hidden />
          </span>
          <span className="flex min-w-0 flex-col">
            <span className="text-[15px] font-semibold text-white">{title}</span>
            <span className="text-[13px] leading-snug text-white/50">{blurb}</span>
          </span>
        </button>
      ))}
      <p className="pt-2 text-center text-xs leading-relaxed text-white/40">
        Owners and staff create accounts here. Administrator access is
        provisioned by the Aveline team.
      </p>
    </div>
  )
}

/** Custom Clerk sign-up page with the flower/aurora brand treatment. */
export function SignUpPage() {
  const [accountType, setAccountType] = useState<AccountType | null>(null)

  return (
    <AuthShell mode="signup">
      {accountType === null ? (
        <AccountTypeStep onSelect={setAccountType} />
      ) : (
        <SignUpForm
          accountType={accountType}
          onResetAccountType={() => setAccountType(null)}
        />
      )}
    </AuthShell>
  )
}
