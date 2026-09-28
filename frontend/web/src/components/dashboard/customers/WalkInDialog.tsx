import { useEffect, useState } from 'react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
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
import { toApiError } from '@/lib/api-error'
import { createWalkInCustomer } from '@/lib/customers-api'

interface WalkInDialogProps {
  organizationId: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onCreated: () => void
}

/**
 * Creates a counter walk-in (`POST /customers`).
 *
 * The **200-vs-201 distinction is honoured in the wording, not just the status code**: a duplicate
 * name returns the existing client with `duplicateOfCustomerId` set, and this dialog says "already
 * on file" rather than "created". Reporting a create that did not happen is the kind of small lie
 * that makes a client book untrustworthy.
 */
export function WalkInDialog({ organizationId, open, onOpenChange, onCreated }: WalkInDialogProps) {
  const [fullName, setFullName] = useState('')
  const [phoneNumber, setPhoneNumber] = useState('')
  const [nickname, setNickname] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  // One key per operation: a retry of the same walk-in must not mint a second client.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

  useEffect(() => {
    if (open) {
      setFullName('')
      setPhoneNumber('')
      setNickname('')
      setIsSaving(false)
      setIdempotencyKey(crypto.randomUUID())
    }
  }, [open])

  const submit = async () => {
    if (fullName.trim().length === 0) {
      toast.error('A name is required to start a client record.')
      return
    }

    setIsSaving(true)
    try {
      const created = await createWalkInCustomer(
        organizationId,
        {
          fullName: fullName.trim(),
          phoneNumber: phoneNumber.trim().length > 0 ? phoneNumber.trim() : undefined,
          nickname: nickname.trim().length > 0 ? nickname.trim() : undefined,
        },
        idempotencyKey,
      )
      onCreated()
      onOpenChange(false)
      if (created.duplicateOfCustomerId) {
        toast('That client is already on file', {
          description: `${created.fullName ?? 'The existing record'} was matched by name. Nothing new was created.`,
        })
      } else {
        toast.success('Client added', { description: created.fullName ?? 'Walk-in saved' })
      }
    } catch (caught) {
      const apiError = toApiError(caught)
      toast.error('Could not add this client', {
        description:
          apiError.code === 'customer-phone-conflict'
            ? 'Another client in this boutique already has that phone number.'
            : apiError.message,
      })
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="font-serif">Add a walk-in client</DialogTitle>
          <DialogDescription>
            A name is enough to start a record. A name that already exists reports the existing
            client instead of creating a second one.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="walkin-name">Name</Label>
            <Input
              id="walkin-name"
              value={fullName}
              onChange={(event) => setFullName(event.target.value)}
              placeholder="Full name"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="walkin-phone">Phone</Label>
            <Input
              id="walkin-phone"
              value={phoneNumber}
              onChange={(event) => setPhoneNumber(event.target.value)}
              placeholder="0771234567"
            />
            <p className="text-xs text-muted-foreground">
              Optional. A number another client already holds is refused rather than silently
              creating a duplicate identity.
            </p>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="walkin-nickname">Nickname</Label>
            <Input
              id="walkin-nickname"
              value={nickname}
              onChange={(event) => setNickname(event.target.value)}
              placeholder="What the floor calls them"
            />
          </div>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={isSaving}>
            Cancel
          </Button>
          <Button type="button" onClick={() => void submit()} disabled={isSaving}>
            Add client
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
