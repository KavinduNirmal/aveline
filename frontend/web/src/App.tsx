import { AuthenticateWithRedirectCallback } from '@clerk/react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from './components/ProtectedRoute'
import { RedirectIfAuthenticated } from './components/RedirectIfAuthenticated'
import { RedirectAdminSignUps } from './components/RedirectAdminSignUps'
import { RequireAccountState } from './components/RequireAccountState'
import { NotificationsProvider } from './contexts/NotificationsContext'
import { UserProvider } from './contexts/UserContext'
import { AuthApiBridge } from './lib/AuthApiBridge'
import { Toaster } from './components/ui/sonner'
import { AdminSignUpPage } from './routes/AdminSignUpPage'
import { AdminPendingPage } from './routes/AdminPendingPage'
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

import { AdminLayout, AdminRootRedirect } from './routes/admin/AdminLayout'
import { AdminShell } from './components/admin/shell/AdminShell'
import { AdminDashboardView } from './routes/admin/AdminDashboard'
import { AdminUsersView } from './routes/admin/AdminUsers'
import { AdminOrgsView } from './routes/admin/AdminOrgs'
import { AdminRequestsView } from './routes/admin/AdminRequests'
import { AdminBlossomsView } from './routes/admin/AdminBlossoms'
import { AdminPricingView } from './routes/admin/AdminPricing'
import { AdminLogsView } from './routes/admin/AdminLogs'
import { AdminAuditView } from './routes/admin/AdminAudit'
import { AdminSystemView } from './routes/admin/AdminSystem'
import { AdminRolesView } from './routes/admin/AdminRoles'

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
            {/* Administrator sign-ups wait here for review instead of onboarding. */}
            <Route path="/admin/pending" element={<AdminPendingPage />} />
            <Route element={<RedirectAdminSignUps />}>
              <Route path="/onboarding" element={<OnboardingPage />} />
              <Route path="/org-setup" element={<OrgSetupPage />} />
            </Route>
            <Route path="/invite" element={<InvitePage />} />
            <Route element={<RequireAccountState />}>
              <Route path="/app" element={<DashboardRedirect />} />
              <Route path="/app/b/:slug" element={<TenantDashboard />} />

              {/* Administrator console. Nested inside the same guards as the tenant app, so an
                  unauthenticated visitor cannot reach it and an account mid-lifecycle is handled
                  by the state gate rather than by the console itself (A2). */}
              <Route path="/admin" element={<AdminRootRedirect />} />
              <Route path="/admin/:userId" element={<AdminLayout />}>
                <Route element={<AdminShell />}>
                  <Route index element={<Navigate to="dashboard" replace />} />
                  <Route path="dashboard" element={<AdminDashboardView />} />
                  <Route path="users" element={<AdminUsersView />} />
                  <Route path="requests" element={<AdminRequestsView />} />
                  <Route path="orgs" element={<AdminOrgsView />} />
                  <Route path="blossoms" element={<AdminBlossomsView />} />
                  <Route path="pricing" element={<AdminPricingView />} />
                  <Route path="logs" element={<AdminLogsView />} />
                  <Route path="audit" element={<AdminAuditView />} />
                  <Route path="system" element={<AdminSystemView />} />
                  <Route path="roles" element={<AdminRolesView />} />
                  <Route path="*" element={<Navigate to="dashboard" replace />} />
                </Route>
              </Route>
            </Route>
          </Route>

          <Route element={<RootLayout />}>
            <Route path="/forbidden" element={<ForbiddenPage />} />
          </Route>

          <Route path="/sso-callback" element={<AuthenticateWithRedirectCallback signInFallbackRedirectUrl="/app" signUpFallbackRedirectUrl="/app" />} />
          <Route path="/suspended" element={<SuspendedPage />} />
          <Route element={<RedirectIfAuthenticated />}>
            <Route path="/sign-in/*" element={<SignInPage />} />
            <Route path="/sign-up/*" element={<SignUpPage />} />
          </Route>
          {/*
            Administrator sign-up is intentionally NOT wrapped in
            RedirectIfAuthenticated: finalizing the Clerk sign-up activates the
            session, and the redirect would unmount this page before the access
            request is submitted.
          */}
          <Route path="/sign-up/admin" element={<AdminSignUpPage />} />
          <Route path="/terms" element={<TermsPage />} />

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>

        <Toaster />
        </NotificationsProvider>
      </UserProvider>
    </>
  )
}
