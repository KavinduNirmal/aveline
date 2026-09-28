import { FileText, Image as ImageIcon, RotateCcw, X } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { Spinner } from '@/components/ui/spinner'
import type { PendingAttachment } from '@/contexts/ConversationsContext'
import { formatFileSize } from '@/lib/image-optimizer'

/**
 * Whether a failed chip may be retried. The classification travels on the chip, set by the context
 * where the HTTP status is known: 403 and 404 are refusals the server will not reconsider, while
 * network, timeout and 5xx failures are transient. Reading the flag rather than matching the error
 * text keeps this honest when the wording changes.
 */
function isRetryableUploadFailure(attachment: PendingAttachment): boolean {
  return attachment.retryable
}

/**
 * The once-per-file note for bytes the visual assistant cannot read. It is derived from the
 * **stored** content type the upload response reported, never the picked file's name: an image the
 * browser re-encoded to JPEG is analysable even when its source was HEIC.
 */
function notAnalysableNote(attachment: PendingAttachment): string | null {
  if (attachment.status !== 'ready' || attachment.analysable) return null
  if (attachment.storedContentType === 'application/pdf') {
    return "Stored in the thread. The visual assistant works on images, so it won't read this document."
  }
  const type = attachment.storedContentType ?? 'this format'
  return `Stored. Aveline's visual assistant can't analyse ${type}, so it won't comment on this file.`
}

export interface AttachmentTrayProps {
  /** The pending chips for one conversation, in pick order. */
  attachments: PendingAttachment[]
  onRetry: (attachmentId: string) => void
  onRemove: (attachmentId: string) => void
}

/**
 * The pending attachment chips shown directly above the composer's textarea. One chip per picked
 * file: its name and size, an honest state (an indeterminate busy indicator while uploading, a
 * specific message when it failed), a retry control where a retry can help, and a remove control
 * always.
 *
 * Progress is deliberately indeterminate. The upload helper exposes no byte-level progress and the
 * per-chip state lives in `ConversationsContext`, so any percentage here would be invented; a
 * spinner plus "Uploading…" is the truthful rendering.
 */
export function AttachmentTray({ attachments, onRetry, onRemove }: AttachmentTrayProps) {
  if (attachments.length === 0) return null

  return (
    <div className="px-3 pt-3">
      <Separator className="mb-2" />
      <ul aria-label="Pending attachments" className="flex gap-2 overflow-x-auto pb-1">
        {attachments.map((attachment) => {
          const note = notAnalysableNote(attachment)
          return (
            <li
              key={attachment.id}
              className="flex w-60 shrink-0 flex-col gap-1.5 rounded-lg border bg-muted/30 p-2"
            >
              <div className="flex items-center gap-2">
                {attachment.storedContentType?.startsWith('image/') ? (
                  <ImageIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                ) : (
                  <FileText className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                )}
                <div className="min-w-0 flex-1">
                  <p className="truncate text-xs font-medium">{attachment.fileName}</p>
                  <p className="text-[11px] text-muted-foreground">
                    {formatFileSize(attachment.sizeBytes)}
                  </p>
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-xs"
                  aria-label={`Remove ${attachment.fileName}`}
                  onClick={() => onRemove(attachment.id)}
                >
                  <X aria-hidden />
                </Button>
              </div>

              {attachment.status === 'uploading' ? (
                <div className="flex items-center gap-2">
                  <Spinner
                    className="size-3.5"
                    aria-label={`Uploading ${attachment.fileName}`}
                  />
                  <span className="text-[11px] text-muted-foreground">Uploading…</span>
                </div>
              ) : null}

              {attachment.status === 'ready' ? (
                <Badge variant="secondary" className="w-fit">
                  Ready
                </Badge>
              ) : null}

              {attachment.status === 'failed' ? (
                <div className="flex items-start gap-1">
                  <p className="flex-1 text-[11px] text-destructive">
                    {attachment.error ?? 'That file could not be uploaded.'}
                  </p>
                  {isRetryableUploadFailure(attachment) ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-xs"
                      aria-label={`Retry upload of ${attachment.fileName}`}
                      onClick={() => onRetry(attachment.id)}
                    >
                      <RotateCcw aria-hidden />
                    </Button>
                  ) : null}
                </div>
              ) : null}

              {note ? <p className="text-[11px] text-muted-foreground">{note}</p> : null}
            </li>
          )
        })}
      </ul>
    </div>
  )
}
