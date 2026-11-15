#!/usr/bin/env bash
# Restores the newest backup into a scratch database and checks row counts/integrity. Never touches the live DB.
set -euo pipefail
cd "$(dirname "$0")/.."
ENV_FILE=${1:-env/dev.env}
dc() { docker compose --env-file "$ENV_FILE" "$@"; }
latest=$(dc exec -T backup sh -c 'ls -1t /backups/petstar-*.dump | head -1')
echo "restoring $latest into petstar_restore_test"
dc exec -T backup sh -c "sha256sum -c $latest.sha256"
dc exec -T backup sh -c "dropdb --if-exists petstar_restore_test && createdb petstar_restore_test && pg_restore --no-owner -d petstar_restore_test $latest"
for t in accounts server_memberships card_instances economy_ledger economy_wallets; do
  live=$(dc exec -T backup psql -tAc "select count(*) from $t")
  restored=$(dc exec -T backup psql -d petstar_restore_test -tAc "select count(*) from $t")
  echo "$t live=$live restored=$restored"
done
# ledger must reconcile with wallets in the restored copy
dc exec -T backup psql -d petstar_restore_test -tAc "select count(*) from economy_wallets w where w.balance <> coalesce((select sum(delta) from economy_ledger l where l.membership_id=w.membership_id and l.currency=w.currency),0)" | grep -qx 0 && echo "ledger reconciliation OK"
dc exec -T backup dropdb petstar_restore_test
