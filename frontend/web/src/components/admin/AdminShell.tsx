import { useState } from "react"
import { Outlet, useParams } from "react-router-dom"
import { AdminSidePanel } from "@/components/admin/AdminSidePanel"
import { AdminHeader } from "@/components/admin/AdminHeader"
import { AuditTrailPanel } from "@/components/admin/AuditTrailPanel"

export function AdminShell() {
  const { userId } = useParams<{ userId: string }>()
  const [auditOpen, setAuditOpen] = useState(false)

  return (
    <div className="min-h-screen bg-background text-foreground flex">
      {/* Side Navigation Panel */}
      <AdminSidePanel />

      {/* Main Administrative Workspace */}
      <div className="flex-1 flex flex-col min-w-0">
        <AdminHeader onOpenAudit={() => setAuditOpen(true)} />

        <main id="admin-main" className="flex-1 p-8 overflow-y-auto max-w-7xl w-full mx-auto">
          <Outlet />
        </main>
      </div>

      {/* Global Audit Side Drawer */}
      <AuditTrailPanel
        open={auditOpen}
        onClose={() => setAuditOpen(false)}
        actorUserId={userId}
        title="Admin Activity Log"
      />
    </div>
  )
}
