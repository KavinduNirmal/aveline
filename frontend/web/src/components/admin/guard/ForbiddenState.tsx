import { Link } from "react-router-dom"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ShieldAlert } from "lucide-react"

/**
 * The in-shell forbidden state. Used when the caller is signed in and identified but may not
 * open the console, or when a scope is not permitted. The message **names the reason**: a bare
 * "forbidden" is what makes an operator open a support ticket.
 */
export function ForbiddenState({
  title = "Administrator access required",
  reason,
}: {
  title?: string
  reason: string
}) {
  return (
    <div className="flex min-h-dvh items-center justify-center p-6 bg-background">
      <Card className="max-w-md w-full border-destructive/30 shadow-md">
        <CardHeader className="text-center">
          <div className="mx-auto w-12 h-12 rounded-full bg-destructive/10 text-destructive flex items-center justify-center mb-2">
            <ShieldAlert className="size-6" />
          </div>
          <CardTitle className="font-serif text-xl">{title}</CardTitle>
          <CardDescription>{reason}</CardDescription>
        </CardHeader>
        <CardContent className="flex justify-center">
          <Button asChild variant="outline">
            <Link to="/app">Back to your dashboard</Link>
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
