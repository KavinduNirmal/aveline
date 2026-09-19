import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { searchAdminOrganizations, setEntitlementOverrides } from "@/lib/admin/api"
import type { AdminOrganizationDto } from "@/types/admin"
import { Card, CardContent } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Search, SlidersHorizontal } from "lucide-react"
import {
  classifyOverrideError,
  coerceOverrideValue,
  inferOverrideValueType,
  type ClassifiedOverrideError,
} from "@/lib/admin/overrides"
import { toast } from "sonner"

export function AdminOrgsView() {
  const [search, setSearch] = useState("")
  const [planTier, setPlanTier] = useState<string>("all")
  const [page] = useState(1)

  const [selectedOrg, setSelectedOrg] = useState<AdminOrganizationDto | null>(null)
  const [overrideKey, setOverrideKey] = useState("feature:custom_styling")
  const [overrideValue, setOverrideValue] = useState("true")
  const [overrideReason, setOverrideReason] = useState("")
  const [savingOverride, setSavingOverride] = useState(false)
  const [overrideError, setOverrideError] = useState<ClassifiedOverrideError | null>(null)

  /** §3.7: a read-mostly list with operator-driven filtering — `keepPreviousData`, no polling. */
  const orgsQuery = useQuery({
    queryKey: ['admin', 'orgs', { search, planTier, page }],
    queryFn: () =>
      searchAdminOrganizations({
        q: search || undefined,
        planTier: planTier === "all" ? undefined : planTier,
        page,
        pageSize: 50,
      }),
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  })

  const orgs = orgsQuery.data?.items ?? []
  const loading = orgsQuery.isPending

  const loadOrgs = async () => {
    const result = await orgsQuery.refetch()
    if (result.error !== null) toast.error("Failed to load organizations")
  }

  const handleOverrideSubmit = async () => {
    if (!selectedOrg) return
    if (!overrideReason.trim()) {
      toast.error("Audit reason is required for entitlement overrides.")
      return
    }

    setSavingOverride(true)
    try {
      const now = new Date()
      const inOneYear = new Date()
      inOneYear.setFullYear(now.getFullYear() + 1)

      const valueType = inferOverrideValueType(overrideKey)
      const value = coerceOverrideValue(overrideValue, valueType)

      await setEntitlementOverrides(selectedOrg.id, {
        overrides: [
          {
            key: overrideKey,
            valueType,
            value,
            // The server defaults the window; the client states it explicitly and shows it.
            effectiveFrom: now.toISOString(),
            effectiveTo: inOneYear.toISOString(),
            reason: overrideReason.trim(),
          },
        ],
      })
      setOverrideError(null)
      toast.success("Entitlement override successfully configured.")
      setSelectedOrg(null)
    } catch (err: unknown) {
      // A 400 is a field-level error; a 409 override-overlap is "the world changed, reload and
      // retry". Rendering both as one toast leaves the operator editing a stale form.
      setOverrideError(classifyOverrideError(err))
    } finally {
      setSavingOverride(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
          Boutiques & Organizations
        </h2>
        <p className="text-sm text-muted-foreground">
          Manage boutique tenants, subscription tiers, and granular feature entitlement overrides.
        </p>
      </div>

      <Card className="border-border shadow-xs">
        <CardContent className="p-4 flex flex-wrap gap-4 items-center justify-between">
          <div className="relative flex-1 min-w-[240px]">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
            <Input
              placeholder="Search boutique by name or slug..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 text-xs"
            />
          </div>

          <div className="flex items-center gap-2">
            <Select value={planTier} onValueChange={setPlanTier}>
              <SelectTrigger className="text-xs h-9 w-[160px]">
                <SelectValue placeholder="All Tiers" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All Tiers</SelectItem>
                <SelectItem value="Seed">Seed</SelectItem>
                <SelectItem value="Bloom">Bloom</SelectItem>
                <SelectItem value="Orchid">Orchid</SelectItem>
                <SelectItem value="Rose">Rose</SelectItem>
                <SelectItem value="Enterprise">Enterprise</SelectItem>
              </SelectContent>
            </Select>
            <Button variant="outline" size="sm" onClick={() => void loadOrgs()} className="text-xs">
              Refresh
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card className="border-border shadow-xs overflow-hidden">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Boutique</TableHead>
              <TableHead>Slug</TableHead>
              <TableHead>Tier</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-xs text-muted-foreground">
                  Loading boutiques...
                </TableCell>
              </TableRow>
            ) : orgs.length === 0 ? (
              <TableRow>
                <TableCell colSpan={5} className="text-center py-8 text-xs text-muted-foreground">
                  No boutiques found.
                </TableCell>
              </TableRow>
            ) : (
              orgs.map((o) => (
                <TableRow key={o.id}>
                  <TableCell className="font-medium text-foreground">{o.name}</TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">{o.slug}</TableCell>
                  <TableCell>
                    <Badge variant="outline" className="font-mono text-[10px]">
                      {o.planTier}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant={o.isActive ? "default" : "secondary"} className="text-[11px]">
                      {o.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    <Button
                      variant="outline"
                      size="sm"
                      className="text-xs h-8 gap-1.5"
                      onClick={() => {
                        setSelectedOrg(o)
                        setOverrideReason("")
                        setOverrideError(null)
                      }}
                    >
                      <SlidersHorizontal className="size-3" />
                      Entitlements
                    </Button>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </Card>

      <Dialog open={!!selectedOrg} onOpenChange={(o) => !o && setSelectedOrg(null)}>
        <DialogContent className="sm:max-w-md bg-card">
          <DialogHeader>
            <DialogTitle className="font-serif">Set Entitlement Override</DialogTitle>
            <DialogDescription className="text-xs">
              Override entitlement parameters for {selectedOrg?.name} ({selectedOrg?.slug}).
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-4 py-2 text-xs">
            <div>
              <label className="font-medium block mb-1">Entitlement Key</label>
              <Input
                value={overrideKey}
                onChange={(e) => setOverrideKey(e.target.value)}
                className="text-xs font-mono"
              />
            </div>
            <div>
              <label className="font-medium block mb-1">
                Override Value
                <span className="ml-2 font-mono text-[10px] text-muted-foreground">
                  {inferOverrideValueType(overrideKey)}
                </span>
              </label>
              <Input
                value={overrideValue}
                onChange={(e) => setOverrideValue(e.target.value)}
                className="text-xs font-mono"
                aria-invalid={overrideError?.kind === "field"}
                aria-describedby={overrideError?.kind === "field" ? "override-error" : undefined}
              />
              {overrideError?.kind === "field" && (
                <p id="override-error" className="mt-1 text-[11px] text-destructive">
                  {overrideError.message}
                  {overrideError.fields !== undefined &&
                    Object.values(overrideError.fields)
                      .flat()
                      .map((message) => <span key={message} className="block">{message}</span>)}
                </p>
              )}
            </div>

            {overrideError?.kind === "overlap" && (
              <div className="rounded-md border border-warning/40 bg-warning/5 p-2 text-[11px] text-foreground">
                {overrideError.message}
                <Button
                  variant="outline"
                  size="sm"
                  className="mt-2 h-7 text-xs"
                  onClick={() => void loadOrgs()}
                >
                  Reload and retry
                </Button>
              </div>
            )}
            <div>
              <label className="font-medium block mb-1">
                Mandatory Reason <span className="text-destructive">*</span>
              </label>
              <Input
                placeholder="Reason for audit log..."
                value={overrideReason}
                onChange={(e) => setOverrideReason(e.target.value)}
                className="text-xs"
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" size="sm" onClick={() => setSelectedOrg(null)}>
              Cancel
            </Button>
            <Button
              size="sm"
              onClick={() => void handleOverrideSubmit()}
              disabled={savingOverride || overrideReason.trim().length === 0}
            >
              {savingOverride ? "Saving..." : "Apply Override"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
