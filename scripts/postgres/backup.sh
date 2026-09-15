#!/usr/bin/env sh
set -eu

umask 077

BACKUP_DIR="${BACKUP_DIR:-backups/postgres}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"

case "$BACKUP_RETENTION_DAYS" in
    ''|*[!0-9]*)
        echo "BACKUP_RETENTION_DAYS must be a non-negative integer." >&2
        exit 1
        ;;
esac

mkdir -p "$BACKUP_DIR"

database="$(
    docker compose exec -T postgres \
        sh -ceu 'printf "%s" "$POSTGRES_DB"'
)"

if [ -z "$database" ]; then
    echo "POSTGRES_DB is empty." >&2
    exit 1
fi

timestamp="$(date -u '+%Y%m%dT%H%M%SZ')"
backup_file="$BACKUP_DIR/${database}-${timestamp}.dump"
temporary_file="${backup_file}.tmp"

cleanup()
{
    rm -f "$temporary_file"
}

trap cleanup EXIT HUP INT TERM

docker compose exec -T postgres \
    sh -ceu '
        PGPASSWORD="$POSTGRES_PASSWORD" \
        pg_dump \
            --username="$POSTGRES_USER" \
            --dbname="$POSTGRES_DB" \
            --format=custom \
            --no-owner \
            --no-acl
    ' > "$temporary_file"

if [ ! -s "$temporary_file" ]; then
    echo "Backup file is empty." >&2
    exit 1
fi

docker compose exec -T postgres \
    pg_restore --list \
    < "$temporary_file" \
    > /dev/null

mv "$temporary_file" "$backup_file"
trap - EXIT HUP INT TERM

find "$BACKUP_DIR" \
    -type f \
    -name "${database}-*.dump" \
    -mtime "+$BACKUP_RETENTION_DAYS" \
    -delete

printf '%s\n' "$backup_file"
