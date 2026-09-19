import { useEffect, useState } from "react"
import { queryAuditEntries } from "@/lib/admin/api"
import type { AuditLogEntry } from "@/types/admin"
import { Card, CardContent } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"

export function AdminAuditView() {
  const [entries, setEntries] = useState<AuditLogEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [action, setAction] = useState("")
  const [entityType, setEntityType] = useState("")
  const [page] = useState(1)
  const [total, setTotal] = useState(0)

  const loadAudit = async () => {
    setLoading(true)
    try {
      const res = await queryAuditEntries({
        action: action || undefined,
        entityType: entityType || undefined,
        page,
        pageSize: 25,
      })
      setEntries(res.items)
      setTotal(res.total)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    const t = setTimeout(() => {
      void loadAudit()
    }, 250)
    return () => clearTimeout(t)
  }, [action, entityType, page])

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Audit Ledger Explorer
        </h2>
        <p className="text-sm text-muted-foreground">
          Full historical business action queries with immutable cryptographic UUIDv7 identifiers.
        </p>
      </div>

      <Card className="border-border shadow-xs">
        <CardContent className="p-4 flex flex-wrap gap-4 items-center justify-between text-xs">
          <div className="flex gap-2 flex-1 min-w-[300px]">
            <Input
              placeholder="Filter by action (e.g. users.state.updated)..."
              value={action}
              onChange={(e) => setAction(e.target.value)}
              className="text-xs"
            />
            <Input
              placeholder="Entity type (e.g. User, Organization)..."
              value={entityType}
              onChange={(e) => setEntityType(e.target.value)}
              className="text-xs max-w-[200px]"
            />
          </div>

          <div className="text-muted-foreground">
            Total Records: <span className="font-semibold text-foreground">{total}</span>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Timestamp</TableHead>
              <TableHead>Action</TableHead>
              <TableHead>Entity</TableHead>
              <TableHead>Actor</TableHead>
              <TableHead>Reason</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-xs text-muted-foreground">
                  Querying audit ledger...
                </TableCell>
              </TableRow>
            ) : entries.length === 0 ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-xs text-muted-foreground">
                  No records match filter criteria.
                </TableCell>
              </TableRow>
            ) : (
              entries.map((e) => (
                <TableRow key={e.id}>
                  <TableCell className="font-mono text-xs text-muted-foreground">
                    {new Date(e.occurredAt).toLocaleString()}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline" className="font-mono text-[11px]">
                      {e.action}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs font-mono">
                    <span className="font-medium text-foreground">{e.entityType}</span>
                    <span className="text-muted-foreground">:{e.entityId}</span>
                  </TableCell>
                  <TableCell className="text-xs font-mono text-muted-foreground">
                    {e.actorRef || e.actorUserId || e.actorKind}
                  </TableCell>
                  <TableCell className="text-xs italic text-muted-foreground">
                    {e.reason || "—"}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
