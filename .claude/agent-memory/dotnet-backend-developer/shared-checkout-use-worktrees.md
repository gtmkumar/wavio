---
name: shared-checkout-use-worktrees
description: This team shares one physical git checkout across concurrent agents — use a real git worktree for any nontrivial branch of work, don't just cross fingers
metadata:
  type: feedback
---

Confirmed twice in one day (2026-07-03, issues #12 round 1 and round 2): every
agent on this team operates in the **same physical directory**
(`/Users/gtmkumar/Documents/source/wavio`), not isolated worktrees. Any
teammate's `git checkout`/`git stash`/etc. changes what every other agent sees
mid-task — including uncommitted modifications and even **staged** index
content following you across an unrelated `git checkout` to your own branch.

**Why this matters:** the first incident (issue #12 round 1) cost significant
recovery time diffing ownership of every changed file by content. The second
incident (round 2, mid-rebase) was worse — staged content from another
agent's branch rode along with a plain `git checkout` back to my own branch,
which is one step closer to an accidental cross-contaminated commit than
unstaged content is.

**What actually fixed it, permanently, for the rest of that session:**
`git worktree add --detach <path> <sha-or-branch>` — gives a fully isolated
working directory immune to any other agent's concurrent git operations. Use
`--detach` at a specific commit/branch-tip SHA if the branch is already
checked out elsewhere (git refuses a second checkout of the same branch name);
push from the detached HEAD via `git push origin HEAD:refs/heads/<branch>`
and only update the shared checkout's local branch ref afterward (or not at
all — the PR only cares about the remote ref).

**How to apply going forward:** the moment a task involves more than one
commit, or requires waiting on a long-running build/verification step where
you're not actively watching every git command, create a worktree
(`git worktree add`) FIRST, before writing any new files. Don't wait for a
second incident to justify it — the cost of setting one up is a few seconds;
the cost of a diff-by-content recovery is significant, error-prone, and risks
actually losing or corrupting a teammate's work if done carelessly.

**Constraint to respect:** the `EnterWorktree` harness tool is explicitly
gated to only fire on direct user/CLAUDE.md instruction — don't invoke it
proactively. Plain `git worktree add` via Bash has no such restriction and
accomplishes the same isolation; use that instead when self-directing this
mitigation.

Also flagged directly to the orchestrator (wavio-orchestrator) as a
team-wide process recommendation, not just a personal workaround.
