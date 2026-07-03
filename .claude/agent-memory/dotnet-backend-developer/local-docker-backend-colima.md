---
name: local-docker-backend-colima
description: This dev machine runs Docker via Colima, not Docker Desktop — how to start the daemon
metadata:
  type: project
---

On this machine (macOS, checked 2026-07-03), there is no Docker Desktop app installed
— `open -a Docker` fails with "Unable to find application named 'Docker'". The Docker
CLI (`/opt/homebrew/bin/docker`, Homebrew-installed) talks to a **Colima** VM instead.
`docker context ls` shows contexts `colima` (active), `default`, and
`desktop-linux` — but no Desktop app backs `desktop-linux` here.

To start the daemon: `colima start` (takes ~5–10s). `docker info` / `docker context
ls` confirm readiness. Don't assume "Docker Desktop should be available" — check
`docker context ls` and try `colima start` before concluding Docker is unavailable.
