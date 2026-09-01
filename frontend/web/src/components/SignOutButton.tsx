import { useClerk } from '@clerk/react'
import { useNavigate } from 'react-router-dom'

/** Signs the user out, then navigates to the sign-in page. */
export function SignOutButton() {
  const { signOut } = useClerk()
  const navigate = useNavigate()

  return (
    <button
      type="button"
      className="sign-out-button"
      onClick={() => signOut(() => navigate('/sign-in'))}
    >
      Sign out
    </button>
  )
}
