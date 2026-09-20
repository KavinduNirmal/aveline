import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const webRoot = fileURLToPath(new URL('../..', import.meta.url))

/**
 * Paths under `src/` that are *deliberately* ignored by git.
 *
 * An entry here means "this file must not be committed". Anything else that git ignores under
 * `src/` is a bug, because it exists on the author's disk and nowhere else.
 */
const ALLOWED_IGNORED_SOURCES: string[] = []

/**
 * Nothing under `src/` may be git-ignored.
 *
 * This is a regression guard, not a style rule. An unanchored `logs` pattern in
 * `frontend/web/.gitignore` plus the Visual Studio template's `[Ll]ogs/` in the root
 * `.gitignore` silently swallowed `src/components/admin/logs/` — four components of the admin
 * log viewer. They built fine on the machine that wrote them and broke every clean clone, so CI
 * and Vercel failed on `AdminLogs.tsx` importing modules that were never committed. Worse, the
 * blocking `admin-conformance` allow-list *named a file inside that directory*, so the gate was
 * asserting against a file only the original author had.
 *
 * A quick `git check-ignore -v <path>` failure is a far better signal than a red deploy.
 */
describe('ignored source guard', () => {
  it('commits every file under src/ (none of them are git-ignored)', () => {
    let stdout: string
    try {
      stdout = execFileSync(
        'git',
        ['ls-files', '--others', '--ignored', '--exclude-standard', '--', 'src'],
        { cwd: webRoot, encoding: 'utf8' },
      )
    } catch (error) {
      throw new Error(
        'Could not run `git ls-files` in frontend/web. This guard reads the real ignore rules, ' +
          'so it needs a git checkout rather than an exported tarball.',
        { cause: error },
      )
    }

    const ignored = stdout.split('\n').filter((line) => line.trim().length > 0)

    expect(ignored).toEqual(ALLOWED_IGNORED_SOURCES)
  })
})
