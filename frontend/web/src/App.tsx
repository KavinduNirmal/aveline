import { AuthenticateWithRedirectCallback } from '@clerk/react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from './components/ProtectedRoute'
import { RequireAccountState } from './components/RequireAccountState'
import { RequireAdmin } from './components/RequireAdmin'
import { UserProvider } from './contexts/UserContext'
import { AuthApiBridge } from './lib/AuthApiBridge'
import { AdminSignUpPage } from './routes/AdminSignUpPage'
import { Dashboard } from './routes/Dashboard'
import { ForbiddenPage } from './routes/ForbiddenPage'
import { OnboardingPage } from './routes/OnboardingPage'
import { OrgSetupPage } from './routes/OrgSetupPage'
import { RootLayout } from './routes/RootLayout'
import { SignInPage } from './routes/SignInPage'
import { SignUpPage } from './routes/SignUpPage'
import { SuspendedPage } from './routes/SuspendedPage'
import { TermsPage } from './routes/TermsPage'

export default function App() {
  return (
    <>
      <AuthApiBridge />
      <UserProvider>
        <Routes>
          <Route element={<ProtectedRoute />}>
            <Route path="/onboarding" element={<OnboardingPage />} />
            <Route path="/org-setup" element={<OrgSetupPage />} />
            <Route element={<RequireAccountState />}>
              <Route element={<RootLayout />}>
                <Route element={<RequireAdmin />}>
                  <Route path="/" element={<Dashboard />} />
                </Route>
                <Route path="/forbidden" element={<ForbiddenPage />} />
              </Route>
            </Route>
          </Route>

          <Route path="/sso-callback" element={<AuthenticateWithRedirectCallback signInFallbackRedirectUrl="/" signUpFallbackRedirectUrl="/" />} />
          <Route path="/suspended" element={<SuspendedPage />} />
          <Route path="/sign-in/*" element={<SignInPage />} />
          <Route path="/sign-up/*" element={<SignUpPage />} />
          <Route path="/sign-up/admin" element={<AdminSignUpPage />} />
          <Route path="/terms" element={<TermsPage />} />

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </UserProvider>
    </>
  )
}
