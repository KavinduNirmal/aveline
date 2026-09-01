import { useUser } from '@clerk/react'

/** Post-auth landing page for owners and managers. */
export function Dashboard() {
  const { user } = useUser()

  return (
    <section className="card">
      <h1>Dashboard</h1>
      <p>
        Signed in as <strong>{user?.fullName ?? user?.id}</strong>.
      </p>
      <p className="muted">
        Approvals, customers, inventory, and analytics land here as the slices
        are built out.
      </p>
    </section>
  )
}
