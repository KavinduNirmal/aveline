import React, { isValidElement } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeSlug from 'rehype-slug'
import rehypeAutolinkHeadings from 'rehype-autolink-headings'
import rehypeHighlight from 'rehype-highlight'
import { Link } from 'react-router-dom'
import { ExternalLink, ChevronLeft, ChevronRight } from 'lucide-react'
import { ALL_DOC_PAGES } from '@/docs/config'
import { Mermaid } from '@/components/docs/Mermaid'
import { CodeBlock } from '@/components/docs/CodeBlock'
import { cn } from '@/lib/utils'

interface DocsContentProps {
  markdown: string
  currentSlug: string
}

function extractText(node: React.ReactNode): string {
  if (typeof node === 'string') return node
  if (typeof node === 'number') return String(node)
  if (Array.isArray(node)) return node.map(extractText).join('')
  if (isValidElement(node)) {
    const props = node.props as { children?: React.ReactNode }
    return extractText(props.children)
  }
  return ''
}

export function DocsContent({ markdown, currentSlug }: DocsContentProps) {
  const currentIndex = ALL_DOC_PAGES.findIndex((p) => p.slug === currentSlug)
  const prevPage = currentIndex > 0 ? ALL_DOC_PAGES[currentIndex - 1] : null
  const nextPage =
    currentIndex >= 0 && currentIndex < ALL_DOC_PAGES.length - 1
      ? ALL_DOC_PAGES[currentIndex + 1]
      : null

  return (
    <article className="min-w-0 max-w-full">
      {/* Typeset container using shadcn/typeset system */}
      <div className="typeset typeset-docs text-foreground">
        <ReactMarkdown
          remarkPlugins={[remarkGfm]}
          rehypePlugins={[
            rehypeSlug,
            [
              rehypeAutolinkHeadings,
              {
                behavior: 'wrap',
                properties: {
                  className: ['heading-anchor'],
                },
              },
            ],
            [rehypeHighlight, { ignoreMissing: true }],
          ]}
          components={{
            a: ({ href, children, ...props }) => {
              if (href?.startsWith('http')) {
                return (
                  <a
                    href={href}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-1 font-medium text-commerce underline underline-offset-4 hover:opacity-80"
                    {...props}
                  >
                    {children}
                    <ExternalLink className="size-3 inline-block" />
                  </a>
                )
              }
              if (href?.startsWith('/')) {
                return (
                  <Link
                    to={href}
                    className="font-medium text-commerce underline underline-offset-4 hover:opacity-80"
                    {...props}
                  >
                    {children}
                  </Link>
                )
              }
              return (
                <a
                  href={href}
                  className="font-medium text-commerce underline underline-offset-4 hover:opacity-80"
                  {...props}
                >
                  {children}
                </a>
              )
            },
            code: ({ className, children, ...props }) => {
              const isInline = !className
              const match = /language-(\w+)/.exec(className || '')
              const language = match ? match[1] : ''
              const codeText = extractText(children).trim()

              // Handle Mermaid diagrams
              if (language === 'mermaid') {
                return <Mermaid chart={codeText} />
              }

              if (isInline) {
                return (
                  <code
                    className="rounded-md bg-muted px-1.5 py-0.5 font-mono text-[13px] font-medium text-foreground"
                    {...props}
                  >
                    {children}
                  </code>
                )
              }

              // Code blocks rendered inside pre with syntax highlighting & copy action
              return (
                <CodeBlock language={language} code={codeText}>
                  <code className={className} {...props}>
                    {children}
                  </code>
                </CodeBlock>
              )
            },
            pre: ({ children }) => {
              // Let the inner code component handle its own container and styling
              return <>{children}</>
            },
            table: ({ children }) => (
              <div className="typeset-scroll my-6">
                <table className="w-full text-left">{children}</table>
              </div>
            ),
          }}
        >
          {markdown}
        </ReactMarkdown>
      </div>

      {/* Pagination: Wide responsive Prev / Next navigation cards */}
      <nav
        aria-label="Documentation Pagination"
        className="mt-14 pt-8 border-t border-dashed border-border"
      >
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 w-full">
          {prevPage ? (
            <Link
              to={`/docs/${prevPage.slug}`}
              className="group flex flex-col justify-between w-full p-5 rounded-2xl border border-dashed border-border bg-card/70 hover:border-commerce/50 hover:bg-commerce/[0.02] transition-all text-left shadow-xs"
            >
              <div className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground group-hover:text-commerce transition-colors">
                <ChevronLeft className="size-4 transition-transform group-hover:-translate-x-1" />
                <span>Previous</span>
              </div>
              <div className="mt-3">
                <p className="font-serif text-lg font-medium text-foreground group-hover:text-commerce transition-colors">
                  {prevPage.title}
                </p>
                {prevPage.description && (
                  <p className="mt-1 text-xs text-muted-foreground line-clamp-2 leading-relaxed">
                    {prevPage.description}
                  </p>
                )}
              </div>
            </Link>
          ) : (
            <div className="hidden sm:block" />
          )}

          {nextPage && (
            <Link
              to={`/docs/${nextPage.slug}`}
              className={cn(
                'group flex flex-col justify-between w-full p-5 rounded-2xl border border-dashed border-border bg-card/70 hover:border-commerce/50 hover:bg-commerce/[0.02] transition-all text-right shadow-xs',
                !prevPage && 'sm:col-start-2'
              )}
            >
              <div className="flex items-center justify-end gap-1.5 text-xs font-semibold uppercase tracking-wider text-muted-foreground group-hover:text-commerce transition-colors">
                <span>Next</span>
                <ChevronRight className="size-4 transition-transform group-hover:translate-x-1" />
              </div>
              <div className="mt-3">
                <p className="font-serif text-lg font-medium text-foreground group-hover:text-commerce transition-colors">
                  {nextPage.title}
                </p>
                {nextPage.description && (
                  <p className="mt-1 text-xs text-muted-foreground line-clamp-2 leading-relaxed">
                    {nextPage.description}
                  </p>
                )}
              </div>
            </Link>
          )}
        </div>
      </nav>
    </article>
  )
}
