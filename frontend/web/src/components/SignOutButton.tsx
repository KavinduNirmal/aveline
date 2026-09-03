import { useClerk } from '@clerk/react'
import { LogOut } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'

/** Signs the user out, then navigates to the sign-in page. */
export function SignOutButton() {
  const { signOut } = useClerk()
  const navigate = useNavigate()

  return (
    <Button
      variant="outline"
      size="sm"
      onClick={() => signOut(() => navigate('/sign-in'))}
    >
      <LogOut className="size-4" aria-hidden />
      Sign out
    </Button>
  )
}
