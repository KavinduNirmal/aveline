import { Download } from 'lucide-react'

import { AuroraField } from '@/components/site/AuroraField'
import { Reveal } from '@/components/site/Reveal'
import { SitePage } from '@/components/site/SitePage'
import { Button } from '@/components/ui/button'
import { AppleIcon, PlayStoreIcon } from '@/components/site/Icons'

export function DownloadPage() {
  return (
    <SitePage>
      <section className="relative overflow-hidden">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-4xl px-5 py-20 lg:px-8">
        <Reveal className="max-w-2xl">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
            Download
          </p>
          <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
            Take Aveline with you.
          </h1>
          <p className="mt-4 text-lg text-neutral-500">
            Associates live in the mobile app — message customers, join a boutique
            and watch the workflow move from anywhere.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-4 sm:grid-cols-2">
          <Reveal>
            <div className="flex h-full flex-col justify-between rounded-3xl border-2 border-dashed border-neutral-200 bg-white p-8">
              <span className="flex size-11 items-center justify-center rounded-full bg-neutral-900 text-white">
                <AppleIcon className="size-5" aria-hidden />
              </span>
              <div className="mt-6">
                <h2 className="text-lg font-semibold text-neutral-900">iOS</h2>
                <p className="mt-1 text-sm text-neutral-500">
                  The Aveline app for iPhone — coming soon to the App Store.
                </p>
              </div>
              <Button asChild size="lg" variant="outline" className="mt-6 h-11 w-full" disabled>
                Coming soon
              </Button>
            </div>
          </Reveal>

          <Reveal delay={0.06}>
            <div className="flex h-full flex-col justify-between rounded-3xl border-2 border-dashed border-neutral-200 bg-white p-8">
              <span className="flex size-11 items-center justify-center rounded-full bg-[#01875f] text-white">
                <PlayStoreIcon className="size-5" />
              </span>
              <div className="mt-6">
                <h2 className="text-lg font-semibold text-neutral-900">Android</h2>
                <p className="mt-1 text-sm text-neutral-500">
                  Install the latest APK build directly — published with every
                  release.
                </p>
              </div>
              <Button asChild size="lg" className="mt-6 h-11 w-full">
                <a
                  href="https://github.com/KavinduNirmal/aveline/releases/latest"
                  target="_blank"
                  rel="noreferrer"
                >
                  <Download className="size-4" aria-hidden />
                  Download APK
                </a>
              </Button>
            </div>
          </Reveal>
        </div>

        <Reveal delay={0.1}>
          <p className="mx-auto mt-10 max-w-xl text-center text-sm leading-relaxed text-neutral-400">
            The mobile app is for boutique associates and managers. Owners manage
            approvals from the web dashboard —{' '}
            <a href="mailto:support@aveline.lk" className="text-commerce hover:underline">
              support@aveline.lk
            </a>{' '}
            if you need help installing.
          </p>
        </Reveal>
        </div>
      </section>
    </SitePage>
  )
}
