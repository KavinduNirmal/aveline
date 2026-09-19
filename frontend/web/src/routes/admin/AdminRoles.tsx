import { ALL_PERMISSIONS, ROLE_PERMISSIONS } from "@/lib/admin/permissions"
import { Card, CardContent } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Check, Minus, Info } from "lucide-react"

export function AdminRolesView() {
  const roles = [
    "owner",
    "admin",
    "moderator",
    "staff",
    "customer_relations",
    "org:boutique_owner",
    "org:boutique_manager",
    "org:boutique_supervisor",
    "org:boutique_staff",
  ]

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Canonical Security & Authorization Matrix
        </h2>
        <p className="text-sm text-muted-foreground">
          Read-only authorization model mirroring `Aveline.Api/Authorization/Permissions.cs`.
        </p>
      </div>

      <Card className="border-border/80 bg-muted/20 shadow-xs">
        <CardContent className="p-4 flex items-start gap-3 text-xs text-foreground">
          <Info className="size-4 text-primary shrink-0 mt-0.5" />
          <p className="text-muted-foreground leading-relaxed">
            Roles are minted cryptographically by Clerk JWT claims. This matrix is authoritative for permission policy checks. Direct role assignment is performed via the access request queue or the Clerk User Management console.
          </p>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[220px]">Permission Identifier</TableHead>
              {roles.map((r) => (
                <TableHead key={r} className="text-center font-mono text-[11px]">
                  {r}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {ALL_PERMISSIONS.map((perm) => (
              <TableRow key={perm}>
                <TableCell className="font-mono text-xs font-medium text-foreground">
                  {perm}
                </TableCell>
                {roles.map((role) => {
                  const grants = ROLE_PERMISSIONS[role] || []
                  const has = grants.includes(perm)
                  return (
                    <TableCell key={role} className="text-center">
                      {has ? (
                        <Check className="size-4 text-primary mx-auto" />
                      ) : (
                        <Minus className="size-3 text-muted-foreground/30 mx-auto" />
                      )}
                    </TableCell>
                  )
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
