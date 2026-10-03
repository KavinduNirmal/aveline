/**
 * Shared configuration for the k6 performance suite.
 *
 * Every script in this directory is a **gate**, not a benchmark: it is run by CI
 * against a composed stack (PostgreSQL + Redis + API + agent service) and its
 * exit code is what stops a merge. Three consequences follow, and each of them
 * is the fix for a defect the SE3110 gap analysis (`D-11`) recorded against the
 * single script that used to live here:
 *
 * 1. **There is no default credential, and there is no default that silently
 *    degrades.** The old script defaulted `API_BASE_URL` to `localhost:5000`
 *    (nothing listens there; compose publishes the API on 5091) and defaulted
 *    `AUTH_TOKEN` to the literal string `test-token`. A run in that shape got
 *    401s from every measured endpoint and still passed, because the check
 *    counted `401` as success. A gate that cannot fail is worse than no gate,
 *    so `requireCredential()` below aborts the run at init time with the exact
 *    variable that is missing and how to obtain it.
 *
 * 2. **The base URL default matches compose.** `docker-compose.yml` publishes
 *    the API on `${API_PORT:-5091}`, so that is the default.
 *
 * 3. **The credential is explicit.** The API accepts two schemes on the
 *    org-scoped routes this suite measures: a Clerk bearer token
 *    (`Authorization: Bearer <jwt>`) and an Aveline API key
 *    (`X-Api-Key: avl_...`). The E2E stack seeds an API key with the scopes the
 *    measured routes need, so CI passes `API_KEY`; `AUTH_TOKEN` is accepted for
 *    a run against an environment that mints real Clerk sessions instead.
 */

/** Compose publishes the API here (`docker-compose.yml`). */
export const DEFAULT_API_BASE_URL = 'http://localhost:5091';

/**
 * Read a required environment value or abort the run.
 *
 * `k6 run` evaluates the init context before it opens a single connection, so a
 * throw here costs nothing and fails loudly instead of producing a green run
 * made entirely of 401s.
 */
export function requireEnv(name) {
  const value = __ENV[name];
  if (!value || String(value).trim() === '') {
    throw new Error(
      `${name} is required. This script is a gate, not a smoke test: it will not ` +
        `run without a real credential, because an unauthenticated run would ` +
        `measure nothing but the 401 path. Set it on the command line ` +
        `(k6 run -e ${name}=...) or in the CI job's env block.`,
    );
  }
  return value;
}

/** The API base URL, overridable with `-e API_BASE_URL=`. */
export function apiBaseUrl() {
  return (__ENV.API_BASE_URL || DEFAULT_API_BASE_URL).replace(/\/+$/, '');
}

/**
 * The org every measured route is scoped to. Required: the org-scoped routes
 * carry `{organizationId:guid}`, and there is no sensible default.
 */
export function requireOrgId() {
  return requireEnv('ORG_ID');
}

/**
 * Build the auth header for the configured credential.
 *
 * Exactly one of `AUTH_TOKEN` / `API_KEY` must be set. Setting both is an error
 * rather than a preference, because the API key scheme resolves the organization
 * from the key itself while the bearer scheme resolves it from the route, and a
 * run that quietly used the wrong one would measure the wrong tenant.
 */
export function authHeaders() {
  const token = __ENV.AUTH_TOKEN;
  const apiKey = __ENV.API_KEY;

  const hasToken = Boolean(token && String(token).trim() !== '');
  const hasApiKey = Boolean(apiKey && String(apiKey).trim() !== '');

  if (hasToken && hasApiKey) {
    throw new Error(
      'Set exactly one of AUTH_TOKEN (Clerk bearer) or API_KEY (X-Api-Key). Both are set, ' +
        'and this script will not guess which principal the run is meant to measure.',
    );
  }

  if (hasToken) {
    return { Authorization: `Bearer ${token}` };
  }

  if (hasApiKey) {
    return { 'X-Api-Key': apiKey };
  }

  throw new Error(
    'No credential supplied. Set AUTH_TOKEN (a Clerk bearer token) or API_KEY (an avl_... ' +
      'key with the scopes the measured routes require). An unauthenticated run measures ' +
      'only the 401 path, which is exactly the defect this suite exists to close.',
  );
}

/** JSON content type plus the configured credential. */
export function jsonHeaders() {
  return Object.assign({ 'Content-Type': 'application/json' }, authHeaders());
}

/** Parse a JSON body without throwing the whole iteration away. */
export function parseJson(response) {
  try {
    return response.json();
  } catch (error) {
    return null;
  }
}
