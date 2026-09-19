import { useQuery } from "@tanstack/react-query"
import { useState } from "react"

import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { searchAdminOrganizations } from "@/lib/admin/api"
import type { AdminOrganizationDto } from "@/types/admin"
import { Search } from "lucide-react"

/**
 * Picks an organization by searching, instead of asking an operator to paste a GUID.
 *
 * The ledger and entitlement flows are org-scoped, and the delivered console required the operator
 * to type the organization's GUID by hand — which is both error-prone and impossible in practice.
 * This is the plan's `OrgPicker`: a query box over `GET /admin/orgs`, with the selection reported
 * as a full `AdminOrganizationDto` so the caller never re-parses an id.
 */
export function OrgPicker({
  value,
  onSelect,
  label = "Organization",
}: {
  value: AdminOrganizationDto | null
  onSelect: (organization: AdminOrganizationDto) => void
  label?: string
}) {
  const [term, setTerm] = useState("")
  const trimmed = term.trim()

  const orgsQuery = useQuery({
    queryKey: ["admin", "org-picker", trimmed],
    queryFn: () => searchAdminOrganizations({ q: trimmed || undefined, pageSize: 10 }),
    enabled: trimmed.length >= 2,
    staleTime: 30_000,
  })

  const results = orgsQuery.data?.items ?? []

  return (
    <div className="flex flex-col gap-2">
      <label className="text-xs font-medium" htmlFor="org-picker-search">
        {label}
      </label>
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
        <Input
          id="org-picker-search"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          placeholder="Search by name or slug…"
          className="pl-9 text-xs"
        />
      </div>

      {value !== null && (
        <div className="rounded-md border border-border px-3 py-2 text-xs">
          <span className="font-medium text-foreground">{value.name}</span>
          <span className="ml-2 font-mono text-[11px] text-muted-foreground">{value.slug}</span>
        </div>
      )}

      {trimmed.length >= 2 && (
        <div role="listbox" aria-label="Organization results" className="flex flex-col gap-1">
          {orgsQuery.isPending ? (
            <p className="text-[11px] text-muted-foreground">Searching…</p>
          ) : orgsQuery.isError ? (
            <p className="text-[11px] text-muted-foreground">
              The organization search failed. Try again.
            </p>
          ) : results.length === 0 ? (
            <p className="text-[11px] text-muted-foreground">No organizations match that search.</p>
          ) : (
            results.map((organization) => (
              <Button
                key={organization.id}
                type="button"
                role="option"
                aria-selected={value?.id === organization.id}
                variant="ghost"
                size="sm"
                className="h-auto justify-start px-2 py-1.5 text-xs"
                onClick={() => onSelect(organization)}
              >
                <span className="font-medium text-foreground">{organization.name}</span>
                <span className="ml-2 font-mono text-[11px] text-muted-foreground">
                  {organization.slug}
                </span>
              </Button>
            ))
          )}
        </div>
      )}
    </div>
  )
}
