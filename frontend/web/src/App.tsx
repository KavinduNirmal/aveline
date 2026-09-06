import { AuthenticateWithRedirectCallback } from '@clerk/react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from './components/ProtectedRoute'
import { RequireAccountState } from './components/RequireAccountState'
import { RequireAdmin } from './components/RequireAdmin'
import { UserProvider } from './contexts/UserContext'
import { AuthApiBridge } from './lib/AuthApiBridge'
import { AdminSignUpPage } from './routes/AdminSignUpPage'
import { ContactPage } from './routes/ContactPage'
import { Dashboard } from './routes/Dashboard'
import { DocsPage } from './routes/DocsPage'
import { DownloadPage } from './routes/DownloadPage'
import { ForbiddenPage } from './routes/ForbiddenPage'
import { LandingPage } from './routes/LandingPage'
import { OnboardingPage } from './routes/OnboardingPage'
import { OrgSetupPage } from './routes/OrgSetupPage'
import { PlansPage } from './routes/PlansPage'
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
          {/* Public marketing + docs/pages */}
          <Route path="/" element={<LandingPage />} />
          <Route path="/plans" element={<PlansPage />} />
          <Route path="/contact" element={<ContactPage />} />
          <Route path="/docs" element={<Navigate to="/docs/getting-started" replace />} />
          <Route path="/docs/:slug" element={<DocsPage />} />
          <Route path="/download" element={<DownloadPage />} />

          <Route element={<ProtectedRoute />}>
            <Route path="/onboarding" element={<OnboardingPage />} />
            <Route path="/org-setup" element={<OrgSetupPage />} />
            <Route element={<RequireAccountState />}>
              <Route element={<RootLayout />}>
                <Route element={<RequireAdmin />}>
                  <Route path="/app" element={<Dashboard />} />
                </Route>
                <Route path="/forbidden" element={<ForbiddenPage />} />
              </Route>
            </Route>
          </Route>

          <Route path="/sso-callback" element={<AuthenticateWithRedirectCallback signInFallbackRedirectUrl="/app" signUpFallbackRedirectUrl="/app" />} />
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
