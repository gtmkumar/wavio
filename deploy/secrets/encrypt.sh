#!/usr/bin/env bash
# Encrypts a plaintext secrets file for commit (issue #12).
#
# Usage:
#   deploy/secrets/encrypt.sh deploy/secrets/prod.env
#   deploy/secrets/encrypt.sh deploy/secrets/jwt-private-key.pem
#
# Produces <file>.enc next to the input — THAT file is what gets committed,
# never the plaintext input. .sops.yaml's creation_rules (matched against the
# input path given here) decide which age recipient protects it.
#
# --input-type/--output-type are passed explicitly rather than relying on
# extension auto-detection, since the *encrypted* output has a ".enc" suffix
# sops wouldn't otherwise recognize.

set -euo pipefail

if [ $# -ne 1 ]; then
    echo "Usage: $0 <plaintext-file>" >&2
    exit 1
fi

INPUT="$1"
if [ ! -f "$INPUT" ]; then
    echo "encrypt.sh: $INPUT not found" >&2
    exit 1
fi

case "$INPUT" in
    *.env) FORMAT=dotenv ;;
    *) FORMAT=binary ;;
esac

sops --encrypt --input-type "$FORMAT" --output-type "$FORMAT" "$INPUT" > "${INPUT}.enc"
echo "encrypt.sh: wrote ${INPUT}.enc"
