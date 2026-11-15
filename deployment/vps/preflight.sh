#!/usr/bin/env bash
# READ-ONLY checks before the first Pet Star deployment on a shared VPS. Changes nothing.
# Usage: bash deployment/vps/preflight.sh [/opt/petstar/petstar.env]
set -uo pipefail
ENV_FILE="${1:-/opt/petstar/petstar.env}"
fail=0
ok()   { printf '  [ok]   %s\n' "$*"; }
warn() { printf '  [warn] %s\n' "$*"; }
bad()  { printf '  [FAIL] %s\n' "$*"; fail=1; }

echo "== Host"
. /etc/os-release 2>/dev/null && ok "OS: ${PRETTY_NAME:-unknown}"
command -v docker >/dev/null && ok "docker $(docker --version | awk '{print $3}' | tr -d ,)" || bad "docker not installed"
docker compose version >/dev/null 2>&1 && ok "docker compose $(docker compose version --short)" || bad "docker compose plugin missing"
mem_mb=$(awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo)
[ "${mem_mb:-0}" -ge 2200 ] && ok "available memory ${mem_mb} MiB" || warn "only ${mem_mb} MiB available; lower API_MEM/PG_MEM in petstar.env"
disk_gb=$(df -BG --output=avail /var/lib/docker 2>/dev/null | tail -1 | tr -dc 0-9)
[ "${disk_gb:-0}" -ge 10 ] && ok "free disk for docker ${disk_gb} GiB" || warn "free disk for docker ${disk_gb:-?} GiB (< 10)"

echo "== Name collisions with other projects (must be none)"
for n in $(docker ps -a --format '{{.Names}}' 2>/dev/null); do case "$n" in petstar-prod-*) warn "container $n already exists (re-deploy?)";; esac; done
docker network ls --format '{{.Name}}' 2>/dev/null | grep -qx petstar-prod-net && warn "network petstar-prod-net exists (re-deploy?)" || ok "network name petstar-prod-net is free"
docker volume ls --format '{{.Name}}' 2>/dev/null | grep -q '^petstar-prod_' && warn "petstar-prod volumes exist (data is kept on re-deploy)" || ok "volume names petstar-prod_* are free"

echo "== Ports"
port=$(grep -E '^API_HOST_PORT=' "$ENV_FILE" 2>/dev/null | cut -d= -f2); port=${port:-18080}
if ss -ltnH 2>/dev/null | awk '{print $4}' | grep -Eq "[:.]${port}\$"; then
  docker ps --format '{{.Names}} {{.Ports}}' | grep -q "petstar-prod-api.*${port}" && ok "port ${port} is used by petstar-prod-api itself" || bad "127.0.0.1:${port} is already in use by another service: pick another API_HOST_PORT"
else ok "127.0.0.1:${port} is free"; fi
ss -ltnH 2>/dev/null | awk '{print $4}' | grep -Eq '[:.](80|443)$' && ok "80/443 are owned by an existing web server (Pet Star never binds them)" || ok "80/443 not in use"

echo "== Secrets file"
if [ -f "$ENV_FILE" ]; then
  perm=$(stat -c %a "$ENV_FILE"); [ "$perm" = "600" ] || [ "$perm" = "400" ] && ok "$ENV_FILE mode $perm" || bad "$ENV_FILE mode $perm: run chmod 600"
  for k in POSTGRES_PASSWORD REDIS_PASSWORD JWT_SIGNING_KEY WEB_ORIGIN; do
    v=$(grep -E "^$k=" "$ENV_FILE" | cut -d= -f2-); [ -n "$v" ] && ok "$k is set" || bad "$k is empty"
  done
  jwt=$(grep -E '^JWT_SIGNING_KEY=' "$ENV_FILE" | cut -d= -f2-); [ ${#jwt} -ge 32 ] || bad "JWT_SIGNING_KEY shorter than 32 chars"
else bad "$ENV_FILE not found (copy petstar.env.example and fill it on the VPS)"; fi

echo "== Existing Nginx (only inspected)"
if command -v nginx >/dev/null; then
  ok "nginx present: Pet Star adds one new file only (sites-available/petstar-api.conf)"
  [ -e /etc/nginx/sites-enabled/petstar-api.conf ] && warn "petstar-api.conf already enabled"
else ok "no host nginx: use the Cloudflare Tunnel profile"; fi

echo
[ $fail -eq 0 ] && echo "PREFLIGHT PASSED" || { echo "PREFLIGHT FAILED: fix the items above before deploying"; exit 1; }
