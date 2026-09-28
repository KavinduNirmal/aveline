import { Badge } from "@/components/ui/badge"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { useAdminScope } from "@/hooks/useAdminScope"

/**
 * States, in one place, **whose console this is**.
 *
 * The delivered panel showed a hard-coded operator identity with no indication of the
 * `{user_Id}` scope. An operator must be able to tell whether they are looking at their own
 * account or at a user they manage.
 */
export function AdminScopeChip() {
  const { roles, provisional } = useAdminSession()
  const { status, scope } = useAdminScope()

  const scopeLabel =
    status === "resolving"
      ? "resolving scope…"
      : scope.kind === "self"
        ? "your account"
        : scope.kind === "managed"
          ? "managed user"
          : scope.kind === "forbidden"
            ? "not permitted"
            : "unresolved"

  return (
    <div className="px-3 py-3 border-b border-sidebar-border" data-testid="admin-scope-chip">
      <div className="text-[11px] font-medium text-muted-foreground">Scope</div>
      <div className="text-xs font-semibold text-sidebar-foreground truncate">
        {scopeLabel}
        {provisional && (
          <span className="ml-1 text-[10px] font-normal text-muted-foreground">(provisional)</span>
        )}
      </div>
      <div className="flex gap-1 mt-1 flex-wrap">
        {roles.length === 0 ? (
          <span className="text-[10px] text-muted-foreground">no role claims</span>
        ) : (
          roles.map((role) => (
            <Badge key={role} variant="secondary" className="text-[10px] font-mono">
              {role}
            </Badge>
          ))
        )}
      </div>
    </div>
  )
}
