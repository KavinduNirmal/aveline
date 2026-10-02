#!/usr/bin/env bash
# =============================================================================
# Boot and drive the composed cross-surface stack for gap E1.
#
#   scripts/e2e-composed-stack.sh up     # docker postgres+redis, stub issuer, API, agent
#   scripts/e2e-composed-stack.sh seed   # the re-runnable seed (scripts/seed-e2e-composed-stack.sh)
#   scripts/e2e-composed-stack.sh web    # the real web app on :5173 (for the browser leg)
#   scripts/e2e-composed-stack.sh run    # up -> seed -> web -> Playwright, then `down`
#   scripts/e2e-composed-stack.sh down   # stop everything this script started (idempotent)
#
# CI splits these across jobs/steps: `up` leaves the background processes running and
# writes everything the next step needs to $E2E_STATE_DIR (idp.json, pids, stack.env,
# and per-process logs for upload on failure). Nothing here needs a TTY.
#
# What is real: PostgreSQL, the ASP.NET Core API process, its JWT validation pipeline,
# its membership resolution, its HTTP hop to the Python agent service, the agent's real
# LangGraph workflow, and the Redis event round-trip that persists the answer. The only
# non-real piece is the identity provider (a stub OIDC issuer), which cannot be the
# system under test because reaching a hosted Clerk instance needs a human sign-in.
#
# Environment (all optional)
# --------------------------
#   E2E_STATE_DIR          state/log directory (default <repo>/test-results/e2e-composed-stack)
#   E2E_API_PORT           API host port            (default 5091)
#   E2E_AGENT_PORT         agent host port          (default 8000)
#   E2E_WEB_PORT           web dev-server port      (default 5173)
#   E2E_CLERK_SUB          token subject / seeded clerk id (default user_e2e_cross_surface)
#   E2E_INTERNAL_TOKEN     shared API<->agent secret (default: value from .env, else generated)
#   E2E_WAIT_TIMEOUT       seconds to wait for each service (default 180)
#   POSTGRES_HOST/PORT/DB/USER/PASSWORD   host-side Postgres (defaults 127.0.0.1/5433/aveline/aveline)
#   E2E_DOWN_INFRA=1       `down` also stops the docker postgres/redis containers
#   E2E_SKIP_BUILD=1       `up` will not run `dotnet build`
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE_DIR="${E2E_STATE_DIR:-$REPO_ROOT/test-results/e2e-composed-stack}"
LOG_DIR="$STATE_DIR/logs"
PID_FILE="$STATE_DIR/pids.env"
IDP_STATE="$STATE_DIR/idp.json"
STACK_ENV="$STATE_DIR/stack.env"
SEED_STATE="$STATE_DIR/e2e-composed-stack.json"

API_PORT="${E2E_API_PORT:-5091}"
AGENT_PORT="${E2E_AGENT_PORT:-8000}"
WEB_PORT="${E2E_WEB_PORT:-5173}"
API_URL="http://127.0.0.1:$API_PORT"
AGENT_URL="http://127.0.0.1:$AGENT_PORT"
WEB_URL="http://127.0.0.1:$WEB_PORT"
WAIT_TIMEOUT="${E2E_WAIT_TIMEOUT:-180}"
E2E_CLERK_SUB="${E2E_CLERK_SUB:-user_e2e_cross_surface}"

POSTGRES_HOST="${POSTGRES_HOST:-127.0.0.1}"
POSTGRES_PORT="${POSTGRES_PORT:-5433}"
POSTGRES_DB="${POSTGRES_DB:-aveline}"
POSTGRES_USER="${POSTGRES_USER:-aveline}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-change-me}"

# The repo .env carries the local secrets (Postgres password, the internal token). It also
# carries *container-network* host names (`POSTGRES_HOST=postgres`, `REDIS_HOST=redis`) that
# are wrong from the host, so the host-side values this script needs are captured first and
# re-applied after sourcing; an operator's explicit env var still wins.
ENV_API_PORT="${E2E_API_PORT:-5091}"
ENV_AGENT_PORT="${E2E_AGENT_PORT:-8000}"
ENV_WEB_PORT="${E2E_WEB_PORT:-5173}"
ENV_PG_HOST="${POSTGRES_HOST:-127.0.0.1}"
ENV_PG_PORT="${POSTGRES_PORT:-5433}"
ENV_PG_DB="${POSTGRES_DB:-aveline}"
ENV_PG_USER="${POSTGRES_USER:-aveline}"
ENV_PG_PASSWORD="${POSTGRES_PASSWORD:-}"
ENV_REDIS_PORT="${REDIS_PORT:-6379}"
ENV_INTERNAL_TOKEN="${E2E_INTERNAL_TOKEN:-${INTERNAL_API_TOKEN:-}}"
ENV_CLERK_SUB="${E2E_CLERK_SUB:-}"

if [ -f "$REPO_ROOT/.env" ]; then
  # shellcheck disable=SC1091
  set -a; . "$REPO_ROOT/.env"; set +a
fi

API_PORT="$ENV_API_PORT"
AGENT_PORT="$ENV_AGENT_PORT"
WEB_PORT="$ENV_WEB_PORT"
API_URL="http://127.0.0.1:$API_PORT"
AGENT_URL="http://127.0.0.1:$AGENT_PORT"
WEB_URL="http://127.0.0.1:$WEB_PORT"
WAIT_TIMEOUT="${E2E_WAIT_TIMEOUT:-180}"
E2E_CLERK_SUB="${ENV_CLERK_SUB:-user_e2e_cross_surface}"

POSTGRES_HOST="$ENV_PG_HOST"
POSTGRES_PORT="$ENV_PG_PORT"
POSTGRES_DB="$ENV_PG_DB"
POSTGRES_USER="$ENV_PG_USER"
# `.env`'s password is the one docker-compose created the volume with, so it is the correct
# one to use when the operator did not state a host-side password explicitly.
POSTGRES_PASSWORD="${ENV_PG_PASSWORD:-${POSTGRES_PASSWORD:-change-me}}"
E2E_INTERNAL_TOKEN="${ENV_INTERNAL_TOKEN:-aveline-e2e-internal-token-$(python3 -c 'import secrets;print(secrets.token_hex(8))')}"

# `.env` names the *in-network* hosts (`postgres`, `redis`); neither is resolvable from the host,
# which is where this script runs. An operator who names any other host (a real database box) is
# left alone.
case "$POSTGRES_HOST" in
  postgres|redis|"") POSTGRES_HOST=127.0.0.1 ;;
esac
case "${REDIS_HOST:-}" in
  postgres|redis|"") REDIS_HOST=127.0.0.1 ;;
esac

# docker-compose.yml marks five values `${VAR:?required}`, and compose resolves every service's
# environment even when only postgres/redis are selected. CI has no repo `.env` (it is
# gitignored), so `up` is self-contained here: generate what is missing and EXPORT it, so the
# compose invocation, the API connection string and `down` all see the same values. A real
# `.env` still wins.
#
# GRAFANA_ADMIN_PASSWORD counts even though this script never starts Grafana: interpolation
# happens for the whole file, so `docker compose up postgres redis` fails outright without it.
ensure_compose_env() {
  if [ -z "${POSTGRES_PASSWORD:-}" ]; then
    POSTGRES_PASSWORD="$(python3 -c 'import secrets;print(secrets.token_urlsafe(24))')"
  fi
  export POSTGRES_PASSWORD
  if [ -z "${POSTGRES_EXPORTER_PASSWORD:-}" ]; then
    POSTGRES_EXPORTER_PASSWORD="$(python3 -c 'import secrets;print(secrets.token_urlsafe(24))')"
  fi
  export POSTGRES_EXPORTER_PASSWORD
  if [ -z "${METRICS_SCRAPE_TOKEN:-}" ]; then
    METRICS_SCRAPE_TOKEN="$(python3 -c 'import secrets;print(secrets.token_hex(32))')"
  fi
  export METRICS_SCRAPE_TOKEN
  if [ -z "${CREDENTIALS_ENCRYPTION_KEY:-}" ]; then
    CREDENTIALS_ENCRYPTION_KEY="$(python3 -c 'import base64,secrets;print(base64.b64encode(secrets.token_bytes(32)).decode())')"
  fi
  export CREDENTIALS_ENCRYPTION_KEY
  if [ -z "${GRAFANA_ADMIN_PASSWORD:-}" ]; then
    GRAFANA_ADMIN_PASSWORD="$(python3 -c 'import secrets;print(secrets.token_urlsafe(24))')"
  fi
  export GRAFANA_ADMIN_PASSWORD

  # The published ports must be exported too, or compose falls back to ITS defaults while this
  # script uses its own: compose maps `${POSTGRES_PORT:-5432}`, this script waits on 5433, so
  # an unexported POSTGRES_PORT starts a container on 5432 and then times out waiting for a
  # connection that will never come. Exporting what the script already resolved keeps the
  # container, the port wait and the connection strings pointed at the same socket.
  export POSTGRES_PORT
  export REDIS_PORT="${REDIS_PORT:-6379}"
}

log()  { printf '[e2e-stack] %s\n' "$*" >&2; }
fail() { printf '[e2e-stack] ERROR: %s\n' "$*" >&2; exit 1; }

mkdir -p "$STATE_DIR" "$LOG_DIR"

pid_of() { # pid_of <name> -> pid on stdout, empty when unknown
  [ -f "$PID_FILE" ] || return 0
  # shellcheck disable=SC1090
  ( set +u; . "$PID_FILE" 2>/dev/null; eval "printf '%s' \"\${$1:-}\"" ) | tr -d '\n' || true
}

is_running() { # is_running <pid>
  [ -n "${1:-}" ] && kill -0 "$1" 2>/dev/null
}

tail_log() { # tail_log <file> [lines]
  local file="$1" lines="${2:-40}"
  if [ -f "$file" ]; then
    printf -- '--- last %s lines of %s ---\n' "$lines" "$file" >&2
    tail -n "$lines" "$file" >&2 || true
  fi
}

record_pid() { # record_pid <VARNAME> <pid>
  touch "$PID_FILE"
  if grep -q "^$1=" "$PID_FILE" 2>/dev/null; then
    python3 - "$PID_FILE" "$1" "$2" <<'PY'
import sys
path, name, value = sys.argv[1], sys.argv[2], sys.argv[3]
lines = [l for l in open(path).read().splitlines() if not l.startswith(name + "=")]
lines.append(f"{name}={value}")
open(path, "w").write("\n".join(lines) + "\n")
PY
  else
    printf '%s=%s\n' "$1" "$2" >> "$PID_FILE"
  fi
}

wait_http() { # wait_http <name> <url> <logfile> [accepted-codes]
  local name="$1" url="$2" logfile="$3" accepted="${4:-200}"
  local deadline=$(( $(date +%s) + WAIT_TIMEOUT )) code=""
  while [ "$(date +%s)" -lt "$deadline" ]; do
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 "$url" 2>/dev/null || true)"
    case ",$accepted," in
      *",$code,"*) log "$name ready after $(( WAIT_TIMEOUT - (deadline - $(date +%s)) ))s ($url -> $code)"; return 0 ;;
    esac
    sleep 2
  done
  tail_log "$logfile"
  fail "$name did not answer $url with one of [$accepted] within ${WAIT_TIMEOUT}s (last code: ${code:-none})."
}

wait_tcp() { # wait_tcp <name> <host> <port> <logfile>
  local name="$1" host="$2" port="$3" logfile="$4"
  local deadline=$(( $(date +%s) + WAIT_TIMEOUT ))
  while [ "$(date +%s)" -lt "$deadline" ]; do
    if python3 -c 'import socket,sys;s=socket.socket();s.settimeout(1);sys.exit(0 if s.connect_ex((sys.argv[1],int(sys.argv[2])))==0 else 1)' "$host" "$port" 2>/dev/null; then
      log "$name reachable on $host:$port"; return 0
    fi
    sleep 1
  done
  tail_log "$logfile"
  fail "$name never accepted a TCP connection on $host:$port within ${WAIT_TIMEOUT}s."
}

# -----------------------------------------------------------------------------
cmd_up() {
  mkdir -p "$STATE_DIR" "$LOG_DIR"
  # Clear only what this subcommand owns. A previous `seed` may have written
  # `e2e-composed-stack.json` and `e2e-api-keys.json` into the same directory, and CI runs `up`
  # and `seed` as separate steps that must share it.
  rm -f "$PID_FILE"

  # 1. dockerised infrastructure -------------------------------------------------
  ensure_compose_env
  log "starting docker postgres + redis"
  docker compose up -d postgres redis >"$LOG_DIR/docker-compose-up.log" 2>&1 \
    || { tail_log "$LOG_DIR/docker-compose-up.log"; fail "docker compose up postgres redis failed."; }

  local deadline=$(( $(date +%s) + WAIT_TIMEOUT ))
  local pg_ready=0
  while [ "$(date +%s)" -lt "$deadline" ]; do
    if (exec 3<>"/dev/tcp/$POSTGRES_HOST/$POSTGRES_PORT") 2>/dev/null; then pg_ready=1; break; fi
    sleep 1
  done
  [ "$pg_ready" = 1 ] || fail "PostgreSQL never accepted a connection on $POSTGRES_HOST:$POSTGRES_PORT."
  log "postgres ready on $POSTGRES_HOST:$POSTGRES_PORT"

  # 2. stub OIDC issuer ----------------------------------------------------------
  rm -f "$IDP_STATE"
  python3 "$REPO_ROOT/tests/e2e/fixtures/stub_oidc_issuer.py" \
    --port 0 --host 127.0.0.1 --sub "$E2E_CLERK_SUB" --out "$IDP_STATE" \
    >"$LOG_DIR/stub-issuer.log" 2>&1 &
  local idp_pid=$!
  record_pid IDP_PID "$idp_pid"

  local i
  for i in $(seq 1 80); do [ -f "$IDP_STATE" ] && break; sleep 0.25; done
  [ -f "$IDP_STATE" ] || { tail_log "$LOG_DIR/stub-issuer.log"; fail "the stub issuer never wrote $IDP_STATE."; }
  local idp_url
  idp_url="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["issuer"])' "$IDP_STATE")"
  wait_http "stub issuer" "$idp_url/health" "$LOG_DIR/stub-issuer.log"

  # 3. the API, built if needed and pointed at the stub issuer --------------------
  local api_dll="$REPO_ROOT/Aveline.Api/bin/Debug/net10.0/Aveline.Api.dll"
  if [ ! -f "$api_dll" ] && [ ! -f "$REPO_ROOT/Aveline.Api/bin/Debug/net9.0/Aveline.Api.dll" ]; then
    log "no built API binary found; running dotnet build (first run is slow)"
  fi
  if [ "${E2E_SKIP_BUILD:-0}" != "1" ]; then
    log "building Aveline.Api"
    dotnet build "$REPO_ROOT/Aveline.Api/Aveline.Api.csproj" --nologo -v q \
      >"$LOG_DIR/dotnet-build.log" 2>&1 \
      || { tail_log "$LOG_DIR/dotnet-build.log"; fail "dotnet build Aveline.Api failed."; }
  fi

  # Media__Provider=database: the composed stack has no Cloudinary credential and the walk
  # attaches nothing. The shell may inherit .env's `Media__Provider=cloudinary`, which makes
  # the API refuse to boot without Media__SigningKey; this override removes that dependency.
  log "starting the API on $API_URL (authority $idp_url)"
  # NOTE: no comment may sit inside the backslash-continued assignment list below. A `#` line
  # there terminates the command and every assignment after it becomes its own command, so the
  # app is started with none of them (this bit once: Clerk__Authority was silently dropped and
  # every token failed with SecurityTokenInvalidIssuerException).
  (
    cd "$REPO_ROOT"
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="$API_URL" \
    Clerk__Authority="$idp_url" \
    Clerk__RequireHttpsMetadata=false \
    ConnectionStrings__DefaultConnection="Host=$POSTGRES_HOST;Port=$POSTGRES_PORT;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD" \
    Redis__ConnectionString="127.0.0.1:$ENV_REDIS_PORT" \
    AgentService__BaseUrl="$AGENT_URL" \
    AgentService__InternalToken="$E2E_INTERNAL_TOKEN" \
    Media__Provider=database \
    Embeddings__ApiKey="${EMBEDDINGS_API_KEY:-}" \
    Embeddings__BaseUrl="${EMBEDDINGS_BASE_URL:-https://api.openai.com/v1}" \
    Embeddings__Model="${EMBEDDINGS_MODEL:-text-embedding-3-small}" \
    Cors__AllowedOrigins__0="$WEB_URL" \
    Cors__AllowedOrigins__1="http://localhost:$WEB_PORT" \
    nohup dotnet run --project Aveline.Api --no-build --urls "$API_URL" \
      >"$LOG_DIR/api.log" 2>&1 &
    echo $! >"$STATE_DIR/api.pid"
  )
  record_pid API_PID "$(cat "$STATE_DIR/api.pid")"
  wait_http "API" "$API_URL/health/live" "$LOG_DIR/api.log"

  # 4. the agent service ---------------------------------------------------------
  # Prefer the checked-out virtualenv; fall back to the interpreter on PATH, which is how CI
  # runs the agent (its requirements are installed into the runner's Python, not a venv).
  local agent_python="$REPO_ROOT/agent-service/.venv/bin/python"
  if [ ! -x "$agent_python" ]; then
    agent_python="$(command -v python3 || true)"
    [ -n "$agent_python" ] || fail "no python3 on PATH and no agent-service/.venv; cannot start the agent."
    log "agent-service/.venv not found; trying $agent_python"
  fi
  "$agent_python" -c 'import uvicorn' 2>/dev/null \
    || fail "uvicorn is not importable from $agent_python. Install the agent requirements (agent-service/requirements.txt) or create agent-service/.venv."

  log "starting the agent service with $agent_python on $AGENT_URL (AGENT_LLM_ENABLED=false)"
  (
    cd "$REPO_ROOT/agent-service"
    INTERNAL_API_TOKEN="$E2E_INTERNAL_TOKEN" \
    AGENT_LLM_ENABLED=false \
    API_BASE_URL="$API_URL" \
    DATABASE_URL="postgresql+asyncpg://$POSTGRES_USER:$POSTGRES_PASSWORD@$POSTGRES_HOST:$POSTGRES_PORT/$POSTGRES_DB" \
    REDIS_URL="redis://127.0.0.1:$ENV_REDIS_PORT/0" \
    nohup "$agent_python" -m uvicorn app.main:app --host 127.0.0.1 --port "$AGENT_PORT" \
      >"$LOG_DIR/agent.log" 2>&1 &
    echo $! >"$STATE_DIR/agent.pid"
  )
  record_pid AGENT_PID "$(cat "$STATE_DIR/agent.pid")"
  wait_http "agent" "$AGENT_URL/health" "$LOG_DIR/agent.log"

  # Non-secret values only: `down` re-sources this, and a generated password that only lived in
  # this shell would otherwise be lost to the next subcommand's connection string.
  cat >"$STACK_ENV" <<EOF
# Generated by scripts/e2e-composed-stack.sh up -- source it, or read the keys directly.
E2E_API_BASE_URL=$API_URL
E2E_IDP_BASE_URL=$idp_url
E2E_AGENT_BASE_URL=$AGENT_URL
E2E_WEB_BASE_URL=$WEB_URL
E2E_CLERK_SUB=$E2E_CLERK_SUB
E2E_STATE_DIR=$STATE_DIR
E2E_AGENT_PYTHON=$agent_python
POSTGRES_HOST=$POSTGRES_HOST
POSTGRES_PORT=$POSTGRES_PORT
POSTGRES_DB=$POSTGRES_DB
POSTGRES_USER=$POSTGRES_USER
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
POSTGRES_EXPORTER_PASSWORD=$POSTGRES_EXPORTER_PASSWORD
METRICS_SCRAPE_TOKEN=$METRICS_SCRAPE_TOKEN
CREDENTIALS_ENCRYPTION_KEY=$CREDENTIALS_ENCRYPTION_KEY
EOF
  chmod 600 "$STACK_ENV"
  log "stack up. issuer=$idp_url api=$API_URL agent=$AGENT_URL"
  log "state: $STACK_ENV"
}

# -----------------------------------------------------------------------------
cmd_seed() {
  [ -f "$STACK_ENV" ] && { set -a; . "$STACK_ENV"; set +a; }
  E2E_STATE_DIR="$STATE_DIR" \
  E2E_API_BASE_URL="${E2E_API_BASE_URL:-$API_URL}" \
  E2E_CLERK_SUB="$E2E_CLERK_SUB" \
  POSTGRES_HOST="${POSTGRES_HOST:-$ENV_PG_HOST}" \
  POSTGRES_PORT="${POSTGRES_PORT:-$ENV_PG_PORT}" \
  POSTGRES_DB="${POSTGRES_DB:-$ENV_PG_DB}" \
  POSTGRES_USER="${POSTGRES_USER:-$ENV_PG_USER}" \
  POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-change-me}" \
  bash "$REPO_ROOT/scripts/seed-e2e-composed-stack.sh"
}

# -----------------------------------------------------------------------------
cmd_web() {
  [ -f "$STACK_ENV" ] && { set -a; . "$STACK_ENV"; set +a; }
  mkdir -p "$LOG_DIR"
  local pid
  pid="$(pid_of WEB_PID || true)"
  if is_running "$pid"; then log "web dev server already running (pid=$pid)"; return 0; fi

  log "starting the real web app on ${E2E_WEB_BASE_URL:-$WEB_URL}"
  (
    cd "$REPO_ROOT/frontend/web"
    # Polling, not inotify: `bun run dev` is passed --strictPort, so a watcher that dies leaves
    # the port unbound and the wait below times out with a misleading message. Vite's chokidar
    # watcher can hit EMFILE on a watched tree this size on a loaded machine (observed here),
    # and this server only has to serve one browser walk.
    CHOKIDAR_USEPOLLING=true CHOKIDAR_INTERVAL=1000 \
      nohup bun run dev -- --port "$WEB_PORT" --strictPort --host 127.0.0.1 >"$LOG_DIR/web.log" 2>&1 &
    echo $! >"$STATE_DIR/web.pid"
  )
  record_pid WEB_PID "$(cat "$STATE_DIR/web.pid")"
  wait_http "web" "${E2E_WEB_BASE_URL:-$WEB_URL}/" "$LOG_DIR/web.log" "200,304"
}

# -----------------------------------------------------------------------------
cmd_run() {
  cmd_up
  cmd_seed
  cmd_web

  [ -f "$STACK_ENV" ] && { set -a; . "$STACK_ENV"; set +a; }
  local spec="${E2E_SPEC:-tests/e2e/integration/salon-cross-surface.spec.ts}"
  log "running Playwright: $spec"
  local status=0
  (
    cd "$REPO_ROOT/frontend/web"
    E2E_BASE_URL="${E2E_WEB_BASE_URL:-$WEB_URL}" \
    E2E_API_BASE_URL="${E2E_API_BASE_URL:-$API_URL}" \
    E2E_IDP_BASE_URL="$E2E_IDP_BASE_URL" \
    E2E_STATE_FILE="$SEED_STATE" \
    E2E_CLERK_SUB="$E2E_CLERK_SUB" \
    bun run test:e2e -- "$spec"
  ) || status=$?
  log "Playwright exited with $status; leaving the stack running (use 'down' to stop it)."
  return $status
}

# -----------------------------------------------------------------------------
cmd_down() {
  local name pid
  for name in WEB_PID AGENT_PID API_PID IDP_PID; do
    pid="$(pid_of "$name" || true)"
    if is_running "$pid"; then
      log "stopping $name (pid=$pid)"
      # Kill the whole process group where possible: `dotnet run` forks the real app.
      kill -TERM "$pid" 2>/dev/null || true
      for _ in $(seq 1 20); do is_running "$pid" || break; sleep 0.5; done
      is_running "$pid" && kill -KILL "$pid" 2>/dev/null || true
    fi
  done
  # uvicorn/dotnet may leave children behind; match the exact command lines this script starts.
  pkill -f "uvicorn app.main:app --host 127.0.0.1 --port $AGENT_PORT" 2>/dev/null || true
  pkill -f "Aveline.Api --no-build --urls $API_URL" 2>/dev/null || true
  pkill -f "stub_oidc_issuer.py --port 0" 2>/dev/null || true
  pkill -f "vite --port $WEB_PORT" 2>/dev/null || true
  # `dotnet run` re-execs the built app under its own process group, so also clear the ports
  # this script owns. Only ever reached from `down`, so it cannot kill an unrelated dev server
  # that was never part of this stack.
  if command -v fuser >/dev/null 2>&1; then
    fuser -k -TERM "$API_PORT/tcp" 2>/dev/null || true
    fuser -k -TERM "$AGENT_PORT/tcp" 2>/dev/null || true
    fuser -k -TERM "$WEB_PORT/tcp" 2>/dev/null || true
  fi
  rm -f "$PID_FILE" "$STATE_DIR/api.pid" "$STATE_DIR/agent.pid" "$STATE_DIR/web.pid"
  if [ "${E2E_DOWN_INFRA:-0}" = "1" ]; then
    # The compose file's `${VAR:?}` values must exist for even `stop` to parse it; generate any
    # that are missing so teardown never fails on a machine with no `.env`.
    ensure_compose_env
    log "stopping docker postgres + redis"
    docker compose stop postgres redis >/dev/null 2>&1 || true
  fi
  log "stack down"
}

usage() {
  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 1
}

case "${1:-}" in
  up)   cmd_up ;;
  seed) cmd_seed ;;
  web)  cmd_web ;;
  run)  cmd_run ;;
  down) cmd_down ;;
  *)    usage ;;
esac
