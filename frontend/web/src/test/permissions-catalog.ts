/**
 * Parses the backend authorization catalog so the frontend mirror can be checked against
 * it rather than transcribed by hand.
 *
 * Authority: `Aveline.Api/Authorization/Permissions.cs` (the permission set and the nine
 * canonical role grant sets) and `Aveline.Api/Authorization/Roles.cs` (the role strings).
 *
 * The parser is deliberately literal: it reads the C# source, not a generated artifact,
 * so a change to `Permissions.cs` changes these expectations with no regeneration step.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export interface BackendPermissionCatalog {
  /** Every string in `Permissions.All`, in declaration order. */
  readonly all: readonly string[]
  /** Role string (e.g. `org:boutique_staff`) to the permission strings it grants. */
  readonly roles: Readonly<Record<string, readonly string[]>>
  /** The permissions `admin` is deliberately denied, in declaration order. */
  readonly adminDenied: readonly string[]
}

const webRoot = fileURLToPath(new URL('../..', import.meta.url))
const apiRoot = resolve(webRoot, '../../Aveline.Api')

function readApiSource(file: string): string {
  return readFileSync(resolve(apiRoot, file), 'utf8')
}

/** Removes `///` doc comments and `//` line comments so identifier scans cannot match prose. */
function stripComments(source: string): string {
  return source
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('///'))
    .map((line) => line.replace(/\/\/.*$/, ''))
    .join('\n')
}

function parseStringConstants(source: string): Map<string, string> {
  const constants = new Map<string, string>()
  for (const match of source.matchAll(/public const string (\w+) = "([^"]+)";/g)) {
    constants.set(match[1], match[2])
  }
  return constants
}

function parseAllBlock(source: string, constants: Map<string, string>): string[] {
  const block =
    /IReadOnlySet<string>\s+All\s*=\s*new HashSet<string>\([^)]*\)\s*\{([\s\S]*?)\};/.exec(
      source,
    )
  if (!block) throw new Error('Permissions.cs: could not locate the All permission set')

  return block[1]
    .split(/[\s,]+/)
    .filter(Boolean)
    .map((identifier) => constants.get(identifier) ?? identifier)
}

/**
 * Reads the permissions `admin` is deliberately denied.
 *
 * The server names them in one array (`PermissionsDeniedToAdmin`) rather than special-casing
 * them at the `All.Where(...)` call site, precisely so this parse is possible: before that
 * array existed, the client mirror had to re-derive the exclusion from a hand-written
 * `.filter()` and the two could drift without any test noticing.
 */
function parseAdminDenied(source: string, constants: Map<string, string>): string[] {
  const block = /PermissionsDeniedToAdmin\s*=\s*\[([\s\S]*?)\];/.exec(source)
  if (!block) {
    throw new Error('Permissions.cs: could not locate PermissionsDeniedToAdmin')
  }

  return block[1]
    .split(/[\s,]+/)
    .filter(Boolean)
    .map((identifier) => constants.get(identifier) ?? identifier)
}

function parsePermissionNames(
  expression: string,
  constants: Map<string, string>,
): string[] {
  return expression
    .split(/[\s,]+/)
    .filter(Boolean)
    .map((identifier) => constants.get(identifier) ?? identifier)
}

function parseRoleGrants(
  source: string,
  constants: Map<string, string>,
  all: readonly string[],
  roleNames: Map<string, string>,
  adminDenied: readonly string[],
): Record<string, string[]> {
  const section = source.slice(source.indexOf('RolePermissions ='))
  if (section.length === 0) {
    throw new Error('Permissions.cs: could not locate RolePermissions')
  }

  const entries = section.matchAll(
    /\[Roles\.(\w+)\]\s*=\s*([\s\S]*?)(?=\n\s*\[Roles\.|\n\s*\};)/g,
  )

  const roles: Record<string, string[]> = {}
  for (const [, roleIdentifier, rawExpression] of entries) {
    const expression = rawExpression.trim().replace(/,$/, '')
    const role = roleNames.get(roleIdentifier) ?? roleIdentifier
    if (/All\.Where\([\s\S]*PermissionsDeniedToAdmin/.test(expression)) {
      // The named-denials shape. Before it existed the exclusion was an inline `!= X`, which
      // had to be re-derived by hand in the client mirror.
      roles[role] = all.filter((permission) => !adminDenied.includes(permission))
    } else if (expression === 'All') {
      roles[role] = [...all]
    } else if (expression.startsWith('Grant(')) {
      const inner = expression.slice(
        expression.indexOf('(') + 1,
        expression.lastIndexOf(')'),
      )
      roles[role] = parsePermissionNames(inner, constants)
    } else {
      const exclude = /All\.Where\([^)]*!=\s*(\w+)\)/.exec(expression)
      if (!exclude) {
        throw new Error(
          `Permissions.cs: unrecognised role grant expression for ${roleIdentifier}: ${expression}`,
        )
      }
      const excluded = constants.get(exclude[1]) ?? exclude[1]
      roles[role] = all.filter((permission) => permission !== excluded)
    }
  }
  return roles
}

/** Reads and parses the backend catalog from the repository source. */
export function parsePermissionsCatalog(): BackendPermissionCatalog {
  const permissionsSource = stripComments(readApiSource('Authorization/Permissions.cs'))
  const rolesSource = stripComments(readApiSource('Authorization/Roles.cs'))

  const constants = parseStringConstants(permissionsSource)
  const roleNames = parseStringConstants(rolesSource)
  const all = parseAllBlock(permissionsSource, constants)
  const adminDenied = parseAdminDenied(permissionsSource, constants)
  const roles = parseRoleGrants(permissionsSource, constants, all, roleNames, adminDenied)

  return { all, roles, adminDenied }
}

export interface BackendRolePolicy {
  /** The policy name the client mirrors, e.g. `AuditView`. */
  readonly policy: string
  /** The role strings, in source order. */
  readonly anyOf: readonly string[]
}

/**
 * Parses the four `RequireRole(...)` registrations in
 * `AuthorizationConfiguration.cs` so the client's `ROLE_POLICIES` overlay can be checked
 * against them rather than transcribed.
 */
export function parseRolePolicies(): BackendRolePolicy[] {
  const source = stripComments(readApiSource('Configurations/AuthorizationConfiguration.cs'))
  const roleNames = parseStringConstants(stripComments(readApiSource('Authorization/Roles.cs')))
  const policyNames = new Map<string, string>()
  for (const match of source.matchAll(/const string (\w+Policy) = "([^"]+)";/g)) {
    policyNames.set(match[1], match[2])
  }

  const policies: BackendRolePolicy[] = []

  // Split on the registration call rather than matching the lambda, so both shapes parse:
  //   options.AddPolicy(X, p => p.RequireRole(Roles.Owner, Roles.Admin));
  //   options.AddPolicy(X, p => { p.RequireRole(...); p.RequirePermission(...); });
  // (A9 adds the permission requirement alongside the role requirement in the block form.)
  for (const chunk of source.split(/options\.AddPolicy\(/).slice(1)) {
    const constant = /^\s*(\w+)\s*,/.exec(chunk)?.[1]
    if (constant === undefined) continue

    const rolesExpression = /RequireRole\(([^)]*)\)/.exec(chunk)?.[1]
    if (rolesExpression === undefined) continue

    const identifiers = rolesExpression
      .split(',')
      .map((value) => value.trim().replace(/^Roles\./, ''))
      .filter(Boolean)

    // `Associates`, `Managers` and `Owners` name role **sets** (`Roles.StaffAccess`,
    // `Roles.ManagementAccess`, `Roles.OwnershipAccess`) rather than individual roles. Those are
    // not the client's overlay; only the four explicit role lists are.
    if (identifiers.some((identifier) => !roleNames.has(identifier))) continue

    policies.push({
      policy: policyNames.get(constant) ?? constant,
      anyOf: identifiers.map((identifier) => roleNames.get(identifier) as string),
    })
  }
  return policies
}

