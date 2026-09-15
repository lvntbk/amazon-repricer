#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <backup-file>" >&2
    exit 1
fi

backup_file="$1"

if [ ! -f "$backup_file" ]; then
    echo "Backup file not found: $backup_file" >&2
    exit 1
fi

if [ ! -s "$backup_file" ]; then
    echo "Backup file is empty: $backup_file" >&2
    exit 1
fi

database="$(
    docker compose exec -T postgres \
        sh -ceu 'printf "%s" "$POSTGRES_DB"'
)"

restore_db="${database}_restore_test_$(date -u '+%Y%m%d%H%M%S')_$$"

cleanup()
{
    docker compose exec -T \
        -e RESTORE_DB="$restore_db" \
        postgres \
        sh -ceu '
            PGPASSWORD="$POSTGRES_PASSWORD" \
            dropdb \
                --if-exists \
                --force \
                --username="$POSTGRES_USER" \
                "$RESTORE_DB"
        ' >/dev/null 2>&1 || true
}

trap cleanup EXIT HUP INT TERM

docker compose exec -T \
    -e RESTORE_DB="$restore_db" \
    postgres \
    sh -ceu '
        PGPASSWORD="$POSTGRES_PASSWORD" \
        createdb \
            --username="$POSTGRES_USER" \
            "$RESTORE_DB"
    '

docker compose exec -T \
    -e RESTORE_DB="$restore_db" \
    postgres \
    sh -ceu '
        PGPASSWORD="$POSTGRES_PASSWORD" \
        pg_restore \
            --username="$POSTGRES_USER" \
            --dbname="$RESTORE_DB" \
            --no-owner \
            --no-acl \
            --exit-on-error
    ' < "$backup_file"

docker compose exec -T \
    -e RESTORE_DB="$restore_db" \
    postgres \
    sh -ceu '
        PGPASSWORD="$POSTGRES_PASSWORD" \
        psql \
            --username="$POSTGRES_USER" \
            --dbname="$RESTORE_DB" \
            --no-align \
            --tuples-only \
            --set=ON_ERROR_STOP=1 \
            --command="
                DO \$\$
                BEGIN
                    IF to_regclass(
                        '\''public.\"__EFMigrationsHistory\"'\''
                    ) IS NULL THEN
                        RAISE EXCEPTION
                            '\''Application migration history is missing'\'';
                    END IF;

                    IF to_regclass(
                        '\''public.\"__EFMigrationsHistory_Auth\"'\''
                    ) IS NULL THEN
                        RAISE EXCEPTION
                            '\''Auth migration history is missing'\'';
                    END IF;
                END
                \$\$;
            "
    '

printf 'Restore test succeeded: %s -> %s\n' \
    "$backup_file" \
    "$restore_db"
