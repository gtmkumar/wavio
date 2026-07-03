#!/usr/bin/env bash
# ufw firewall baseline for the Wavio VPS (issue #12).
#
# Only 80/443/SSH are ever reachable from the internet — Postgres and RabbitMQ
# must never be (docs/BUILD_PLAN.md "Firewall (ufw): only 80/443/SSH exposed").
# docker-compose.prod.yml already doesn't publish host ports for postgres or
# rabbitmq, so this is defense in depth, not the only control.
#
# Run ONCE, as root, on the actual VPS (requires ufw installed and real network
# capabilities this sandbox doesn't have — cannot be exercised in CI/locally,
# only shellchecked/reviewed). Idempotent: re-running is safe.
#
#   sudo ./deploy/vps/setup-ufw.sh
#
# IMPORTANT: run this over a console/KVM session if possible, or at minimum
# confirm your current SSH port is allowed BEFORE running `ufw enable` — a
# mistake here can lock you out with no way back in except a VPS console.

set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "setup-ufw.sh must be run as root (sudo)." >&2
    exit 1
fi

command -v ufw >/dev/null 2>&1 || {
    echo "ufw is not installed. On Debian/Ubuntu: apt-get update && apt-get install -y ufw" >&2
    exit 1
}

SSH_PORT="${SSH_PORT:-22}"

echo "== ufw baseline: default deny, allow SSH ($SSH_PORT/tcp) + 80/443/tcp =="

ufw default deny incoming
ufw default allow outgoing

ufw allow "${SSH_PORT}/tcp" comment 'SSH'
ufw allow 80/tcp comment 'HTTP (Caddy ACME + redirect)'
ufw allow 443/tcp comment 'HTTPS (Caddy)'
ufw allow 443/udp comment 'HTTP/3 (Caddy, QUIC)'

# Explicit deny, even though nothing publishes these ports to the host — belt
# and suspenders against a future docker-compose.prod.yml edit that accidentally
# adds a `ports:` mapping for one of these.
ufw deny 5432/tcp comment 'Postgres — never public'
ufw deny 5672/tcp comment 'RabbitMQ AMQP — never public'
ufw deny 15672/tcp comment 'RabbitMQ management UI — never public'

ufw --force enable
ufw status verbose
