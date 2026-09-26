import { lazy, Suspense, type ComponentType, type LazyExoticComponent } from 'react'
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
import { PageLoader } from './components/PageLoader'
// Not lazy: it is a redirect, and deferring it would turn one navigation into two round trips.
import { DashboardRedirect } from './routes/Dashboard'

/**
 * Route-level code splitting.
 *
 * Before this, every route component was a static import, so `/` — the anonymous marketing
 * landing page — downloaded and parsed the entire application: the admin console with Recharts,
 * the documentation engine with Mermaid and highlight.js, and the tenant dashboard. The entry
 * chunk was 2.7 MB raw / 756 KB gzip and nothing was split, because Vite only splits on dynamic
 * `import()`. Turning each route element into a dynamic import is the whole mechanism.
 *
 * The providers, the route guards, `AuthApiBridge` and `<Toaster>` deliberately stay **eager**:
 * they are mounted outside the route table in every session and lazy-loading them would trade a
 * real cost for a loading flash on the first paint.
 *
 * `lazyRoute` keeps the route table readable while preserving the named-export shape that these
 * modules use (`export function LandingPage()`, not a default export).
 */
function lazyRoute<TModule, TKey extends keyof TModule>(
  load: () => Promise<TModule>,
  exportName: TKey,
): LazyExoticComponent<ComponentType> {
  return lazy(async () => ({
    default: (await load())[exportName] as unknown as ComponentType,
  }))
}

// Public marketing + docs/pages
const LandingPage = lazyRoute(() => import('./routes/LandingPage'), 'LandingPage')
const PlansPage = lazyRoute(() => import('./routes/PlansPage'), 'PlansPage')
const ContactPage = lazyRoute(() => import('./routes/ContactPage'), 'ContactPage')
const DocsPage = lazyRoute(() => import('./routes/DocsPage'), 'DocsPage')
const DownloadPage = lazyRoute(() => import('./routes/DownloadPage'), 'DownloadPage')
const PrivacyPage = lazyRoute(() => import('./routes/PrivacyPage'), 'PrivacyPage')
const ConsentFlowPage = lazyRoute(() => import('./routes/ConsentFlowPage'), 'ConsentFlowPage')
const OptOutPage = lazyRoute(() => import('./routes/OptOutPage'), 'OptOutPage')

// Account lifecycle
const AdminPendingPage = lazyRoute(() => import('./routes/AdminPendingPage'), 'AdminPendingPage')
const OnboardingPage = lazyRoute(() => import('./routes/OnboardingPage'), 'OnboardingPage')
const OrgSetupPage = lazyRoute(() => import('./routes/OrgSetupPage'), 'OrgSetupPage')
const InvitePage = lazyRoute(() => import('./routes/InvitePage'), 'InvitePage')
const TenantDashboard = lazyRoute(() => import('./routes/TenantDashboard'), 'TenantDashboard')

// Administrator console (its own tree, and the reason Recharts used to ship to every visitor)
const AdminRootRedirect = lazyRoute(() => import('./routes/admin/AdminLayout'), 'AdminRootRedirect')
const AdminLayout = lazyRoute(() => import('./routes/admin/AdminLayout'), 'AdminLayout')
const AdminShell = lazyRoute(() => import('./components/admin/shell/AdminShell'), 'AdminShell')
const AdminDashboardView = lazyRoute(() => import('./routes/admin/AdminDashboard'), 'AdminDashboardView')
const AdminUsersView = lazyRoute(() => import('./routes/admin/AdminUsers'), 'AdminUsersView')
const AdminRequestsView = lazyRoute(() => import('./routes/admin/AdminRequests'), 'AdminRequestsView')
const AdminOrgsView = lazyRoute(() => import('./routes/admin/AdminOrgs'), 'AdminOrgsView')
const AdminBlossomsView = lazyRoute(() => import('./routes/admin/AdminBlossoms'), 'AdminBlossomsView')
const AdminRevenueView = lazyRoute(() => import('./routes/admin/AdminRevenue'), 'AdminRevenueView')
const AdminRevenueLedgerView = lazyRoute(
  () => import('./routes/admin/AdminRevenueLedger'),
  'AdminRevenueLedgerView',
)
const AdminRevenueStatsView = lazyRoute(
  () => import('./routes/admin/AdminRevenueStats'),
  'AdminRevenueStatsView',
)
const AdminPricingRulesView = lazyRoute(
  () => import('./routes/admin/AdminPricingRules'),
  'AdminPricingRulesView',
)
const AdminPriceBookView = lazyRoute(
  () => import('./routes/admin/AdminPriceBook'),
  'AdminPriceBookView',
)
const AdminLogsView = lazyRoute(() => import('./routes/admin/AdminLogs'), 'AdminLogsView')
const AdminAuditView = lazyRoute(() => import('./routes/admin/AdminAudit'), 'AdminAuditView')
const AdminSystemView = lazyRoute(() => import('./routes/admin/AdminSystem'), 'AdminSystemView')
const AdminRolesView = lazyRoute(() => import('./routes/admin/AdminRoles'), 'AdminRolesView')
const AdminStatisticsAgentsView = lazyRoute(
  () => import('./routes/admin/AdminStatisticsAgents'),
  'AdminStatisticsAgentsView',
)
const AdminStatisticsApiView = lazyRoute(
  () => import('./routes/admin/AdminStatisticsApi'),
  'AdminStatisticsApiView',
)
const AdminBusinessGrowthView = lazyRoute(
  () => import('./routes/admin/AdminBusinessGrowth'),
  'AdminBusinessGrowthView',
)
const AdminBusinessUsageView = lazyRoute(
  () => import('./routes/admin/AdminBusinessUsage'),
  'AdminBusinessUsageView',
)

// Shells and one-off pages
const RootLayout = lazyRoute(() => import('./routes/RootLayout'), 'RootLayout')
const ForbiddenPage = lazyRoute(() => import('./routes/ForbiddenPage'), 'ForbiddenPage')
const SuspendedPage = lazyRoute(() => import('./routes/SuspendedPage'), 'SuspendedPage')
const SignInPage = lazyRoute(() => import('./routes/SignInPage'), 'SignInPage')
const SignUpPage = lazyRoute(() => import('./routes/SignUpPage'), 'SignUpPage')
const AdminSignUpPage = lazyRoute(() => import('./routes/AdminSignUpPage'), 'AdminSignUpPage')
const TermsPage = lazyRoute(() => import('./routes/TermsPage'), 'TermsPage')

export default function App() {
  return (
    <>
      <AuthApiBridge />
      <UserProvider>
        <NotificationsProvider>
          {/* One boundary for the whole route table. `PageLoader` is the existing full-screen
              skeleton, so a slow connection sees the app's own loading state rather than a blank
              document. */}
          <Suspense fallback={<PageLoader />}>
            <Routes>
              {/* Public marketing + docs/pages */}
              <Route path="/" element={<LandingPage />} />
              <Route path="/plans" element={<PlansPage />} />
              <Route path="/contact" element={<ContactPage />} />
              <Route path="/docs" element={<Navigate to="/docs/getting-started" replace />} />
              <Route path="/docs/:slug" element={<DocsPage />} />
              <Route path="/download" element={<DownloadPage />} />
              {/* The transparency surface the WhatsApp disclosure links to. Public and anonymous by
                  design: the opt-out page authenticates with a one-time code, not an account. */}
              <Route path="/privacy" element={<PrivacyPage />} />
              <Route path="/privacy/consent-flow" element={<ConsentFlowPage />} />
              <Route path="/privacy/opt-out" element={<OptOutPage />} />

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
                  {/* The section is part of the URL so a dashboard section is linkable and survives
                      a refresh (Q6). The bare slug route redirects to `/overview`. */}
                  <Route
                    path="/app/b/:slug"
                    element={<Navigate to="overview" replace />}
                  />
                  {/* One catalogue piece has its own URL, so its information page is linkable and
                      survives a refresh. React Router ranks the literal `catalog` above `:section`, so
                      this route wins for `/app/b/:slug/catalog/:itemId`. */}
                  <Route path="/app/b/:slug/catalog/:itemId" element={<TenantDashboard />} />
                  <Route path="/app/b/:slug/:section" element={<TenantDashboard />} />

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
                      <Route path="revenue" element={<AdminRevenueView />} />
                      <Route path="revenue/ledger" element={<AdminRevenueLedgerView />} />
                      <Route path="revenue/statistics" element={<AdminRevenueStatsView />} />
                      <Route path="pricing" element={<AdminPricingRulesView />} />
                      <Route path="pricing/price-book" element={<AdminPriceBookView />} />
                      <Route path="logs" element={<AdminLogsView />} />
                      <Route path="audit" element={<AdminAuditView />} />
                      <Route path="system" element={<AdminSystemView />} />
                      <Route path="roles" element={<AdminRolesView />} />
                      <Route path="statistics/agents" element={<AdminStatisticsAgentsView />} />
                      <Route path="statistics/api" element={<AdminStatisticsApiView />} />
                      <Route path="business" element={<AdminBusinessGrowthView />} />
                      <Route path="business/usage" element={<AdminBusinessUsageView />} />
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
          </Suspense>

          <Toaster />
        </NotificationsProvider>
      </UserProvider>
    </>
  )
}
