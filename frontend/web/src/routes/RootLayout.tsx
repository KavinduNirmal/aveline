import { useUser } from '@clerk/react'
import { Outlet } from 'react-router-dom'

import { SignOutButton } from '@/components/SignOutButton'

/** Signed-in app shell: header with user identity and sign-out, plus routed content. */
export function RootLayout() {
  const { isLoaded, isSignedIn, user } = useUser()

  return (
    <div className="flex min-h-screen flex-col">
      <header className="sticky top-0 z-10 border-b bg-background/80 backdrop-blur-sm">
        <div className="mx-auto flex h-16 w-full max-w-5xl items-center justify-between px-6">
          <span className="font-serif text-xl font-medium tracking-tight text-primary">
            Aveline
          </span>
          {isLoaded && isSignedIn && user ? (
            <div className="flex items-center gap-4">
              <span className="text-sm text-muted-foreground">
                {user.primaryEmailAddress?.emailAddress ?? user.fullName}
              </span>
              <SignOutButton />
            </div>
          ) : null}
        </div>
      </header>
      <main className="mx-auto w-full max-w-5xl flex-1 px-6 py-8">
        <Outlet />
      </main>
    </div>
  )
}
