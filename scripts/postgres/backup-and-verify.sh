#!/usr/bin/env sh
set -eu

script_dir="$(
    CDPATH= cd -- "$(dirname -- "$0")" &&
    pwd
)"

repo_root="$(
    CDPATH= cd -- "$script_dir/../.." &&
    pwd
)"

cd "$repo_root"

backup_file="$("$script_dir/backup.sh")"

"$script_dir/restore-test.sh" "$backup_file"
