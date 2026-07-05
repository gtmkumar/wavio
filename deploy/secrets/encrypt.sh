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

# S5 guard: refuse to encrypt anything while .sops.yaml still points at the throwaway demo
# age recipient (its private half was generated, used once for the committed placeholder
# .enc files, and discarded — see deploy/secrets/README.md). Encrypting a REAL secret to it
# would make that secret unrecoverable by anyone. Replace the recipient in .sops.yaml first:
#   age-keygen -o ~/.config/sops/age/keys.txt   (keep this file OFFLINE and backed up)
# then swap every "age1yuw..." line in .sops.yaml for the "Public key:" line it prints.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOPS_CONFIG="${SCRIPT_DIR}/../../.sops.yaml"
DEMO_RECIPIENT="age1yuwpassvdgaxvqw9lvp2e3eqs7h9hemharsxayhkwsj3msw4pqds8dzz3s"

if [ -f "$SOPS_CONFIG" ] && grep -q "$DEMO_RECIPIENT" "$SOPS_CONFIG"; then
    echo "encrypt.sh: refusing to run — ${SOPS_CONFIG} still uses the throwaway demo age" >&2
    echo "recipient (${DEMO_RECIPIENT}). Its private key no longer exists, so anything" >&2
    echo "encrypted to it is permanently unrecoverable. Generate your own real age" >&2
    echo "recipient and replace it in .sops.yaml before encrypting real secrets — see" >&2
    echo "the comment at the top of .sops.yaml for the exact steps." >&2
    exit 1
fi

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
