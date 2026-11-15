#!/usr/bin/env bash
# Deploys / upgrades ONLY the petstar-prod compose project. Never touches other containers, volumes or Nginx.
#   1. preflight (read-only)   2. backup of the current database (if it exists)   3. build or pull the API image
#   4. up -d (migrations run on API start, additive only)   5. wait for /health/ready   6. print rollback hint
# Usage (on the VPS, from the repo checkout):  bash deployment/vps/deploy.sh [/opt/petstar/petstar.env]
set -euo pipefail
ENV_FILE="${1:-/opt/petstar/petstar.env}"
HERE="$(cd "$(dirname "$0")" && pwd)"
DC=(docker compose --env-file "$ENV_FILE" -f "$HERE/docker-compose.yml" -p petstar-prod)
PROFILE_ARGS=()
grep -Eq '^CLOUDFLARE_TUNNEL_TOKEN=.+' "$ENV_FILE" && PROFILE_ARGS=(--profile tunnel)

bash "$HERE/preflight.sh" "$ENV_FILE"
BACKUP_DIR=$(grep -E '^BACKUP_HOST_DIR=' "$ENV_FILE" | cut -d= -f2-); BACKUP_DIR=${BACKUP_DIR:-/opt/petstar/backups}
mkdir -p "$BACKUP_DIR"

prev_image=$(docker inspect --format '{{.Config.Image}}' petstar-prod-api-1 2>/dev/null || true)
if docker ps --format '{{.Names}}' | grep -qx petstar-prod-postgres-1; then
  echo "== Pre-upgrade backup"
  "${DC[@]}" exec -T backup /bin/sh /backup.sh
fi

image=$(grep -E '^PETSTAR_API_IMAGE=' "$ENV_FILE" | cut -d= -f2-)
if [ -z "$image" ] || [ "$image" = "petstar-api:local" ]; then
  echo "== Building API image on this host"
  "${DC[@]}" build api
else
  echo "== Pulling $image"
  "${DC[@]}" pull api
fi

echo "== Starting petstar-prod"
"${DC[@]}" "${PROFILE_ARGS[@]}" up -d --remove-orphans

port=$(grep -E '^API_HOST_PORT=' "$ENV_FILE" | cut -d= -f2); port=${port:-18080}
echo "== Waiting for http://127.0.0.1:${port}/health/ready"
for i in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${port}/health/ready" 2>/dev/null | grep -q Healthy; then
    echo "API healthy after $((i * 3))s"; "${DC[@]}" ps; exit 0
  fi
  sleep 3
done
echo "API did not become healthy. Logs:"; "${DC[@]}" logs --tail 80 api
[ -n "$prev_image" ] && echo "Rollback: set PETSTAR_API_IMAGE=$prev_image in $ENV_FILE and re-run deploy.sh (database backups: $BACKUP_DIR)"
exit 1
