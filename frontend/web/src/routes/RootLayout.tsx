import { useUser } from '@clerk/react'
import { Outlet } from 'react-router-dom'

import { SignOutButton } from '../components/SignOutButton'

/** Signed-in app shell: header with user identity and sign-out, plus routed content. */
export function RootLayout() {
  const { isLoaded, isSignedIn, user } = useUser()

  return (
    <div className="app-shell">
      <header className="app-header">
        <span className="brand">Aveline Admin</span>
        <div className="header-actions">
          {isLoaded && isSignedIn && user ? (
            <>
              <span className="user-name">
                {user.primaryEmailAddress?.emailAddress ?? user.fullName}
              </span>
              <SignOutButton />
            </>
          ) : null}
        </div>
      </header>
      <main className="app-content">
        <Outlet />
      </main>
    </div>
  )
}
