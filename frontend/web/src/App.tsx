import { AuthenticateWithRedirectCallback } from '@clerk/react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from './components/ProtectedRoute'
import { RequireAdmin } from './components/RequireAdmin'
import { RequireOnboarding } from './components/RequireOnboarding'
import { UserProvider } from './contexts/UserContext'
import { AuthApiBridge } from './lib/AuthApiBridge'
import { AdminSignUpPage } from './routes/AdminSignUpPage'
import { Dashboard } from './routes/Dashboard'
import { ForbiddenPage } from './routes/ForbiddenPage'
import { OnboardingPage } from './routes/OnboardingPage'
import { RootLayout } from './routes/RootLayout'
import { SignInPage } from './routes/SignInPage'
import { SignUpPage } from './routes/SignUpPage'

export default function App() {
  return (
    <>
      <AuthApiBridge />
      <UserProvider>
        <Routes>
          <Route element={<ProtectedRoute />}>
            <Route path="/onboarding" element={<OnboardingPage />} />
            <Route element={<RequireOnboarding />}>
              <Route element={<RootLayout />}>
                <Route element={<RequireAdmin />}>
                  <Route path="/" element={<Dashboard />} />
                </Route>
                <Route path="/forbidden" element={<ForbiddenPage />} />
              </Route>
            </Route>
          </Route>

          <Route path="/sso-callback" element={<AuthenticateWithRedirectCallback signInFallbackRedirectUrl="/" signUpFallbackRedirectUrl="/" />} />
          <Route path="/sign-in/*" element={<SignInPage />} />
          <Route path="/sign-up/*" element={<SignUpPage />} />
          <Route path="/sign-up/admin" element={<AdminSignUpPage />} />

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </UserProvider>
    </>
  )
}
