#!/usr/bin/env bash
# Decrypts a SOPS-encrypted secrets file (issue #12). Run on the VPS, with the
# real age private key available via SOPS_AGE_KEY_FILE (or SOPS_AGE_KEY) — see
# deploy/secrets/README.md.
#
# Usage:
#   deploy/secrets/decrypt.sh deploy/secrets/prod.env.enc /opt/wavio/.env
#   deploy/secrets/decrypt.sh deploy/secrets/jwt-private-key.pem.enc /opt/wavio/secrets/jwt-private-key.pem
#
# Output is chmod 600 — it contains real credentials / a private key.

set -euo pipefail

if [ $# -ne 2 ]; then
    echo "Usage: $0 <encrypted-file> <output-path>" >&2
    exit 1
fi

INPUT="$1"
OUTPUT="$2"

BASENAME="${INPUT%.enc}"
case "$BASENAME" in
    *.env) FORMAT=dotenv ;;
    *) FORMAT=binary ;;
esac

umask 077
mkdir -p "$(dirname "$OUTPUT")"

# Decrypt to a temp file first and only move it into place on success — a failed
# `sops --decrypt ... > "$OUTPUT"` would otherwise still create $OUTPUT (shell
# redirection opens the file before the command runs), leaving a misleading
# empty file behind that looks like a successful decrypt at a glance.
TMP_OUTPUT="$(mktemp "${OUTPUT}.XXXXXX")"
trap 'rm -f "$TMP_OUTPUT"' EXIT

sops --decrypt --input-type "$FORMAT" --output-type "$FORMAT" "$INPUT" > "$TMP_OUTPUT"
mv "$TMP_OUTPUT" "$OUTPUT"
trap - EXIT
chmod 600 "$OUTPUT"
echo "decrypt.sh: wrote $OUTPUT (mode 600)"
