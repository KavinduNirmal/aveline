import { motion } from 'motion/react'

import { Blossom } from '@/components/auth/Blossom'
import { Button } from '@/components/ui/button'
import { useConversations } from '@/contexts/ConversationsContext'
import { cn } from '@/lib/utils'

interface AvelineChatLauncherProps {
  open: boolean
  onOpen: () => void
}

/**
 * The Aveline chat launcher shown in the dashboard header. It is a primary call-to-action,
 * so the Blossom mark is always in motion: the whole flower slowly rotates while its petals
 * counter-sway (mirroring the landing-page medallion) and its colour cycles through the
 * persona accents (primary -> Ava -> Elle -> Lina). When Aveline is processing a reply
 * (`waiting`) the sway is added so the blossom visibly "thinks".
 */
export function AvelineChatLauncher({ open, onOpen }: AvelineChatLauncherProps) {
  const { waiting } = useConversations()

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={onOpen}
      aria-label={open ? 'Aveline chat is open' : 'Open Aveline chat'}
      title="Aveline"
      className={cn(
        'relative size-9 rounded-full',
        open && 'bg-primary/10',
      )}
    >
      <motion.div
        className="flex size-5 items-center justify-center"
        animate={{ rotate: 360 }}
        transition={{ duration: 12, repeat: Infinity, ease: 'linear' }}
      >
        <Blossom
          className="aveline-waiting size-5"
          animateCounter={waiting}
          counterDuration={2.4}
        />
      </motion.div>
    </Button>
  )
}
