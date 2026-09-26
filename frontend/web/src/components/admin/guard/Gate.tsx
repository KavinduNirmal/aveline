import type { ReactNode } from "react"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { canOpenGate, type Gate as GateType } from "@/lib/admin/role-policies"

/**
 * Renders its children only when the caller may open what they protect, and renders **nothing**
 * otherwise (or a supplied fallback).
 *
 * Absence, not hiding: a section the caller cannot use is not in the DOM at all. A greyed-out
 * control is indistinguishable from a broken one, and the delivered console showed sections a
 * caller could only receive a `403` from.
 */
export function Gate({
  gate,
  children,
  fallback = null,
}: {
  gate: GateType
  children: ReactNode
  fallback?: ReactNode
}) {
  const { roles, can } = useAdminSession()
  return canOpenGate(gate, { roles, can }) ? <>{children}</> : <>{fallback}</>
}
