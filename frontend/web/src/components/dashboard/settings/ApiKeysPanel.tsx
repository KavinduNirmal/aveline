import { useCallback, useEffect, useState } from 'react'
import { Copy, KeyRound, RefreshCw, Trash2 } from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { toApiError } from '@/lib/api-error'
import { usePanelLoad } from '@/hooks/usePanelLoad'
import {
  createApiKey,
  deleteApiKey,
  fetchApiKeys,
  revokeApiKey,
  type ApiKey,
} from '@/lib/settings-api'

interface ApiKeysPanelProps {
  organizationId: string
  /** Writes require `apikeys:manage`; the list itself only needs `apikeys:view`. */
  canManage: boolean
}

/**
 * API keys for this boutique.
 *
 * **The secret is shown exactly once.** The server stores only a hash, so the create response's
 * plaintext is the only time it exists on the wire; the panel says so beside the value rather than
 * leaving the operator to discover it after closing the dialog.
 */
export function ApiKeysPanel({ organizationId, canManage }: ApiKeysPanelProps) {
  const [keys, setKeys] = useState<ApiKey[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [createOpen, setCreateOpen] = useState(false)
  const [name, setName] = useState('')
  const [scopes, setScopes] = useState('catalog:read')
  const [environment, setEnvironment] = useState('test')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [createdSecret, setCreatedSecret] = useState<string | null>(null)

  // A cancelled request is not a failure, and a superseded one must not overwrite a newer result.
  const load = usePanelLoad(
    (signal?: AbortSignal) => fetchApiKeys(organizationId, signal),
    (keys) => setKeys(keys),
    () => {
      setKeys([])
      setError('Could not load API keys.')
    },
    [organizationId],
  )

  const runLoad = useCallback(
    async (signal?: AbortSignal) => {
      setIsLoading(true)
      setError(null)
      await load(signal)
      setIsLoading(false)
    },
    [load],
  )

  useEffect(() => {
    const controller = new AbortController()
    void runLoad(controller.signal)
    return () => controller.abort()
  }, [runLoad])

  const submit = async () => {
    setIsSubmitting(true)
    setActionError(null)
    try {
      const created = await createApiKey(organizationId, {
        name: name.trim(),
        scopes: scopes
          .split(',')
          .map((scope) => scope.trim())
          .filter(Boolean),
        environment,
      })
      setCreatedSecret(created.secret)
      setName('')
      await runLoad()
    } catch (caught) {
      setActionError(toApiError(caught).message)
    } finally {
      setIsSubmitting(false)
    }
  }

  const revoke = async (key: ApiKey) => {
    try {
      await revokeApiKey(organizationId, key.id)
      toast.success('Key revoked.')
      await runLoad()
    } catch (caught) {
      toast.error(toApiError(caught).message)
    }
  }

  const remove = async (key: ApiKey) => {
    try {
      await deleteApiKey(organizationId, key.id)
      toast.success('Key deleted.')
      await runLoad()
    } catch (caught) {
      toast.error(toApiError(caught).message)
    }
  }

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <CardTitle className="font-serif text-lg font-medium">API keys</CardTitle>
            <CardDescription>
              Machine credentials for this boutique. The secret is shown once, at creation.
            </CardDescription>
          </div>
          {canManage ? (
            <Button
              type="button"
              size="sm"
              onClick={() => {
                setActionError(null)
                setCreatedSecret(null)
                setCreateOpen(true)
              }}
            >
              <KeyRound className="size-4" aria-hidden /> New key
            </Button>
          ) : (
            <Badge variant="outline">read-only</Badge>
          )}
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {isLoading ? (
          <Skeleton className="h-32 w-full" />
        ) : error ? (
          <div className="flex flex-col items-start gap-3">
            <p className="text-sm text-destructive">{error}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => void runLoad()}>
              Try again
            </Button>
          </div>
        ) : keys.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No API keys have been created for this boutique.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Prefix</TableHead>
                <TableHead>Environment</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Requests</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {keys.map((key) => (
                <TableRow key={key.id}>
                  <TableCell className="font-medium">{key.name}</TableCell>
                  <TableCell className="font-mono text-xs">{key.prefix}…</TableCell>
                  <TableCell>
                    <Badge variant="outline">{key.environment}</Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant={key.status === 'Active' ? 'secondary' : 'outline'}>
                      {key.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{key.requestCount}</TableCell>
                  <TableCell>
                    <div className="flex items-center justify-end gap-2">
                      {canManage && key.status === 'Active' ? (
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          onClick={() => void revoke(key)}
                        >
                          Revoke
                        </Button>
                      ) : null}
                      {canManage ? (
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          onClick={() => void remove(key)}
                        >
                          <Trash2 className="size-3.5 text-destructive" aria-hidden /> Delete
                        </Button>
                      ) : null}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Create an API key</DialogTitle>
            <DialogDescription>
              The scopes decide what the key may call. The secret is shown once and never again.
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="api-key-name">Name</Label>
              <Input
                id="api-key-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="Point of sale"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="api-key-scopes">Scopes (comma separated)</Label>
              <Input
                id="api-key-scopes"
                value={scopes}
                onChange={(event) => setScopes(event.target.value)}
                placeholder="catalog:read, customers:read"
              />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="api-key-environment">Environment</Label>
              <Select value={environment} onValueChange={setEnvironment}>
                <SelectTrigger id="api-key-environment" aria-label="Choose an environment">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="test">test</SelectItem>
                  <SelectItem value="live">live</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {createdSecret ? (
              <div className="flex flex-col gap-2 rounded-lg border border-primary/20 bg-primary/5 p-3">
                <p className="text-xs font-semibold uppercase tracking-wider text-primary">
                  Secret — copy it now
                </p>
                <code className="break-all font-mono text-sm">{createdSecret}</code>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    void navigator.clipboard?.writeText(createdSecret)
                    toast.success('Secret copied.')
                  }}
                >
                  <Copy className="size-3.5" aria-hidden /> Copy secret
                </Button>
                <p className="text-xs text-muted-foreground">
                  This is the only time the server can show it; only a hash is stored.
                </p>
              </div>
            ) : null}

            {actionError ? <p className="text-sm text-destructive">{actionError}</p> : null}
          </div>

          <DialogFooter>
            <Button
              type="button"
              disabled={isSubmitting || name.trim() === ''}
              onClick={() => void submit()}
            >
              {isSubmitting ? <RefreshCw className="size-4 animate-spin" aria-hidden /> : null}
              Create key
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  )
}
