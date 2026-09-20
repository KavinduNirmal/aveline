import { useAdminSession } from "@/contexts/AdminSessionContext"
import type { Permission } from "@/lib/admin/permissions"

/**
 * Whether the caller holds a permission. Presentation and navigation only: the server is
 * always authoritative, and this mirror exists so a control the caller cannot use is not
 * rendered at all.
 */
export function useCan(permission: Permission): boolean {
  return useAdminSession().can(permission)
}
