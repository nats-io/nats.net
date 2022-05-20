#!/usr/bin/env bash

cd "$(dirname "$0")"
set -e

docker run --rm \
    -p 4222:4222 \
    -p 4280:4280 \
    -p 8222:8222 \
    -v "$(pwd)/nats-server.conf:/etc/nats/nats-server.conf" \
    nats:2.8.2-alpine \
    "$@"
