#!/bin/sh
set -eu
instance=${TUNNEL_INSTANCE_ID:?TUNNEL_INSTANCE_ID is required}
printf '%s' "$instance" | grep -Eq '^i-[0-9a-f]{17}$' || { echo 'Invalid helper instance ID.' >&2; exit 2; }
identity=$(aws sts get-caller-identity --region us-east-1 --query Arn --output text)
case "$identity" in
    arn:aws:sts::638500336727:assumed-role/order-management-dev-inventory-tunnel-operator/*) ;;
    *) echo 'The dedicated Inventory tunnel operator role is required.' >&2; exit 2 ;;
esac
# SSM listens on container loopback. Relay it to the Docker-published port;
# the Windows host publishes that port only on 127.0.0.1:443.
socat TCP4-LISTEN:9443,bind=0.0.0.0,reuseaddr,fork TCP4:127.0.0.1:8443 &
relay_pid=$!
session_pid=''
cleanup() {
    [ -z "$session_pid" ] || kill "$session_pid" 2>/dev/null || true
    kill "$relay_pid" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
aws ssm start-session --region us-east-1 --target "$instance" \
    --document-name order-management-dev-inventory-swagger-forward \
    --reason 'Private Inventory Swagger access' &
session_pid=$!
if wait "$session_pid"; then exit 0; else exit $?; fi
