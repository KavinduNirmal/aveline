import { Navigate, Route, Routes } from 'react-router-dom'

import { ProtectedRoute } from './components/ProtectedRoute'
import { RequireAdmin } from './components/RequireAdmin'
import { Dashboard } from './routes/Dashboard'
import { ForbiddenPage } from './routes/ForbiddenPage'
import { RootLayout } from './routes/RootLayout'
import { SignInPage } from './routes/SignInPage'
import { SignUpPage } from './routes/SignUpPage'

export default function App() {
  return (
    <Routes>
      <Route element={<ProtectedRoute />}>
        <Route element={<RootLayout />}>
          <Route element={<RequireAdmin />}>
            <Route path="/" element={<Dashboard />} />
          </Route>
          <Route path="/forbidden" element={<ForbiddenPage />} />
        </Route>
      </Route>

      <Route path="/sign-in/*" element={<SignInPage />} />
      <Route path="/sign-up/*" element={<SignUpPage />} />

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
