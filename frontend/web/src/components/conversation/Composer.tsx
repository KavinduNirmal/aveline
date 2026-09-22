import { CircleAlert, Paperclip, Send } from 'lucide-react'
import { useRef, useState } from 'react'
import { toast } from 'sonner'

import { AttachmentTray } from '@/components/conversation/AttachmentTray'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import type { PendingAttachment } from '@/contexts/ConversationsContext'
import { checkAttachmentCap, type AttachmentRefusal } from '@/lib/attachment-preparation'

/** Ties the send button to the sentence that says why it is unavailable. */
const SEND_STATUS_ID = 'composer-attachment-status'

/** Shown when the server did not confirm a send. The composed text is never cleared on failure. */
const SEND_FAILURE_MESSAGE =
  'That message could not be sent. Nothing was lost; your text and files are still here.'

interface ComposerProps {
  /**
   * Sends the composed message and the ids of the stored chips it binds. Resolving means the
   * server confirmed the message, and only then is the textarea cleared: a rejection keeps the
   * text so the user can retry without retyping it.
   */
  onSend: (text: string, attachmentIds?: string[]) => void | Promise<void>
  /**
   * Picks files for the open conversation. Each is uploaded on pick and the refusals the user must
   * be told about (over-cap, unsupported type, the sixth file) come back to be displayed here.
   */
  onAttach?: (files: File[]) => Promise<AttachmentRefusal[]>
  /** The pending tray for the open conversation. */
  pendingAttachments?: PendingAttachment[]
  onRetryAttachment?: (attachmentId: string) => void
  onRemoveAttachment?: (attachmentId: string) => void
  disabled?: boolean
  sending?: boolean
  placeholder?: string
}

/**
 * The message input at the bottom of a Salon. Enter sends; Shift+Enter inserts a newline.
 *
 * Files are picked with the shadcn hidden-input pattern (`AddProductModal`): a paperclip `Button`
 * opens a visually hidden `Input type="file"`. The allow-list is the attachment route's, which is
 * deliberately wider than the catalog's `image/*`: it includes PDF.
 */
export function Composer({
  onSend,
  onAttach,
  pendingAttachments,
  onRetryAttachment,
  onRemoveAttachment,
  disabled,
  sending,
  placeholder = 'Message Aveline…',
}: ComposerProps) {
  const [value, setValue] = useState('')
  const [refusals, setRefusals] = useState<AttachmentRefusal[]>([])
  const [sendError, setSendError] = useState<string | null>(null)
  const [preparing, setPreparing] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const chips = pendingAttachments ?? []
  const uploading = chips.find((attachment) => attachment.status === 'uploading')
  const notStored = chips.find((attachment) => attachment.status !== 'ready')

  // Why the send control is unavailable, in words rather than a colour. An in-flight upload and a
  // failed one are different situations and say so differently.
  const sendBlockedReason = uploading
    ? `Waiting for ${uploading.fileName} to finish uploading. Sending is unavailable until every attached file is stored.`
    : notStored
      ? 'An attachment failed to upload. Retry or remove it before sending.'
      : null

  const readyIds = chips
    .filter((attachment) => attachment.status === 'ready' && attachment.attachmentId)
    .map((attachment) => attachment.attachmentId as string)

  const handleFiles = async (files: File[]) => {
    if (files.length === 0 || !onAttach) return

    // The count cap is enforced here, before `onAttach`, so a sixth file never starts an upload
    // and never becomes an orphan row. The message is the server's own wording, unchanged.
    const accepted: File[] = []
    const refused: AttachmentRefusal[] = []
    for (const file of files) {
      const cap = checkAttachmentCap(chips.length + accepted.length)
      if (cap) {
        refused.push({ ...cap, fileName: file.name })
        continue
      }
      accepted.push(file)
    }

    let prepareRefusals: AttachmentRefusal[] = []
    if (accepted.length > 0) {
      setPreparing(true)
      try {
        prepareRefusals = await onAttach(accepted)
      } catch {
        toast.error('Those files could not be prepared for upload. Please try again.')
      } finally {
        setPreparing(false)
      }
    }

    const all = [...refused, ...prepareRefusals]
    setRefusals(all)
    for (const refusal of all) toast.error(refusal.message)
  }

  const submit = async () => {
    const text = value.trim()
    if (!text || disabled || sending || preparing || sendBlockedReason) return
    setSendError(null)
    try {
      await onSend(text, readyIds.length > 0 ? readyIds : undefined)
      // Confirmed by the server. The text and the refusal notice are dropped only now, so a failed
      // send can never cost the user their message.
      setValue('')
      setRefusals([])
    } catch {
      setSendError(SEND_FAILURE_MESSAGE)
      toast.error(SEND_FAILURE_MESSAGE)
    }
  }

  return (
    <div className="border-t bg-background">
      {refusals.length > 0 ? (
        <div className="px-3 pt-3">
          <Alert variant="destructive">
            <CircleAlert aria-hidden />
            <AlertTitle>Some files were not attached</AlertTitle>
            <AlertDescription>
              <ul className="list-disc pl-4">
                {refusals.map((refusal, index) => (
                  <li key={`${refusal.fileName ?? 'file'}-${index}`}>{refusal.message}</li>
                ))}
              </ul>
            </AlertDescription>
          </Alert>
        </div>
      ) : null}

      {sendError ? (
        <div className="px-3 pt-3">
          <Alert variant="destructive">
            <CircleAlert aria-hidden />
            <AlertTitle>Message not sent</AlertTitle>
            <AlertDescription>{sendError}</AlertDescription>
          </Alert>
        </div>
      ) : null}

      <AttachmentTray
        attachments={chips}
        onRetry={(attachmentId) => onRetryAttachment?.(attachmentId)}
        onRemove={(attachmentId) => onRemoveAttachment?.(attachmentId)}
      />

      <div className="flex items-end gap-2 p-3">
        {onAttach ? (
          <>
            <Input
              ref={fileInputRef}
              type="file"
              multiple
              // The attachment route's allow-list: nine image types plus a PDF. Deliberately not
              // the catalog's `image/*`, which would hide PDFs the route stores.
              accept="image/*,application/pdf"
              className="hidden"
              aria-label="Choose files to attach"
              onChange={(event) => {
                const files = Array.from(event.target.files ?? [])
                // Reset so choosing the same file again still fires a change event.
                event.target.value = ''
                void handleFiles(files)
              }}
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              aria-label="Attach files"
              disabled={disabled || preparing}
              onClick={() => fileInputRef.current?.click()}
            >
              <Paperclip className="size-4" aria-hidden />
            </Button>
          </>
        ) : null}

        <Textarea
          aria-label="Message"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault()
              void submit()
            }
          }}
          placeholder={placeholder}
          rows={1}
          className="max-h-32 min-h-10 resize-none"
          disabled={disabled}
        />
        <Button
          size="icon"
          onClick={() => void submit()}
          disabled={
            disabled || sending || preparing || Boolean(sendBlockedReason) || !value.trim()
          }
          aria-label="Send message"
          aria-describedby={sendBlockedReason ? SEND_STATUS_ID : undefined}
        >
          <Send className="size-4" aria-hidden />
        </Button>
      </div>

      {sendBlockedReason ? (
        <p id={SEND_STATUS_ID} role="status" className="sr-only">
          {sendBlockedReason}
        </p>
      ) : null}
    </div>
  )
}
