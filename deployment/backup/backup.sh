#!/bin/sh
# Compressed logical backup (custom format) + retention. Restores are tested by deployment/scripts/restore-test.sh.
set -eu
ts=$(date -u +%Y%m%dT%H%M%SZ)
dir="${BACKUP_DIR:-/backups}"
out="$dir/petstar-$ts.dump"
pg_dump --format=custom --compress=9 --no-owner --file="$out.partial"
mv "$out.partial" "$out"
sha256sum "$out" > "$out.sha256"
find "$dir" -name 'petstar-*.dump*' -mtime +"${BACKUP_RETENTION_DAYS:-14}" -delete
echo "backup ok: $out ($(du -h "$out" | cut -f1))"
