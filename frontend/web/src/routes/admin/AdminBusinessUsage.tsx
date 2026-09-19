/**
 * The usage console (`/admin/:userId/business/usage`). Delivered in Business KPIs phase 6, which
 * adds the rank table and the organization drill-down.
 */
export function AdminBusinessUsageView() {
  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="font-serif text-3xl font-medium tracking-tight text-foreground">
          Usage &amp; engagement
        </h1>
        <p className="text-xs text-muted-foreground">
          Messages, agent runs, API calls and Blossom consumption per organization.
        </p>
      </div>
    </div>
  )
}
