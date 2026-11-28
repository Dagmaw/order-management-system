#!/bin/sh
# Retry only Maven bootstrap transport failures; never retry a build or its tests.
set -eu
log_file=$(mktemp)
trap 'rm -f "$log_file"' EXIT
attempt=1
while :; do
    if sh ./mvnw --version >"$log_file" 2>&1; then
        cat "$log_file"
        exit 0
    else
        status=$?
    fi
    cat "$log_file" >&2
    if [ "$attempt" -ge 4 ] || ! grep -Eiq '(^|[^0-9])(429|502|503|504)([^0-9]|$)|timed out|Connection reset|Temporary failure in name resolution' "$log_file"; then
        echo "Maven bootstrap failed; build and tests were not started." >&2
        exit "$status"
    fi
    case "$attempt" in
        1) delay=10 ;;
        2) delay=20 ;;
        3) delay=40 ;;
    esac
    echo "Transient Maven bootstrap failure; retrying in ${delay}s (attempt $((attempt + 1))/4)." >&2
    sleep "$delay"
    attempt=$((attempt + 1))
done
