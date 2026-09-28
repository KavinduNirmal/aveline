#!/usr/bin/env bash
#
# Expose the locally-running Aveline API so Meta can reach the WhatsApp webhook.
#
# Meta requires a public HTTPS endpoint for the webhook verification handshake, so a
# localhost URL will never work. This script starts a tunnel to the Docker-published API
# port and prints the exact callback URL to paste into the Meta App Dashboard.
#
# Usage:
#   scripts/whatsapp-local-tunnel.sh                    # pick provider automatically
#   scripts/whatsapp-local-tunnel.sh tailscale          # Tailscale Funnel (stable URL)
#   scripts/whatsapp-local-tunnel.sh cloudflared        # Cloudflare quick tunnel (random URL)
#   API_PORT=5091 ORG_ID=<guid> scripts/whatsapp-local-tunnel.sh
#
set -euo pipefail

API_PORT="${API_PORT:-5091}"
DB_CONTAINER="${DB_CONTAINER:-aveline_postgres}"
DB_USER="${DB_USER:-aveline}"
DB_NAME="${DB_NAME:-aveline}"
PROVIDER="${1:-auto}"

log()  { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[warn]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; exit 1; }

# --- Preflight: the API must actually be answering locally -------------------
log "Checking the API on http://127.0.0.1:${API_PORT} ..."
if ! curl -fsS -o /dev/null --max-time 5 "http://127.0.0.1:${API_PORT}/health"; then
  die "No healthy API on port ${API_PORT}. Start it first: docker compose up -d api"
fi
log "API is healthy."

# --- Discover the organization id (needed in the callback path) --------------
resolve_org_id() {
  if [[ -n "${ORG_ID:-}" ]]; then
    printf '%s' "$ORG_ID"
    return
  fi
  command -v docker >/dev/null 2>&1 || return 0
  docker inspect "$DB_CONTAINER" >/dev/null 2>&1 || return 0
  docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -tAc \
    'SELECT "Id" FROM "Organizations" ORDER BY "CreatedAt" LIMIT 1;' 2>/dev/null | tr -d '[:space:]'
}

ORG_ID="$(resolve_org_id || true)"
if [[ -z "$ORG_ID" ]]; then
  warn "Could not auto-detect an organization id."
  warn "Set ORG_ID=<guid> and re-run; the URL below will contain a placeholder."
fi

TUNNEL_URL=""

# --- Provider: Tailscale Funnel (stable hostname, TLS handled by Tailscale) ---
start_tailscale() {
  command -v tailscale >/dev/null 2>&1 || die "tailscale is not installed."

  local dns_name
  dns_name="$(tailscale status --json 2>/dev/null \
    | grep -m1 '"DNSName"' \
    | sed -E 's/.*"DNSName": *"([^"]+)".*/\1/' | sed 's/\.$//')"
  [[ -n "$dns_name" ]] || die "Could not read this machine's Tailscale DNS name. Is tailscaled running?"

  log "Enabling Tailscale Funnel for port ${API_PORT} ..."
  # `tailscale funnel` does NOT exit when Funnel is disabled for the tailnet: it prints an
  # enable link and then blocks while it polls. Without a timeout this script would hang
  # until the user clicks. Time it out and detect the disabled state from the output.
  rm -f /tmp/aveline-funnel.log
  timeout 20 tailscale funnel --bg --https=443 "http://127.0.0.1:${API_PORT}" \
    >/tmp/aveline-funnel.log 2>&1 || true

  if grep -q "not enabled on your tailnet" /tmp/aveline-funnel.log 2>/dev/null; then
    echo
    warn "Funnel is disabled for your tailnet. Open this link once, then re-run this script:"
    echo
    grep -o 'https://login\.tailscale\.com/f/funnel[^ ]*' /tmp/aveline-funnel.log || true
    echo
    die "Funnel not enabled yet."
  fi

  # Confirm the config actually landed rather than trusting the command's exit code.
  if ! timeout 15 tailscale serve status 2>/dev/null | grep -q "127.0.0.1:${API_PORT}"; then
    warn "Funnel config does not appear active. Output was:"
    sed 's/^/    /' /tmp/aveline-funnel.log || true
    die "Could not confirm the Funnel is serving port ${API_PORT}."
  fi

  log "Funnel active -> https://${dns_name}"
  TUNNEL_URL="https://${dns_name}"
}

# --- Provider: Cloudflare quick tunnel (random URL, no account needed) -------
start_cloudflared() {
  command -v cloudflared >/dev/null 2>&1 \
    || die "cloudflared is not installed. Install it with: sudo pacman -S cloudflared"

  # Keep state inside the workspace: /tmp is not shared between invocations here.
  local state_dir=".docker-tmp"
  mkdir -p "$state_dir"
  local log_file="${state_dir}/cloudflared.log"
  local url_file="${state_dir}/tunnel-url.txt"
  local pid_file="${state_dir}/cloudflared.pid"

  # Reuse a tunnel that is already running rather than stacking up processes.
  if [[ -f "$pid_file" ]] && kill -0 "$(cat "$pid_file")" 2>/dev/null && [[ -s "$url_file" ]]; then
    TUNNEL_URL="$(cat "$url_file")"
    log "Reusing running tunnel (pid $(cat "$pid_file")) -> ${TUNNEL_URL}"
    return
  fi

  log "Starting Cloudflare quick tunnel for port ${API_PORT} ..."
  : > "$log_file"
  # setsid detaches the tunnel from this script's session, so it survives after the
  # script (and the shell that invoked it) exits.
  setsid nohup cloudflared tunnel --url "http://127.0.0.1:${API_PORT}" --no-autoupdate \
    >"$log_file" 2>&1 < /dev/null &
  local pid=$!
  echo "$pid" > "$pid_file"

  local i
  for i in $(seq 1 60); do
    TUNNEL_URL="$(grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' "$log_file" | head -1 || true)"
    [[ -n "$TUNNEL_URL" ]] && break
    kill -0 "$pid" 2>/dev/null || { sed 's/^/    /' "$log_file" | tail -15; die "cloudflared exited early (see ${log_file})."; }
    sleep 0.5
  done

  if [[ -z "$TUNNEL_URL" ]]; then
    sed 's/^/    /' "$log_file" | tail -15
    die "Timed out waiting for a trycloudflare.com URL."
  fi

  echo "$TUNNEL_URL" > "$url_file"
  log "Tunnel is up (pid ${pid}). Logs: ${log_file}"
}


case "$PROVIDER" in
  tailscale)   start_tailscale ;;
  cloudflared) start_cloudflared ;;
  auto)
    if command -v tailscale >/dev/null 2>&1; then
      start_tailscale || { warn "Falling back to cloudflared ..."; start_cloudflared; }
    else
      start_cloudflared
    fi
    ;;
  *) die "Unknown provider '${PROVIDER}'. Use: tailscale | cloudflared | auto" ;;
esac

CALLBACK_URL="${TUNNEL_URL}/api/v1/webhooks/whatsapp/${ORG_ID:-<YOUR-ORG-ID>}"

# --- Prove the public URL reaches this API before touching Meta --------------
log "Verifying the tunnel reaches the API ..."
if curl -fsS -o /dev/null --max-time 15 "${TUNNEL_URL}/health"; then
  log "Public URL is reachable."
else
  warn "Public health check failed. The tunnel may still be warming up, or an ACL is blocking it."
fi

# A correct verify token returns 200 + the challenge; a wrong one returns 403.
# Either result proves the request reached the app (rather than 502/404 from the tunnel).
log "Probing the webhook path through the tunnel (403 = reached the app, token mismatch = expected) ..."
WEBHOOK_PROBE="$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 \
  "${CALLBACK_URL//<YOUR-ORG-ID>/00000000-0000-0000-0000-000000000000}?hub.mode=subscribe&hub.verify_token=probe&hub.challenge=1" || true)"
log "Webhook probe returned HTTP ${WEBHOOK_PROBE} (403 means the app answered correctly)."

cat <<EOF

────────────────────────────────────────────────────────────────────────
 Paste these into the Meta App Dashboard
────────────────────────────────────────────────────────────────────────

Callback URL:
  ${CALLBACK_URL}

Verify token:
  <the value you saved in Aveline Settings -> Integrations, or one you choose now>

 Where: Meta App Dashboard -> WhatsApp -> Configuration -> Webhook -> Edit.
 After saving, click "Verify and save", then subscribe to the "messages" field.

 Test the handshake from Meta's side once the token matches:
   curl "${CALLBACK_URL}?hub.mode=subscribe&hub.verify_token=<TOKEN>&hub.challenge=12345"
   -> expect "12345" back; 403 means the token does not match what is stored.

 Note: the callback path is per-organization. If you connected WhatsApp for a
 different organization, replace the id in the URL with that organization's id.

────────────────────────────────────────────────────────────────────────
$( [[ "$PROVIDER" == "cloudflared" || "$PROVIDER" == "auto" ]] && \
   echo " The quick-tunnel URL changes every restart, and Meta must re-verify each"
   echo " time it does. Prefer 'tailscale' for a stable hostname. Stop the tunnel"
   echo " with: pkill -f 'cloudflared tunnel'  |  tailscale funnel --https=443 off" )
────────────────────────────────────────────────────────────────────────
EOF
