- [Issue #9 dev infra: what was built](issue-9-dev-infra.md) — docker-compose.dev.yml, Postgres/RabbitMQ bootstrap, file locations
- [Core identity seeder needed schema — RESOLVED by #10](core-identity-seeder-needs-schema.md) — history of the crash-loop and its 2026-07-03 resolution
- [Local machine uses Colima, not Docker Desktop](local-docker-backend-colima.md) — how to start the docker daemon on this dev machine
- [Aspire dcp quirks when probing/killing services](aspire-dcp-quirks.md) — hung curl probes and orphaned processes when stopping the AppHost
- [Issue #10 .NET wiring: what changed](issue-10-dotnet-wiring.md) — DbMigrator project, EF remaps, RLS GUC rename, full verification steps
- [Issue #10 design decisions](decisions.md) — why a custom runner not DbUp, the Npgsql regclass cast bug, remap rationale
<<<<<<< HEAD
- [Current status across #9/#10](status.md) — quick pointer to what's done and what's next
- [Handoff after #10](handoff.md) — what issue #11 (CI) and #12 (VPS deploy) need to pick up
=======
- [Current status across #9/#10/#11](status.md) — quick pointer to what's done and what's next
- [Handoff after #10/#11](handoff.md) — what issue #12 (VPS deploy) needs to pick up
- [Issue #11 CI pipeline: what was built](issue-11-ci-pipeline.md) — workflow jobs, FK-audit gate design, every gate's local verification incl. negative cases
>>>>>>> origin/main
