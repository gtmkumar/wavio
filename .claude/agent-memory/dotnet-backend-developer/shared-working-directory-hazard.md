---
name: shared-working-directory-hazard
description: Parallel agents can share the same physical git working directory and clobber each other's checked-out branch mid-task — protect uncommitted work with an isolated worktree immediately
metadata:
  type: project
---

Discovered 2026-07-03 while implementing issue #13 (wa-ingest-svc) in parallel
with another agent working issue #12 (VPS deploy): both agents were operating
in the SAME physical directory (`/Users/gtmkumar/Documents/source/wavio`), not
separate worktrees. Mid-task, `git branch --show-current` unexpectedly showed
a different branch than the one I had checked out, and `git status` showed my
own uncommitted new/modified files sitting on top of the OTHER agent's branch.
`git reflog` confirmed a rapid sequence of `checkout`/`reset` operations
switching between `feature/10-db-migrations`, `feature/12-vps-deploy`, and
`feature/13-ingest-webhooks` — not something I initiated. A `git fsck` later
turned up a dangling merge commit the other process had created specifically
to preserve my uncommitted WIP before rebasing something else out from under
it (message: "not-mine: ingest agent wip, stashing only to allow my rebase") —
so the other process WAS trying to be careful, but the collision still nearly
cost real work, and my first `git commit` in the shared directory got silently
undone by a subsequent `reset` before I could push it.

**Why this happens**: nothing in the harness isolates one agent's `cd`'d shell
session from another's when both are pointed at the same clone. Branch refs
and the working tree are both mutable shared state.

**How to apply**: the moment you need to create a branch and start committing
substantial work in a repo where a teammate/another agent might be
concurrently active in the SAME directory (check `docker ps`/running
processes for signs of other agents, or just assume it given multi-agent
orchestration), immediately do the real work in an isolated `git worktree`
(`git worktree add <scratch-path> <branch>`) instead of the shared directory —
this is exactly what the other agent was already doing (they had their own
worktree at a fixed path). Branch refs and commits made from a worktree are
visible repo-wide immediately, so this costs nothing and fully removes the
collision risk. Commit and `git push` early and often from the isolated
worktree — a pushed commit is safe from any local reset/rebase happening
elsewhere; an uncommitted or even just-committed-but-unpushed change in a
shared directory is not. See [[issue-13-ingest]] for what was actually built
under this constraint.
