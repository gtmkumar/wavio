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

# These `ufw deny` lines do NOT protect a container whose port is actually
# published (`ports:` in docker-compose.prod.yml) — Docker manipulates
# iptables directly, inserting its own ACCEPT rules into the DOCKER/FORWARD
# chains ahead of ufw's INPUT-chain rules, so a published container port stays
# reachable from the internet regardless of what ufw says. This is a
# well-known Docker+ufw interaction, not a bug in ufw. Keep these as
# documentation of intent / defense against non-Docker services binding these
# ports directly on the host — the REAL mitigation for containers is:
#   1. docker-compose.prod.yml simply never publishes a `ports:` entry for
#      postgres or rabbitmq (current state — verify this stays true on every
#      change, since ufw cannot catch a regression here);
#   2. if a port ever does need to be reachable from the host but NOT the
#      internet, bind it to loopback only, e.g. `ports: ["127.0.0.1:5432:5432"]`,
#      not `["5432:5432"]`;
#   3. for a hard guarantee enforced at the firewall layer regardless of
#      compose config, add rules to the `DOCKER-USER` chain instead (the one
#      chain Docker itself never overwrites and evaluates before its own
#      permissive rules), e.g.:
#      iptables -I DOCKER-USER -p tcp --dport 5432 -j DROP
#      iptables -I DOCKER-USER -p tcp --dport 5672 -j DROP
#      iptables -I DOCKER-USER -p tcp --dport 15672 -j DROP
#      (not added here automatically — do this deliberately on the VPS if you
#      want defense-in-depth beyond "compose never publishes the port").
ufw deny 5432/tcp comment 'Postgres — non-Docker services only; see note above re: Docker+ufw'
ufw deny 5672/tcp comment 'RabbitMQ AMQP — non-Docker services only; see note above re: Docker+ufw'
ufw deny 15672/tcp comment 'RabbitMQ management UI — non-Docker services only; see note above re: Docker+ufw'

ufw --force enable
ufw status verbose
