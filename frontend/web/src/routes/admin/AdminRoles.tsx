import { ALL_PERMISSIONS, ROLE_PERMISSIONS } from "@/lib/admin/permissions"
import { ROLE_POLICIES } from "@/lib/admin/role-policies"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"
import { Check, Minus, Info } from "lucide-react"

/**
 * The canonical matrix, read-only.
 *
 * Both mirrors it renders are checked against the C# by a generated drift test
 * (`permissions.sync.test.ts`, `role-policies.sync.test.ts`), so this page cannot silently lie
 * about the catalogue. The role list is derived from `ROLE_PERMISSIONS` rather than hand-written,
 * for the same reason.
 */
const ROLES = Object.keys(ROLE_PERMISSIONS)

export function AdminRolesView() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Canonical Security &amp; Authorization Matrix
        </h2>
        <p className="text-sm text-muted-foreground">
          Read-only view of `Aveline.Api/Authorization/Permissions.cs` — {ALL_PERMISSIONS.length}{' '}
          permissions across {ROLES.length} canonical roles.
        </p>
      </div>

      <Card className="border-border/80 bg-muted/20 shadow-xs">
        <CardContent className="p-4 flex items-start gap-3 text-xs text-foreground">
          <Info className="size-4 text-primary shrink-0 mt-0.5" />
          <div className="space-y-2">
            <p className="text-muted-foreground leading-relaxed">
              <span className="font-medium text-foreground">The server is authoritative.</span>{' '}
              This table is a presentation mirror used for navigation and gating; it is never the
              enforcement point. Roles are minted from Clerk metadata, and no role-write endpoint
              exists — a role changes through the access request queue or the Clerk console.
            </p>
            <p className="text-muted-foreground leading-relaxed">
              A permission table alone cannot express the role-guarded surfaces.{' '}
              <code className="bg-muted px-1.5 py-0.5 rounded font-mono">audit:view</code>,{' '}
              <code className="bg-muted px-1.5 py-0.5 rounded font-mono">stats:system</code> and{' '}
              <code className="bg-muted px-1.5 py-0.5 rounded font-mono">pricing:view</code> are
              registered as policies for every catalogue entry and referenced by no endpoint, while
              the routes that own them use a role requirement. The console therefore carries a
              separate role overlay.
            </p>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs">
        <CardHeader>
          <CardTitle className="font-serif text-base">Role-guarded policies</CardTitle>
          <CardDescription className="text-xs">
            Mirrors of the four `RequireRole(...)` registrations in
            `AuthorizationConfiguration.cs`, by name.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          {Object.values(ROLE_POLICIES).map((policy) => (
            <div
              key={policy.policy}
              className="rounded-md border border-border px-2.5 py-1.5 text-[11px]"
            >
              <span className="font-mono font-medium text-foreground">{policy.policy}</span>
              <span className="ml-2 text-muted-foreground">{policy.anyOf.join(', ')}</span>
            </div>
          ))}
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        <Table>
          <caption className="sr-only">
            Canonical role-to-permission matrix, {ALL_PERMISSIONS.length} permissions by{' '}
            {ROLES.length} roles
          </caption>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[220px]">Permission identifier</TableHead>
              {ROLES.map((role) => (
                <TableHead key={role} className="text-center font-mono text-[11px]">
                  {role}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {ALL_PERMISSIONS.map((permission) => (
              <TableRow key={permission}>
                <TableCell className="font-mono text-xs font-medium text-foreground">
                  {permission}
                </TableCell>
                {ROLES.map((role) => {
                  const holds = (ROLE_PERMISSIONS[role] ?? []).includes(permission)
                  return (
                    <TableCell key={role} className="text-center">
                      {holds ? (
                        <span
                          aria-label={`${role} holds ${permission}`}
                          className="inline-flex justify-center"
                        >
                          <Check className="size-4 text-primary" />
                        </span>
                      ) : (
                        <span
                          aria-label={`${role} does not hold ${permission}`}
                          className="inline-flex justify-center"
                        >
                          <Minus className="size-3 text-muted-foreground/40" />
                        </span>
                      )}
                    </TableCell>
                  )
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      <div className="flex flex-wrap gap-2">
        <Badge variant="secondary" className="text-[10px]">
          {ALL_PERMISSIONS.length} permissions
        </Badge>
        <Badge variant="secondary" className="text-[10px]">
          {ROLES.length} roles
        </Badge>
        <Badge variant="secondary" className="text-[10px]">
          {Object.keys(ROLE_POLICIES).length} role policies
        </Badge>
      </div>
    </div>
  )
}
