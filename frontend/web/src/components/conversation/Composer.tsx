import { Send } from 'lucide-react'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'

interface ComposerProps {
  onSend: (text: string) => void
  disabled?: boolean
  sending?: boolean
  placeholder?: string
}

/**
 * The message input at the bottom of a Salon. Enter sends; Shift+Enter inserts a newline.
 */
export function Composer({
  onSend,
  disabled,
  sending,
  placeholder = 'Message Aveline…',
}: ComposerProps) {
  const [value, setValue] = useState('')

  const submit = () => {
    const text = value.trim()
    if (!text || disabled || sending) return
    onSend(text)
    setValue('')
  }

  return (
    <div className="flex items-end gap-2 border-t bg-background p-3">
      <Textarea
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault()
            submit()
          }
        }}
        placeholder={placeholder}
        rows={1}
        className="max-h-32 min-h-10 resize-none"
        disabled={disabled}
      />
      <Button
        size="icon"
        onClick={submit}
        disabled={disabled || sending || !value.trim()}
        aria-label="Send message"
      >
        <Send className="size-4" aria-hidden />
      </Button>
    </div>
  )
}
