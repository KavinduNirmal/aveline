import { AuthenticateWithRedirectCallback } from '@clerk/react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from './components/ProtectedRoute'
import { RedirectIfAuthenticated } from './components/RedirectIfAuthenticated'
import { RequireAccountState } from './components/RequireAccountState'
import { NotificationsProvider } from './contexts/NotificationsContext'
import { UserProvider } from './contexts/UserContext'
import { AuthApiBridge } from './lib/AuthApiBridge'
import { Toaster } from './components/ui/sonner'
import { AdminSignUpPage } from './routes/AdminSignUpPage'
import { ContactPage } from './routes/ContactPage'
import { DashboardRedirect } from './routes/Dashboard'
import { DocsPage } from './routes/DocsPage'
import { DownloadPage } from './routes/DownloadPage'
import { ForbiddenPage } from './routes/ForbiddenPage'
import { InvitePage } from './routes/InvitePage'
import { LandingPage } from './routes/LandingPage'
import { OnboardingPage } from './routes/OnboardingPage'
import { OrgSetupPage } from './routes/OrgSetupPage'
import { PlansPage } from './routes/PlansPage'
import { RootLayout } from './routes/RootLayout'
import { SignInPage } from './routes/SignInPage'
import { SignUpPage } from './routes/SignUpPage'
import { SuspendedPage } from './routes/SuspendedPage'
import { TenantDashboard } from './routes/TenantDashboard'
import { TermsPage } from './routes/TermsPage'

export default function App() {
  return (
    <>
      <AuthApiBridge />
      <UserProvider>
        <NotificationsProvider>
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
            <Route path="/invite" element={<InvitePage />} />
            <Route element={<RequireAccountState />}>
              <Route path="/app" element={<DashboardRedirect />} />
              <Route path="/app/b/:slug" element={<TenantDashboard />} />
              <Route element={<RootLayout />}>
                <Route path="/forbidden" element={<ForbiddenPage />} />
              </Route>
            </Route>
          </Route>

          <Route path="/sso-callback" element={<AuthenticateWithRedirectCallback signInFallbackRedirectUrl="/app" signUpFallbackRedirectUrl="/app" />} />
          <Route path="/suspended" element={<SuspendedPage />} />
          <Route element={<RedirectIfAuthenticated />}>
            <Route path="/sign-in/*" element={<SignInPage />} />
            <Route path="/sign-up/*" element={<SignUpPage />} />
            <Route path="/sign-up/admin" element={<AdminSignUpPage />} />
          </Route>
          <Route path="/terms" element={<TermsPage />} />

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>

        <Toaster />
        </NotificationsProvider>
      </UserProvider>
    </>
  )
}
