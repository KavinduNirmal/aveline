import { Component, type ErrorInfo, type ReactNode } from "react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

interface AdminErrorBoundaryState {
  error: Error | null
}

/**
 * One admin page failing must not take the whole console down. The boundary renders the
 * failure, never a substitute page: a dashboard that "recovers" into fiction is the defect the
 * overhaul exists to remove.
 */
export class AdminErrorBoundary extends Component<
  { children: ReactNode },
  AdminErrorBoundaryState
> {
  state: AdminErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): AdminErrorBoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // eslint-disable-next-line no-console
    console.error("Admin console error boundary", error, info.componentStack)
  }

  render(): ReactNode {
    if (this.state.error !== null) {
      return (
        <Card className="border-destructive/30 shadow-xs max-w-xl m-6">
          <CardHeader>
            <CardTitle className="font-serif text-base">This page failed to render</CardTitle>
            <CardDescription className="text-xs">
              {this.state.error.message}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button
              variant="outline"
              size="sm"
              className="text-xs"
              onClick={() => this.setState({ error: null })}
            >
              Try again
            </Button>
          </CardContent>
        </Card>
      )
    }
    return this.props.children
  }
}
