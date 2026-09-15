#!/usr/bin/env sh
set -eu

umask 077

BACKUP_DIR="${BACKUP_DIR:-/backups}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"
BACKUP_INTERVAL_SECONDS="${BACKUP_INTERVAL_SECONDS:-86400}"

case "$BACKUP_RETENTION_DAYS" in
    ''|*[!0-9]*)
        echo "BACKUP_RETENTION_DAYS must be a non-negative integer." >&2
        exit 1
        ;;
esac

case "$BACKUP_INTERVAL_SECONDS" in
    ''|*[!0-9]*)
        echo "BACKUP_INTERVAL_SECONDS must be an integer." >&2
        exit 1
        ;;
esac

if [ "$BACKUP_INTERVAL_SECONDS" -lt 60 ]; then
    echo "BACKUP_INTERVAL_SECONDS must be at least 60." >&2
    exit 1
fi

mkdir -p "$BACKUP_DIR"

while :
do
    timestamp="$(date -u '+%Y%m%dT%H%M%SZ')"

    backup_file="$BACKUP_DIR/${PGDATABASE}-${timestamp}.dump"
    temporary_file="${backup_file}.tmp"

    cleanup()
    {
        rm -f "$temporary_file"
    }

    trap cleanup EXIT HUP INT TERM

    PGPASSWORD="$PGPASSWORD" \
    pg_dump \
        --host="$PGHOST" \
        --port="$PGPORT" \
        --username="$PGUSER" \
        --dbname="$PGDATABASE" \
        --format=custom \
        --no-owner \
        --no-acl \
        > "$temporary_file"

    if [ ! -s "$temporary_file" ]; then
        echo "Backup file is empty." >&2
        exit 1
    fi

    pg_restore --list "$temporary_file" > /dev/null

    mv "$temporary_file" "$backup_file"

    if [ "$(id -u)" -eq 0 ]; then
        backup_owner="$(stat -c '%u:%g' "$0")"
        chown "$backup_owner" "$backup_file"
    fi

    chmod 600 "$backup_file"
    trap - EXIT HUP INT TERM

    find "$BACKUP_DIR" \
        -type f \
        -name "${PGDATABASE}-*.dump" \
        -mtime "+$BACKUP_RETENTION_DAYS" \
        -exec rm -f {} \;

    printf 'Backup succeeded: %s\n' "$backup_file"

    sleep "$BACKUP_INTERVAL_SECONDS"
done
