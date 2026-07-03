---
name: aspire-dcp-quirks
description: Aspire's dcp orchestrator process holds ports open during a child crash-loop, and can outlive the AppHost when stopping
metadata:
  type: project
---

Aspire's underlying orchestrator, `dcp` (from the
`aspire.hosting.orchestration.osx-arm64` NuGet package, spawned as a child of
`dotnet run --project wavio.AppHost`), keeps a resource's listening socket open even
while the actual project process is crash-looping behind it. Symptom seen
2026-07-03: `curl http://localhost:5050/alive` hung indefinitely (no connection
refused, no response) while `core.WebApi` was crash-looping on a missing-schema
exception — `lsof -nP -iTCP:5050 -sTCP:LISTEN` showed `dcp`, not `core.WebApi`, bound
to the port. Always pass `curl -m <seconds>` (or `--max-time`) when polling
Aspire-hosted ports, or a bounded-timeout probe will hang for the full command
timeout instead of failing fast.

When stopping the AppHost: killing `dotnet run --project wavio.AppHost` and the
`wavio.AppHost` binary is not sufficient — the spawned service processes (the 5 wa-*
WebApis, `wavio.Gateway`, and their `dotnet run --project ... --no-build` wrapper
processes) and the `dcp` process itself can survive independently. Confirmed the
clean stop sequence: `pkill -f "dotnet run --project wavio.AppHost"`, then
`pkill -f dcp`, then `pkill -f "WaGateway.WebApi\|WaIngest.WebApi\|WaAdmin.WebApi\|WaBilling.WebApi\|WaIntel.WebApi\|wavio.Gateway\|wavio.AppHost"`
— verify with `ps aux | grep -iE "core.WebApi|Wa....WebApi|wavio.Gateway|wavio.AppHost|dcp"`.

Also: this machine has no `timeout`/`gtimeout` command. To bound a `dotnet run`
invocation, background it with `nohup ... &`, capture the PID, `sleep N`, then
`kill $PID` (and `pkill -f <dll name>` as a backstop, since `dotnet run` is a wrapper
around the actual apphost/exe process).
