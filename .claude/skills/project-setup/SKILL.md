---
name: project-setup
description: Set up a production-grade project scaffold tailored to the user's stack and team — language configs, quality gates, skills/ reference docs, CLAUDE.md, and pre-commit hooks. Use when the user runs /project-setup or asks to set up, scaffold, or bootstrap a new project.
---

# Project Setup Skill — Top 1% Engineering Standards

## Trigger: `/project-setup` or "set up my project"

This skill sets up a production-grade project scaffold tailored to your stack and team.
It works in two modes — **express** (solo/early-stage, ~15 min) and **full** (team/production, ~45 min).

---

# AGENT BEHAVIOR RULES

Before writing a single file, you must:

1. Run the stack detection commands (Step 1) — silently
2. Ask ALL questions in the questionnaire (Step 2) — wait for answers before proceeding
3. Confirm the chosen mode and plan with the user
4. Execute setup steps, skipping any that don't apply to the confirmed stack

Never:

- Create files until the questionnaire is complete
- Install tools without confirming the user wants them
- Generate language configs for a language not in the confirmed stack
- Skip the questionnaire because the stack "seems obvious"
- Report "setup complete" before the final validation checklist passes

---

# PART I — INTERACTIVE SETUP

## Step 1: Detect Existing Stack

Run silently — do not report output, just note what exists:

```bash
ls -la
cat package.json 2>/dev/null || true
cat pyproject.toml 2>/dev/null || true
find . -maxdepth 1 \( -name "*.sln" -o -name "*.slnx" \) 2>/dev/null  # glob-safe in zsh too
find . -maxdepth 3 -name "*.csproj" -not -path "*/node_modules/*" 2>/dev/null | head -20
cat global.json 2>/dev/null || true
cat Directory.Build.props 2>/dev/null || true
```

Do not act on findings yet. The questionnaire determines what to build.

---

## Step 2: Questionnaire — Ask All Before Proceeding

Present these questions conversationally — do not dump all at once.
Group A first, wait for answers, then B, then C and D together. All answers are needed before Step 3.

### A. Project Identity

1. **What is this project?** One sentence: what it builds and who it's for.
2. **Primary language / framework?**
   - TypeScript / JavaScript — and if so, which framework: Next.js, Express, Node, React, Vue, Svelte, other?
   - Python — and if so: FastAPI, Django, Flask, scripts/data, other?
   - C# / .NET — and if so: ASP.NET Core Web API (Clean Architecture), Minimal API, Worker Service, Blazor, .NET Aspire distributed app, other?
   - Multiple languages (which ones)?
3. **Project type?**
   - Full-stack web app (frontend + API)
   - Backend API / microservice only
   - Frontend only (SPA or static site)
   - CLI tool
   - Library / SDK
   - Data pipeline / scripts
   - Mobile app backend

### B. Structure and Team

4. **Single service or multiple?**
   - Single repo / single service
   - Monorepo (multiple services, one repo)
   - Polyrepo (multiple services, separate repos — setting up this one only)
5. **Team size?**
   - Solo (just me)
   - Small (2–5 engineers)
   - Growing (6+ engineers)

### C. Infrastructure

6. **Deployment target?**
   - Vercel / Netlify
   - AWS (EC2, ECS, Lambda, etc.)
   - GCP / Azure
   - Railway / Render / Fly.io
   - Self-hosted / on-prem
   - Not decided yet
7. **Database?** (skip if no database)
   - PostgreSQL
   - MySQL / MariaDB
   - SQLite
   - MongoDB
   - Supabase (hosted Postgres)
   - No database / not decided yet
8. **Auth strategy?** (skip if no auth)
   - Self-built (JWT / sessions)
   - Clerk / Auth0 / Supabase Auth
   - No auth / not decided yet

### D. Engineering Preferences

9. **Testing philosophy?**
   - TDD (tests written first, always)
   - Test-after (code first, tests after)
   - Integration-first (mostly integration + e2e)
   - Minimal (tests where it makes sense)
10. **CI/CD?**
    - GitHub Actions
    - GitLab CI / CircleCI / other
    - None / not yet
11. **Setup mode?**
    - **Express** — lean scaffold, core files only. Best for: solo projects, early-stage, side projects.
    - **Full** — all 14 skills/ files, pre-commit hooks, graphify, agentation, AGENTS.md. Best for: team projects, production-bound work.

---

## Step 3: Confirm Plan Before Executing

Summarize back to the user:

```
Stack:     [languages + frameworks confirmed]
Type:      [project type]
Mode:      [express / full]
Services:  [single / monorepo / polyrepo]
Will create:
  - Directory structure
  - [list of config files for confirmed stack only]
  - skills/ files ([count] files)
  - CLAUDE.md, WELCOME.md
  - [full mode: AGENTS.md, COMMIT.md, pre-commit.sh, graphify, agentation if applicable]

Proceed?
```

Wait for confirmation before creating anything.

---

## Step 4: Directory Structure

### Single Service

```
project-root/
├── src/                    ← application source
├── skills/                 ← reference docs for agents and engineers
├── plan/                   ← research docs (always gitignored — never ships)
├── tests/
├── CLAUDE.md
├── WELCOME.md
├── AGENTS.md               ← full mode only
├── COMMIT.md               ← full mode only
└── pre-commit.sh
```

### Monorepo (multiple services)

```
project-root/               ← NOT a git repo
├── [frontend or app]/      ← git init here
├── [backend or api]/       ← git init here
├── [shared or packages]/   ← git init here (if applicable)
├── skills/                 ← shared reference docs
├── plan/                   ← gitignored
├── CLAUDE.md
├── AGENTS.md               ← full mode only
├── COMMIT.md               ← full mode only
├── WELCOME.md
└── pre-commit.sh
```

**Absolute rules:**

- `plan/` is always in `.gitignore`. Research never ships.
- In a monorepo, the project root is NOT a git repo. Each service is independently versioned.
- Each service git repo gets a pre-commit hook symlinked to `../../pre-commit.sh`.

---

## Step 5: .gitignore

```
# Research — never ships
plan/

# Secrets
.env
.env.*
!.env.example

# Dependencies
node_modules/
.venv/
vendor/

# Build artifacts
dist/
build/
.next/
out/

# .NET
bin/
obj/
*.user
TestResults/

# Python
__pycache__/
*.py[cod]
.mypy_cache/
.ruff_cache/
.pytest_cache/
*.egg-info/

# Coverage
coverage/
.coverage
htmlcov/

# Logs
*.log

# Graphify
graph.json
graph.html
graphify-out/
GRAPH_REPORT.md
```

---

## Step 6: Language-Specific Config

Generate ONLY the configs for languages confirmed in the questionnaire.

---

### TypeScript / JavaScript Projects

**tsconfig.json**

```json
{
  "compilerOptions": {
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "exactOptionalPropertyTypes": true,
    "forceConsistentCasingInFileNames": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "allowJs": false
  }
}
```

`noUncheckedIndexedAccess` is non-negotiable — `arr[0]` returns `T | undefined`, not `T`.

**eslint.config.mjs**

```js
{
  rules: {
    "@typescript-eslint/no-explicit-any": "error",
    "@typescript-eslint/no-unsafe-assignment": "error",
    "@typescript-eslint/no-unsafe-member-access": "error",
    "@typescript-eslint/no-unsafe-return": "error",
    "@typescript-eslint/no-non-null-assertion": "error",
    "eqeqeq": ["error", "always"],
    "no-cond-assign": "error",
    "@typescript-eslint/strict-boolean-expressions": "error",
    "no-console": ["error", { "allow": ["warn", "error"] }],
    "@typescript-eslint/no-unused-vars": ["error", {
      "argsIgnorePattern": "^_",
      "varsIgnorePattern": "^_",
      "caughtErrorsIgnorePattern": "^_",
    }],
  }
}
```

**Hook naming rule:** Only name a function `useX` if it calls React hooks. Plain utilities that don't call hooks must use a verb prefix (`apply`, `handle`, `build`, `get`). Naming a non-hook `useX` triggers `react-hooks/rules-of-hooks` the moment it's called inside a callback.

**`eslint-disable` policy:** Every suppression must have a `//` explanation comment on the line immediately above it. Pre-commit enforces this.

---

### Python Projects

**pyproject.toml**

```toml
[tool.mypy]
python_version = "3.12"
strict = true
disallow_any_explicit = true
warn_unreachable = true
warn_unused_ignores = true

[tool.ruff]
target-version = "py312"
line-length = 100

[tool.ruff.lint]
select = ["E", "W", "F", "I", "B", "UP", "S", "SIM", "RUF", "ANN", "N", "PT"]
ignore = ["S101"]  # ANN101/ANN102 (self/cls annotations) were removed from ruff — do not list them
```

---

### C# / .NET Projects

**global.json** — pin the SDK. No "works on my machine" builds.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

**Directory.Build.props** (solution root — applies to every project automatically)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is non-negotiable — a possible-null dereference is a compile error, not a 2am `NullReferenceException`.

**.editorconfig** (key severity rules — full file generated at setup)

```ini
[*.cs]
dotnet_diagnostic.CA2007.severity = none          # ConfigureAwait — app code, not a library
dotnet_diagnostic.CA1062.severity = error         # validate public args
dotnet_diagnostic.CA1305.severity = error         # IFormatProvider — culture bugs
dotnet_diagnostic.CA2016.severity = error         # forward CancellationToken
dotnet_diagnostic.CS8618.severity = error         # non-nullable uninitialized
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_namespace_declarations = file_scoped:error
dotnet_style_require_accessibility_modifiers = always:error
```

**Rules:**

- `async void` is banned everywhere except event handlers.
- `.Result` / `.Wait()` / `GetAwaiter().GetResult()` on async code is banned — deadlock + thread-pool starvation.
- Every async method that does I/O accepts and forwards a `CancellationToken`.
- No `dynamic` in domain or application code.
- Custom CQRS dispatch (`ICommandHandler<,>` / `IQueryHandler<,>`) — no MediatR or any mediator library.

---

## Step 7: Design Token System (Frontend Projects Only)

Skip this step entirely if the project has no frontend UI.

All colors, fonts, and spacing live in ONE file: `src/design/tokens.ts`.

```typescript
// The only place design values are defined. Change here → reflects everywhere.
export const Colors = {
  /* hex values */
} as const;
export const Typography = {
  fontFamily,
  fontSize,
  lineHeight,
  fontWeight,
} as const;
export const Spacing = {
  /* 4px base unit, named scale */
} as const;
export const Radius = { sm, md, lg, xl, full } as const;
export const Shadow = { sm, md, lg, glow } as const;
```

Rules:

- Never hardcode a hex value in a component
- CSS custom properties generated from this file — TypeScript and CSS stay in sync
- Font size change = one line in `tokens.ts`

For Tailwind 4 projects: define in `@theme` in `globals.css`, mirror the token names in a TypeScript `TOKENS` constant using `satisfies`.

---

## Step 8: Constants Architecture & Branded Types (No Free-Floating Strings)

### TypeScript

```typescript
// src/constants/[domain].constants.ts
export const UserStatus = {
  ACTIVE: "active",
  SUSPENDED: "suspended",
  DELETED: "deleted",
} as const;
export type UserStatus = (typeof UserStatus)[keyof typeof UserStatus];

// Barrel export — src/constants/index.ts
export * from "./user-status.constants";
export * from "./api-paths.constants";
export * from "./error-messages.constants";
```

### Python

```python
from enum import StrEnum
from typing import Final

class UserStatus(StrEnum):
    ACTIVE    = "active"
    SUSPENDED = "suspended"
    DELETED   = "deleted"

MAX_ITEMS_PER_PAGE: Final[int] = 100
```

### C#

```csharp
// Domain/Constants/UserStatus.cs — smart enum, DB-friendly string values
public sealed record UserStatus
{
    public static readonly UserStatus Active    = new("active");
    public static readonly UserStatus Suspended = new("suspended");
    public static readonly UserStatus Deleted   = new("deleted");

    public string Value { get; }
    private UserStatus(string value) => Value = value;

    public static UserStatus From(string value) => value switch
    {
        "active"    => Active,
        "suspended" => Suspended,
        "deleted"   => Deleted,
        _ => throw new ArgumentException($"Invalid UserStatus: {value}"),
    };
}

// Domain/Constants/Limits.cs
public static class Limits
{
    public const int MaxItemsPerPage = 100;
}
```

**Rule:** Before writing any feature, identify the string/number constants it needs. Add them to the constants file first. No literals in domain or application code.

### Branded / Opaque Types (No Free-Floating Strings)

Plain `string` (and `number`) types are the source of a whole class of bugs: a `UserId` gets passed where an `OrgId` was expected, an unvalidated raw input flows into a function that assumes it's been checked, and the compiler says nothing. Every domain identifier and meaningful value gets its own **opaque brand type** so the type system — not code review — guarantees the right value reaches the right place.

**Rule:** No domain value travels as a bare `string`/`number`. If a value has meaning (an id, a token, an email, a slug, a path, a currency amount), give it a brand. A branded value can only be produced by its constructor/validator, so once you hold one, its provenance is proven by the type. Constructors live next to the type; they are the single chokepoint where a raw string becomes a branded value.

#### TypeScript

```typescript
// src/types/brand.ts — one tiny helper, reused everywhere
declare const __brand: unique symbol;
export type Brand<T, B extends string> = T & { readonly [__brand]: B };

// src/domain/user/user.types.ts
export type UserId = Brand<string, "UserId">;
export type OrgId = Brand<string, "OrgId">;
export type Email = Brand<string, "Email">;

// Constructors are the ONLY way to mint a branded value — validate at the boundary.
export const UserId = (raw: string): UserId => {
  if (!/^usr_[a-z0-9]+$/.test(raw)) throw new Error(`Invalid UserId: ${raw}`);
  return raw as UserId;
};
export const Email = (raw: string): Email => {
  if (!raw.includes("@")) throw new Error(`Invalid Email: ${raw}`);
  return raw.toLowerCase() as Email;
};

// Now this is a COMPILE error, not a 2am incident:
function getUser(id: UserId) {
  /* ... */
}
declare const orgId: OrgId;
getUser(orgId); // ✗ Argument of type 'OrgId' is not assignable to 'UserId'
getUser("usr_123"); // ✗ string is not assignable to 'UserId'
getUser(UserId("usr_123")); // ✓ provenance proven by the type
```

#### Python

```python
from typing import NewType
import re

UserId = NewType("UserId", str)
OrgId  = NewType("OrgId", str)
Email  = NewType("Email", str)

# Smart constructors — the only sanctioned way to build a branded value.
def make_user_id(raw: str) -> UserId:
    if not re.fullmatch(r"usr_[a-z0-9]+", raw):
        raise ValueError(f"Invalid UserId: {raw}")
    return UserId(raw)

# mypy flags getUser(org_id) where getUser expects UserId.
# (For runtime-validated brands, Pydantic models or Annotated types are the heavier alternative.)
```

#### C#

```csharp
// Strongly-typed ID — readonly record struct: zero heap allocation, fully distinct at compile time.
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
    public static UserId Parse(string raw) =>
        Guid.TryParse(raw, out var g) ? new(g)
        : throw new ArgumentException($"Invalid UserId: {raw}");
    public override string ToString() => Value.ToString();
}

public readonly record struct OrgId(Guid Value);

// Task<User> GetUserAsync(UserId id, CancellationToken ct)
// — passing an OrgId or a bare Guid is a compile error.
```

**EF Core value converter — the ONLY place the brand unwraps to a primitive:**

```csharp
// Infrastructure/Persistence/Configurations/UserConfiguration.cs
builder.Property(u => u.Id)
    .HasConversion(id => id.Value, value => new UserId(value));
```

Register a converter per branded type in `ConfigureConventions` so no `Guid`/`string` ever leaks into domain signatures. Same pattern for API boundaries: a `JsonConverter<UserId>` (or minimal-API `TryParse` support) so DTOs also carry the brand.

**Agent rule:** When you introduce a new identifier or meaningful value, define its brand type and constructor first, then thread the branded type through every signature. Never widen back to `string`/`number` to "make it pass" — fix the type, or add a validating constructor at the boundary where the raw value enters.

---

## Step 9: External Tools (Full Mode)

### Graphify — Orient Before You Explore

```bash
pip install graphifyy
graphify install
graphify claude install  # registers PreToolUse hook in Claude Code
```

Run on each service after creation:

```bash
/graphify src/
/graphify backend/
```

**Agent rule:** Read `GRAPH_REPORT.md` before exploring any unfamiliar module. Never grep blindly when the knowledge graph answers the question in one read.

### Iris — Automated Manual Testing (React / Next.js / any React-based framework — MANDATORY)

> Source: https://github.com/syrin-labs/iris

Iris automates manual UI testing and closes the feedback loop between code changes and visual/behavioral verification. It is **mandatory** for any React-based project — install it before writing the first component.

```bash
cd app && npm install @syrin/iris -D
```

Follow the setup instructions at https://github.com/syrin-labs/iris for framework-specific configuration (Next.js app router, Vite, CRA, etc.).

**Why mandatory:** Manual testing is the last mile of every UI feature. Without Iris, feedback loops are slow — you change code, manually click through flows, and miss regressions in adjacent paths. Iris automates this so every agent and engineer gets instant, repeatable feedback on real browser behavior, not just type-check output.

**Agent rule:** Before marking any UI task complete, run the relevant Iris test. "Type-checks pass" is not the same as "the feature works."

### Agentation — Visual Feedback (React / Next.js only)

```bash
cd app && npm install agentation agentation-mcp -D
npx add-mcp "npx -y agentation-mcp server"
```

Add to root layout (dev only):

```tsx
{
  process.env.NODE_ENV === "development" && <Agentation />;
}
```

### Refero Design Reference (Frontend projects)

```bash
npx skills add https://github.com/referodesign/refero_skill
```

Search: `/refero [niche]` e.g. `/refero "SaaS dashboard"`. Extract: color palette, typography scale, spacing rhythm, shadow system. Put all tokens in `skills/design.md` — the single source of truth.

### WebMCP — Expose Page Actions as Agent Tools (Frontend projects)

> Source: https://developer.chrome.com/docs/ai/webmcp • Spec explainer: https://github.com/webmachinelearning/webmcp

WebMCP is a proposed web standard that lets a page register **structured tools** (name + description + JSON Schema + callback) that AI agents — built-in browser agents or extensions — can discover and invoke, instead of guessing at your UI by screenshot. One tool call replaces a fragile click-path, and the human UI stays primary: tools augment it, never replace it.

**Availability (as of mid-2026):** Chrome 149+ origin trial for production (register at the Chrome origin trial portal); local dev via `chrome://flags/#enable-webmcp-testing` → Enabled → relaunch. The API is under active discussion — always feature-detect, never assume.

**Imperative API — register a tool for each key user flow:**

```typescript
// src/agent/tools.ts — one module owns all tool registration
export function registerAgentTools(): AbortController | undefined {
  if (!("modelContext" in document)) return undefined; // feature-detect: origin trial only

  const controller = new AbortController();

  document.modelContext.registerTool(
    {
      name: "add-todo",
      description: "Add a new item to the user's active todo list",
      inputSchema: {
        type: "object",
        properties: {
          text: { type: "string", description: "The text content of the todo item" },
        },
        required: ["text"],
      },
      async execute({ text }: { text: string }) {
        await addTodoItemToCollection(text); // REUSE existing app logic — never a parallel code path
        return {
          content: [{ type: "text", text: `Added todo item: "${text}" successfully.` }],
        };
      },
    },
    { signal: controller.signal }, // abort() unregisters — tie to route/component lifecycle
  );

  return controller;
}
```

**Declarative API:** plain `<form>`s can be annotated so the browser synthesizes the tool definition automatically — prefer it for simple form submission; use the imperative API for anything JS-driven.

**Rules (non-negotiable):**

- **Tool input is untrusted input.** Agents can be prompt-injected; validate and authorize every argument server-side exactly as you would a hand-typed form. A tool must never widen what the logged-in user could already do in the UI.
- **No silent destructive actions.** Payments, deletes, and sends go through the same confirmation UI a human sees — the tool stages the action, the user commits it.
- **Tools mirror page state.** Register/unregister with `AbortController` on SPA route changes so the advertised tool list always matches what the page can actually do.
- **Reuse client-side logic.** `execute` calls the same functions your buttons call. A second code path for agents is a second place for bugs.
- **Cross-origin is opt-in.** Tools default to same-origin + built-in agents. Delegate to embedded agents only via `<iframe allow="tools">` and the `exposedTo` registration option; `registerTool` rejects with `NotAllowedError` when the `tools` Permissions Policy is disabled. WebMCP also requires origin-isolated documents (no `document.domain`).
- **Feature-detect everything** (`"modelContext" in document`) — this is an origin trial, subject to change or removal.

**Testing:** the *Model Context Tool Inspector* Chrome extension exercises registered tools with natural-language prompts. Demo references: GoogleChromeLabs `webmcp-demos` (Pizza Maker — imperative; Le Petit Bistro — declarative).

---

## Step 10: pre-commit.sh Quality Gate

Generate only the sections for languages in the confirmed stack.
Order of operations — always this order, never skip:

1. **Safety** — no secrets staged, no `plan/` staged, no `any` (TS), no `console.log` (TS), no `dynamic` (C#), no `.Result`/`.Wait()` (C#), no files over 500 lines
2. **Python** (if in stack) — ruff format → ruff lint → mypy
3. **TypeScript** (if in stack) — prettier → eslint → tsc --noEmit
4. **C#** (if in stack) — `dotnet format --verify-no-changes` → `dotnet build -warnaserror --nologo` (analyzers run in build)
5. **Tests** — fast unit tests only. Integration tests belong in CI only.
6. **Summary** — colored pass/fail count, exit 1 if any failure

Install as git hook in each service:

```bash
ln -sf ../../pre-commit.sh app/.git/hooks/pre-commit
ln -sf ../../pre-commit.sh backend/.git/hooks/pre-commit
chmod +x pre-commit.sh
```

---

## Step 11: Generate skills/ Reference Files

> This step is what separates this skill from a setup script.
> These files are what engineers and agents open every day.
> Generate them with real engineering depth — adapt all examples to the actual project stack.
> Content comes from Part II of this SKILL.md AND your own knowledge of best practices for the detected stack.

### Full Mode — All 14 Files

| File                      | Content source                      | Open when                          |
| ------------------------- | ----------------------------------- | ---------------------------------- |
| `skills/design.md`        | Part II (design tokens) + refero    | Building any UI component          |
| `skills/typescript.md`    | Part II.11 + TS patterns            | Any TypeScript question or pattern |
| `skills/python.md`        | Part II.11 + Python patterns        | Any Python question or pattern     |
| `skills/csharp.md`        | Part II.11 + C#/.NET patterns       | Any C# / .NET question or pattern  |
| `skills/testing.md`       | Part II.7 + TDD workflow            | Before writing any feature code    |
| `skills/conventions.md`   | Part II (constants + branded types) | Before naming anything             |
| `skills/agents.md`        | Part II (agent patterns)            | Multi-agent task or orchestration  |
| `skills/architecture.md`  | Part II.9 + service contracts       | New service or API contract        |
| `skills/database.md`      | Part II.12 (full database guide)    | Before any table, query, or schema |
| `skills/security.md`      | Part II.4 + security checklist      | Any endpoint touching user data    |
| `skills/performance.md`   | Part II.3 + frontend perf           | Anything slow or memory-hungry     |
| `skills/observability.md` | Part II.6 + alerting + tracing      | Logs, metrics, or circuit breakers |
| `skills/api-design.md`    | Part II (API section) + REST        | Any new API endpoint               |
| `skills/cicd.md`          | Part II (infra) + Docker + deploy   | CI pipeline, Docker, deployment    |

Mark inapplicable files (e.g., `skills/design.md` for a pure API project) with "N/A for this stack" at the top rather than omitting them.

### Express Mode — 4 Core Files

Generate only:

- `skills/conventions.md` — always needed
- `skills/testing.md` — always needed
- `skills/architecture.md` — needed from day 1
- One language-specific file (`skills/typescript.md` OR `skills/python.md` OR `skills/csharp.md`)

---

## Step 12: Generate CLAUDE.md

Must include:

1. **Project overview** — name, one-paragraph description, and stack (from questionnaire)
2. **Directory structure** with annotations
3. **Service boundary rules** — who owns what; which services handle which concerns
4. **Non-negotiables** — pulled from Part II, adapted to the confirmed stack:
   - Always `===` not `==` (TS) / always `==` not `is` for values (Python) / no `==` on floating-point, use `Equals` semantics intentionally (C#)
   - No `any`, no non-null assertions (TS) / `mypy strict` (Python) / `Nullable enable`, no `dynamic`, no `async void`, no `.Result`/`.Wait()` (C#)
   - No free-floating strings — constants first, and every domain value carries an opaque brand type (no bare `string`/`number` for ids, tokens, emails, etc.)
   - Tests before (or alongside) code
   - 500-line file cap
   - No `SELECT *`, no unbounded queries (if database in stack)
5. **Naming conventions table** — adapted to language(s) in stack
6. **Pre/post coding checklist:**
   - Scan for reuse before writing
   - Write failing test first (if TDD mode confirmed)
   - Code to green
   - Refactor with tests passing
   - Check file length (500 line cap)
7. **Reference table** — links to all skills/ files created and when to open each

---

## Step 13 (Full Mode): AGENTS.md and COMMIT.md

### AGENTS.md

```markdown
# Agent Orchestration

## Tool Manifest

| Tool       | Use for                             | When NOT to use            |
| ---------- | ----------------------------------- | -------------------------- |
| Graphify   | Orient before exploring a module    | Already know file location |
| Iris       | Automated UI testing, feedback loop | Non-UI / non-React changes |
| Agentation | Visual verification of UI changes   | Non-UI changes             |
| Bash       | Shell operations, git, build        | Reading/writing files      |
| Read       | Reading known file paths            | Broad codebase exploration |
| Explore    | Finding files, grepping across repo | Single known file          |

## Parallelization Rules

- Independent reads: run in parallel
- File writes that could conflict: sequential
- Test suite: parallel by test file
- Cross-service API calls: always async

## Context Handoff

When handing off between agents:

- Include: current task, files modified, tests status, blockers
- Always attach: relevant skills/ file path for the domain
```

### COMMIT.md

```markdown
# Pre-Commit Checklist

□ No secrets or .env files staged?
□ plan/ excluded from staging?
□ All linters pass?
□ Type checker passes with zero errors?
□ Any failing tests? If so, explain why in the commit message.
□ Any file over 500 lines? If so, refactor first.
□ eslint-disable comments have an explanation comment above them?
□ New constants added to constants/ before use in domain code?
□ New domain values (ids, tokens, emails…) given a brand type, not passed as bare string/number?
□ New DB columns: migration is backward-compatible with running app code?
```

---

## Step 14: Generate WELCOME.md

Create `WELCOME.md` at the project root. Must include:

1. Project name and one-line description
2. Stack summary (languages, frameworks, deployment, key choices from questionnaire)
3. Annotated directory structure
4. First 5 commands to run after cloning
5. Table: "File → When to open it" for all skills/ files created
6. Non-negotiables for this stack
7. Installed tools and how to invoke them
8. Feature build order (what to build first and why, given the project type)

This is the first file a new engineer or agent reads. Make it the orientation they need.

---

## Step 15: Git Setup and Final Validation

```bash
# Single service
git init
ln -sf ../pre-commit.sh .git/hooks/pre-commit
chmod +x pre-commit.sh

# Monorepo — run per service
cd frontend && git init && ln -sf ../../pre-commit.sh .git/hooks/pre-commit
cd ../backend && git init && ln -sf ../../pre-commit.sh .git/hooks/pre-commit
```

### Final Validation Checklist

Do NOT report "setup complete" until all applicable items pass.

**Always (both modes):**

```
□ .gitignore includes plan/, .env, all build artifacts, graphify outputs?
□ CLAUDE.md exists and references all skills/ files created?
□ WELCOME.md created with first-5-commands and skills table?
□ pre-commit.sh executable and runs without error on empty project?
□ Constants architecture set up with code examples for this stack?
□ .env.example created for each service with all required vars documented?
□ Git initialized and pre-commit hook linked?
```

**Full mode only:**

```
□ All 14 skills/ files created (or marked N/A for stack)?
□ AGENTS.md exists with tool manifest?
□ COMMIT.md created?
□ Design token file created? (frontend only)
□ eqeqeq: "error" and no-cond-assign: "error" in ESLint? (TS projects)
□ mypy strict = true and disallow_any_explicit = true? (Python projects)
□ Nullable enable + TreatWarningsAsErrors in Directory.Build.props? (C# projects)
□ Graphify installed and knowledge graph built for each service?
□ Iris installed and configured? (any React-based project — mandatory)
□ Agentation installed? (Next.js / React projects)
□ Refero design skill installed? (frontend projects)
□ WebMCP tools registered for key user flows, feature-detected, args validated server-side? (frontend projects — Chrome origin trial, optional)
```

---

# PART II — ENGINEERING STANDARDS THAT SEPARATE TOP 1%

> IMPORTANT: This section is the source of truth for generating skills/ files.
> Each II.N section maps to a specific skills/ file.
> When generating skills/ files in Step 11, draw content from these sections
> AND apply them to the specific project stack.
>
> Mapping:
> II.1 → skills/agents.md (async/concurrency traps)
> II.2 → skills/database.md (ORM traps, N+1, isolation levels)
> II.3 → skills/performance.md (anti-patterns, big-O, memory leaks)
> II.4 → skills/security.md (security vulnerabilities)
> II.5 → skills/api-design.md (idempotency)
> II.6 → skills/observability.md (logging, metrics, circuit breaker)
> II.7 → skills/testing.md (TDD, property-based, mocking)
> II.8 → skills/conventions.md (quality metrics, rule of three)
> II.9 → skills/architecture.md (distributed systems, architecture decisions)
> II.10 → skills/architecture.md (CAP theorem)
> II.11 → skills/typescript.md + skills/python.md + skills/csharp.md (equality, type safety)
> II.12 → skills/database.md (full database engineering guide)

> This section is not project setup. It is the permanent operating manual.
> Every agent working in this codebase must internalize this before writing code.
> These are the things that cause production outages, data loss, and security breaches
> when a developer who doesn't know them ships to production.

---

## II.1 — Async and Concurrency: Traps That Hit Production

### Python asyncio: Single-threaded does NOT mean race-condition-free

Every `await` is a suspension point where the event loop switches to another coroutine. This is where race conditions live:

```python
# BUG: race condition at every await
ran = False

async def foo():
    global ran
    if not ran:
        await asyncio.sleep(0)  # event loop switches HERE — three tasks enter this branch
        ran = True

# All three coroutines see False, all three set ran = True
```

```python
# FIX: asyncio.Lock
lock = asyncio.Lock()

async def foo():
    async with lock:
        global ran
        if not ran:
            await asyncio.sleep(0)
            ran = True
```

**Interview question that kills candidates:** "Can asyncio code have race conditions?" **Yes.** At every `await`.

### The GIL and when it actually matters

```
Task type           | threading      | asyncio          | multiprocessing
--------------------|----------------|------------------|------------------
CPU-bound (compute) | Worse than 1   | No improvement   | True parallelism
I/O-bound (network) | Works (GIL     | Best choice      | Overkill
                    | releases)      |                  |
```

When a FastAPI endpoint is CPU-bound, `ThreadPoolExecutor` does not help — use `ProcessPoolExecutor`:

```python
loop = asyncio.get_event_loop()
result = await loop.run_in_executor(
    ProcessPoolExecutor(),   # NOT ThreadPoolExecutor for CPU work
    compute_heavy_function,
    argument
)
```

### Node.js Event Loop: Output order is not what you think

```javascript
console.log("1"); // sync
setTimeout(() => console.log("2"), 0); // macrotask
Promise.resolve().then(() => console.log("3")); // microtask
process.nextTick(() => console.log("4")); // nextTick (before microtasks)
console.log("5"); // sync

// Output: 1, 5, 4, 3, 2
```

**Microtask starvation** — if a microtask schedules another microtask, macrotasks (I/O) never run:

```javascript
// This blocks all I/O indefinitely:
function infinite() {
  Promise.resolve().then(infinite);
}
infinite();
```

### C# async/await: The Three Traps That Take Down ASP.NET Core Services

**Trap 1 — Sync-over-async deadlocks and thread-pool starvation:**

```csharp
// WRONG: blocks a thread-pool thread waiting for another thread-pool thread.
// Under load, all threads block → starvation → every request times out at once.
var user = _userService.GetUserAsync(id).Result;
var user = _userService.GetUserAsync(id).GetAwaiter().GetResult();

// RIGHT: async all the way down — from controller to repository.
var user = await _userService.GetUserAsync(id, ct);
```

**Trap 2 — `async void` swallows exceptions and crashes the process:**

```csharp
// WRONG: exception here cannot be caught by the caller — it crashes the process
public async void ProcessMessage(Message msg) { ... }

// RIGHT: async Task — awaitable, exceptions observable
public async Task ProcessMessageAsync(Message msg, CancellationToken ct) { ... }
```

`async void` is legal ONLY for UI event handlers. Nowhere else.

**Trap 3 — Fire-and-forget without observation:**

```csharp
// WRONG: unobserved task — exception vanishes, work may be killed on app shutdown
_ = DoBackgroundWorkAsync();

// RIGHT: use a hosted BackgroundService + Channel<T>, or queue to RabbitMQ.
// In-process background work belongs in IHostedService with proper cancellation.
```

**CancellationToken rule:** Every async method that performs I/O accepts a `CancellationToken` and forwards it to every awaited call (`CA2016` enforces this). A cancelled HTTP request that keeps running its DB query is wasted pool capacity.

**Parallelism:** `Task.WhenAll` for independent I/O — but never share a single EF Core `DbContext` across concurrent tasks. `DbContext` is not thread-safe; resolve one per unit of work (or use `IDbContextFactory`).

---

## II.2 — Database: What ORMs Hide From You

### The N+1 Query Problem — The Most Common Production Performance Disaster

```python
# Django — generates N+1 queries silently
authors = Author.objects.all()          # 1 query
for author in authors:
    print(author.books.all())           # 1 query PER author — never do this
```

```python
# Fix: select_related (JOIN) for ForeignKey/OneToOne
authors = Author.objects.select_related('profile').all()  # 1 query

# Fix: prefetch_related (IN) for ManyToMany
authors = Author.objects.prefetch_related('books').all()  # 2 queries total
```

```python
# SQLAlchemy — explicitly opt in to eager loading
books = session.query(Book).options(selectinload(Book.reviews)).all()
```

```csharp
// EF Core — lazy loading proxies generate N+1 silently. Eager-load explicitly:
var authors = await db.Authors
    .Include(a => a.Books)                       // JOIN
    .AsSplitQuery()                              // avoid cartesian explosion on multiple Includes
    .ToListAsync(ct);

// Read-only queries: AsNoTracking() — skips change tracker, big win on large result sets
var list = await db.Authors.AsNoTracking().Include(a => a.Books).ToListAsync(ct);
```

**Rule:** Never access a relationship inside a loop without verifying the ORM is not generating N queries. Always check with `EXPLAIN ANALYZE` or Django's `django-debug-toolbar` in development.

### Transaction Isolation Levels — Know What Your DB Default Is

| Level            | Dirty Read | Non-Repeatable Read | Phantom Read | Default in         |
| ---------------- | ---------- | ------------------- | ------------ | ------------------ |
| Read Uncommitted | Possible   | Possible            | Possible     | (never use)        |
| Read Committed   | Prevented  | **Possible**        | **Possible** | PostgreSQL, Oracle |
| Repeatable Read  | Prevented  | Prevented           | **Possible** | MySQL InnoDB       |
| Serializable     | Prevented  | Prevented           | Prevented    | Explicit only      |

**The production bug Read Committed causes:**

```sql
-- Session A (PostgreSQL default — Read Committed):
BEGIN;
SELECT balance FROM accounts WHERE id = 1;  -- Returns 1000

-- Session B (concurrent):
UPDATE accounts SET balance = balance - 200 WHERE id = 1; COMMIT;

-- Session A reads again:
SELECT balance FROM accounts WHERE id = 1;  -- Returns 800 ← NON-REPEATABLE READ
```

For financial operations, inventory, or any "read-then-write based on what I read" flow: use `SERIALIZABLE` or explicit locking. Read Committed is fine for analytics.

### When Indexes Make Queries Slower

1. **Low selectivity** — An index on a boolean where 90% of rows are `true`. The planner does a seq scan anyway, you added write overhead for nothing.
2. **High write tables** — 10 indexes = 10× write amplification on every INSERT/UPDATE/DELETE.
3. **Left-most prefix violation** — Index on `(user_id, status, created_at)`. A query on `WHERE status = 'pending' AND created_at > $1` without `user_id` cannot use this index.
4. **SELECT \*** — Prevents covering index usage, fetches blob columns you don't need.

**Rule:** Always verify with `EXPLAIN ANALYZE` before and after adding an index. Never add indexes without measuring.

### Connection Pooling — You Cannot Open a DB Connection Per Request

Each new PostgreSQL connection costs 50–200ms (TCP + TLS + auth + process fork) and ~10MB RAM. At 1,000 req/s, that's 1,000 new connections/second — PostgreSQL defaults to `max_connections=100`, so you collapse at ~100 concurrent requests.

**Pool size formula:** `3–5 × CPU_core_count` backend connections. Beyond that, context-switching overhead exceeds parallelism gain.

**Never** create a new DB client/pool inside a request handler. Create it once at application startup and share it.

---

## II.3 — Performance Anti-Patterns That Kill Production at Scale

### O(n²) Hidden in Innocent-Looking Code

```python
# O(n²) — looks harmless at 100 items, destroys production at 100,000
def find_duplicates(items: list[str]) -> list[str]:
    duplicates = []
    for i in items:
        for j in items:       # inner loop runs len(items) times for each outer
            if i == j and i not in duplicates:
                duplicates.append(i)
    return duplicates

# O(n) fix:
def find_duplicates(items: list[str]) -> list[str]:
    seen: set[str] = set()
    duplicates: set[str] = set()
    for item in items:
        if item in seen:
            duplicates.add(item)
        seen.add(item)
    return list(duplicates)
```

**Rule:** Before shipping any function that iterates, ask: "What is the time complexity? What happens at 10× current data volume?"

### String Concatenation in Loops — O(n²) Memory

```python
# O(n²) — creates len(lines) intermediate string objects
result = ""
for line in lines:
    result += line + "\n"

# O(n) fix:
result = "\n".join(lines)
```

```javascript
// O(n²):
let html = "";
for (const item of items) {
  html += `<li>${item}</li>`;
}

// O(n):
const html = items.map((item) => `<li>${item}</li>`).join("");
```

### Python Mutable Default Arguments — Stateful Bugs

```python
# BUG: default list is created ONCE at function definition time
def add_item(item: str, bank: list[str] = []) -> list[str]:
    bank.append(item)
    return bank

add_item("a")  # ["a"]
add_item("b")  # ["a", "b"] ← same list object across calls!

# FIX: use None as sentinel
def add_item(item: str, bank: list[str] | None = None) -> list[str]:
    if bank is None:
        bank = []
    bank.append(item)
    return bank
```

**This also applies to class-level mutable attributes in Django models and SQLAlchemy.**

### Exceptions as Control Flow — 100-1000× Overhead

```python
# Expensive: exception creation captures full stack trace
def get_value(d: dict[str, int], key: str) -> int | None:
    try:
        return d[key]        # exception on every miss
    except KeyError:
        return None

# Fast: explicit check
def get_value(d: dict[str, int], key: str) -> int | None:
    return d.get(key)        # no exception created
```

**Rule:** Exceptions are for exceptional conditions. If "key not found" is a normal case, check with `.get()` or `in`, not try/except.

### SELECT \* in Production

```sql
-- WRONG: fetches unused columns (including large TEXT/JSONB blobs)
SELECT * FROM users WHERE id = $1;

-- RIGHT: fetch only what's needed — allows covering index, reduces serialization
SELECT id, display_name, email, created_at FROM users WHERE id = $1;
```

### Unbounded Queries — No Pagination

```python
# WRONG: at 1M rows this returns 1M rows in memory
posts = await db.execute("SELECT * FROM posts WHERE user_id = $1", user_id)

# RIGHT: always paginate
posts = await db.execute(
    "SELECT * FROM posts WHERE user_id = $1 ORDER BY created_at DESC LIMIT $2 OFFSET $3",
    user_id, limit, offset
)
```

**Rule:** Every query that returns a list must have an explicit LIMIT. No exceptions. Even internal admin queries.

### Cache Stampede — The Thundering Herd

When a popular cache key expires and 1,000 concurrent requests all miss simultaneously, all 1,000 hit the database at once, potentially taking it down.

**Fix — add jitter to TTL:**

```python
import random
ttl = BASE_TTL + random.randint(0, int(0.2 * BASE_TTL))  # ±20% jitter
await redis.setex(cache_key, ttl, value)
```

**Fix — singleflight pattern:**

```python
in_flight: dict[str, asyncio.Future] = {}

async def get_with_singleflight(key: str) -> str:
    if key in in_flight:
        return await in_flight[key]
    future: asyncio.Future[str] = asyncio.get_event_loop().create_future()
    in_flight[key] = future
    try:
        value = await fetch_from_db(key)
        future.set_result(value)
        return value
    finally:
        del in_flight[key]
```

**Rule:** "Just add a cache" makes things worse when: (1) the bottleneck isn't the query, (2) invalidation logic is wrong, (3) stampede isn't handled. Profile before caching.

---

## II.4 — Security: What Most Engineers Miss

### Timing Attacks — Never Use `==` for Secrets

```python
# VULNERABLE: short-circuits on first mismatch — response time reveals correct bytes
if user_token == secret_token:
    grant_access()

# SAFE: constant-time regardless of where mismatch occurs
import hmac
if hmac.compare_digest(user_token, secret_token):
    grant_access()
```

```javascript
import { timingSafeEqual } from "crypto";
const safe = timingSafeEqual(Buffer.from(userToken), Buffer.from(secretToken));
```

```csharp
// C# — constant-time comparison
var safe = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(userToken),
    Encoding.UTF8.GetBytes(secretToken));
```

**Rule:** Use `hmac.compare_digest` / `crypto.timingSafeEqual` / `CryptographicOperations.FixedTimeEquals` for: HMAC digests, API keys, password reset tokens, session tokens. Regular `==` for everything else.

### JWT Algorithm Confusion Attack

```python
# WRONG: reads algorithm from the token itself — attacker sets alg: "none"
jwt.decode(token, key, algorithms=jwt.get_unverified_header(token)['alg'])

# RIGHT: server decides the algorithm
jwt.decode(token, key, algorithms=['HS256'])  # never trust the header
```

### Mass Assignment Vulnerability

```javascript
// VULNERABLE: trusts entire request body
await User.findByIdAndUpdate(req.user.id, req.body);
// Attacker sends: { "name": "John", "role": "admin", "balance": 999999 }

// SAFE: explicit allowlist
const { name, email, bio } = req.body;
await User.findByIdAndUpdate(req.user.id, { name, email, bio });
```

### SSRF — User-Controlled URLs

```python
# VULNERABLE: user controls this URL
@app.get("/fetch")
async def fetch(url: str):
    return requests.get(url).text  # attacker fetches http://169.254.169.254/...
# AWS Instance Metadata Service returns IAM credentials to the attacker
```

**SSRF bypass techniques attackers use:**

- `http://2130706433/` — decimal encoding of 127.0.0.1
- `http://017700000001/` — octal encoding
- DNS rebinding — resolves public IP during validation, then 169.254.169.254 on actual request

**Fix:** Allowlist of permitted domains. Resolve DNS server-side and validate the resolved IP is not RFC1918 or link-local before making the request.

### IDOR — Scope Every Query to the Authenticated User

```python
# VULNERABLE: checks authentication but not authorization
@app.get("/documents/{doc_id}")
async def get_doc(doc_id: UUID, user: User = Depends(get_current_user)):
    return await db.documents.find_by_id(doc_id)  # any user can read any document

# SAFE: scope to owner
async def get_doc(doc_id: UUID, user: User = Depends(get_current_user)):
    doc = await db.documents.find_by_id_and_owner(doc_id, user.id)
    if doc is None:
        raise HTTPException(status_code=404)  # 404 not 403 — don't confirm existence
    return doc
```

**Rule:** Every data access query that uses a user-supplied ID must also filter by the authenticated user's ID. Enforce this at the repository layer, not just the route handler.

### Second-Order SQL Injection

```sql
-- Phase 1: stored safely (parameterized, no injection)
INSERT INTO users (username) VALUES ("admin'--");

-- Phase 2: retrieved from DB and used in a NEW query WITHOUT parameterization
SELECT * FROM users WHERE username = 'admin'--';
-- The -- comments out the rest → authentication bypassed
```

**Rule:** Data from your own database is not inherently safe. Parameterize every query, even when the data originated from your DB.

### Prototype Pollution (JavaScript)

```javascript
function merge(target, source) {
  for (const key of Object.keys(source)) {
    if (typeof source[key] === "object") {
      merge(target[key], source[key]); // recurses into __proto__
    } else {
      target[key] = source[key];
    }
  }
}

const payload = JSON.parse('{"__proto__": {"isAdmin": true}}');
merge({}, payload);
({}).isAdmin; // true — all objects now have isAdmin: true
```

**Fix:** Explicit key check before merging:

```javascript
if (key === '__proto__' || key === 'constructor' || key === 'prototype') continue;
```

Or use `Object.create(null)` for user-key maps. Or `Map` instead of plain objects.

### ReDoS — Catastrophic Backtracking in Regex

```python
import re
# Nested quantifier with overlap — vulnerable
pattern = re.compile(r'^(a+)+$')
pattern.match('a' * 30 + '!')  # takes years — catastrophic backtracking
```

**Fix:** Use non-backtracking engines — `google-re2` (Python), `RegexOptions.NonBacktracking` (.NET 7+). In .NET, always pass a `matchTimeout` to `Regex` when the pattern or input is user-influenced. Lint regexes in CI with `safe-regex` (JS/TS). If users supply regex patterns, run them with a hard timeout.

---

## II.5 — Idempotency: Correctness for Distributed Systems

### Why It Matters

A user clicks "Pay" twice. A network timeout triggers an automatic retry. Without idempotency, they're charged twice.

### Implementation

```python
# Server stores (idempotency_key, response) before returning
INSERT INTO idempotency_keys (key, response_status, response_body)
VALUES ($1, $2, $3)
ON CONFLICT (key) DO NOTHING
RETURNING id;

# If no row returned — it's a duplicate. Return the stored response.
```

```python
# Client generates a UUID per logical operation and includes it on every retry
headers = {"Idempotency-Key": str(uuid4())}
response = requests.post("/charge", headers=headers, json=payload)
# On timeout, retry with THE SAME key — server deduplicates
```

**Rule:** Any endpoint that triggers a payment, sends an email, or creates a record must be idempotent. Accept an `Idempotency-Key` header. Store and return cached responses for duplicate keys.

---

## II.6 — Observability: If You Can't Observe It, You Can't Debug It

### Structured Logging — Required Format

```python
# WRONG: unstructured — grep archaeology at 1M logs/day
logger.error(f"Payment failed for user {user_id} amount {amount} reason {reason}")

# RIGHT: structured — queryable in 1 line
logger.error("payment_failed", extra={
    "user_id": str(user_id),
    "amount": float(amount),
    "reason": reason,
    "trace_id": trace_id_var.get(),
    "service": "payment-service",
    "duration_ms": duration_ms,
})
```

Non-negotiable fields in every log: `trace_id`, `service`, `level`, `event` (not `message`), `user_id` where applicable.

### Correlation IDs — Wire Them Through Every Layer

```python
from contextvars import ContextVar
import uuid

trace_id_var: ContextVar[str] = ContextVar('trace_id', default='')

@app.middleware("http")
async def inject_trace_id(request: Request, call_next: Callable) -> Response:
    trace_id = request.headers.get('X-Trace-ID', str(uuid.uuid4()))
    trace_id_var.set(trace_id)
    response = await call_next(request)
    response.headers['X-Trace-ID'] = trace_id
    return response
```

Every outbound call to another service must forward the `X-Trace-ID` header. Without this, debugging a failure across 5 services means correlating by timestamp — imprecise and painful.

### Minimum Metrics From Day One

| Metric                            | Method          | Why                                     |
| --------------------------------- | --------------- | --------------------------------------- |
| Request rate (req/s)              | Counter         | Baseline for anomaly detection          |
| Error rate (%)                    | Counter + ratio | First signal of degradation             |
| P50/P95/P99 latency               | Histogram       | P50 hides tail latency suffering        |
| CPU and memory utilization        | Gauge           | Predict failures before they happen     |
| DB connection pool usage          | Gauge           | Pool exhaustion = hard outage           |
| Queue depth                       | Gauge           | Backpressure signal                     |
| Business metric (e.g. orders/min) | Counter         | Separate technical from business impact |

**USE method for resources:** Utilization, Saturation, Errors.
**RED method for services:** Rate, Errors, Duration.

### Circuit Breaker — Fail Fast, Degrade Gracefully

Three states: **Closed** (normal), **Open** (fail fast), **Half-Open** (probe recovery).

```python
try:
    recommendations = circuit_breaker.call(recommendation_service.get, user_id)
except CircuitOpenError:
    recommendations = get_popular_items_fallback()  # stale but not empty
    log.warning("recommendation_service.circuit_open", extra={"user_id": str(user_id)})
```

**Rule:** Never fail empty. Every circuit breaker must have a fallback that provides partial value. A blank page is worse than stale data.

---

## II.7 — Testing: What Junior Engineers Get Wrong

### The Correct Test-First Loop

```
UNDERSTAND → RED (failing test) → GREEN (minimum code) → REFACTOR (with tests green)
```

Before writing any code, answer:

1. What are the **valid inputs**? Write tests for them.
2. What are the **invalid inputs**? Write tests that expect specific errors.
3. What are the **boundary conditions**? At exactly MAX_VALUE, at MAX_VALUE+1?
4. What **state transitions** are valid? Which are invalid (and must be rejected)?
5. What **side effects** does this have? DB writes, events, notifications?

### Property-Based Testing — Catches What Unit Tests Miss

```python
from hypothesis import given, strategies as st

@given(st.lists(st.integers()))
def test_sort_preserves_elements(lst: list[int]) -> None:
    sorted_lst = my_sort(lst)
    assert sorted(sorted_lst) == sorted(lst)
    assert len(sorted_lst) == len(lst)
    assert all(sorted_lst[i] <= sorted_lst[i+1]
               for i in range(len(sorted_lst)-1))
```

**What it catches that unit tests don't:** Empty inputs, off-by-one at boundaries, encoding round-trip failures, commutativity violations.

**Hypothesis auto-shrinks:** When a failure is found, it minimizes the input to the smallest reproducing example automatically.

### 100% Coverage Means Nothing

```python
# 100% coverage — all lines executed — but the test is wrong:
def divide(a: int, b: int) -> int | None:
    if b == 0:
        return None    # BUG: should raise ZeroDivisionError
    return a // b

def test_divide():
    assert divide(10, 2) == 5
    assert divide(5, 0) is None  # covers b==0 branch — but wrong behavior!
# Coverage: 100%. Correctness: wrong.
```

Coverage measures which lines were executed, not whether the behavior is correct. The real metric is **mutation testing** — `mutmut` (Python), `Stryker` (JS/TS), `Stryker.NET` (C#). Mutants flip operators (`>` to `>=`, `+` to `-`). If your tests don't kill the mutant, you have a blind spot.

### Mocking vs Faking — When Each Is Wrong

| Use            | When                                                                                               |
| -------------- | -------------------------------------------------------------------------------------------------- |
| **Mock**       | External services you don't own (Stripe, SendGrid, Anthropic API)                                  |
| **Fake**       | Your own infrastructure (DB → real test DB, cache → in-memory dict)                                |
| **Never mock** | Your own domain or application layer — this creates tests that verify implementation, not behavior |

**The rule:** Mock at service boundaries you don't own. Fake your own infrastructure. Never mock your own domain layer.

### Time-Dependent Code — Inject the Clock

```python
# Untestable — calls datetime.now() internally
def is_expired(user: User) -> bool:
    return datetime.now() > user.premium_expires_at

# Testable — inject time as a parameter
def is_expired(user: User, now: datetime | None = None) -> bool:
    if now is None:
        now = datetime.now()
    return now > user.premium_expires_at

# Test: deterministic, no mocking, no time-freezing library
def test_not_expired():
    user = User(premium_expires_at=datetime(2030, 1, 1))
    assert not is_expired(user, now=datetime(2025, 1, 1))
```

### Contract Testing for Independent Services

When two services are deployed independently, schema changes in one silently break the other. Contract tests catch this in CI without requiring both services to be deployed.

**Pact pattern:** Consumer defines "I expect `GET /users/{id}` to return `{id, email, role}`." Provider verifies this contract in its own CI. If the provider changes the schema, contract verification fails before deployment.

---

## II.8 — Code Quality: Metrics That Actually Matter

### Cyclomatic Complexity

Counts independent paths through code. Every `if`, `elif`, `else`, `for`, `while`, `and`, `or`, `try/except` adds 1.

| Score | Verdict                         |
| ----- | ------------------------------- |
| 1–5   | Simple, easy to test            |
| 6–10  | Moderate — testable with effort |
| 11–15 | Complex — refactor soon         |
| 15–30 | High risk — hard to maintain    |
| 30+   | Rewrite this function           |

**The test connection:** Cyclomatic complexity of N requires minimum N test cases for full branch coverage.

**Tooling:** `radon cc -a .` for Python, `eslint complexity` rule for TypeScript, `CA1502` analyzer (Microsoft.CodeAnalysis.Metrics) for C#.

### Coupling: Afferent vs Efferent

**Afferent (Ca)** — how many modules depend on this one. High Ca = load-bearing, must be stable.

**Efferent (Ce)** — how many modules this one depends on. High Ce = fragile, breaks when any dependency changes.

**Instability:** `I = Ce / (Ca + Ce)`. Range 0 (maximally stable) to 1 (maximally unstable).

**The rule:** High-Ca modules must be stable (low Ce). A module everything depends on that itself depends on everything is an architecture smell.

### The Rule of Three vs DRY

**DRY taken too far:**

- Two places both filter by user and status. You abstract into a generic filter.
- Three months later, one needs date filtering, the other needs org filtering.
- Your abstraction now has 12 parameters. Copy-paste would have been cleaner.

**Rule of Three:** Copy once — it's an example. Copy twice — it's a coincidence. Copy three times — extract the abstraction. The third copy reveals the correct abstraction axis.

**When copy-paste is correct:**

- The two things look identical now but will diverge as requirements evolve
- The shared logic is trivial (1–2 lines)
- Abstraction requires passing complex context that couples otherwise-independent modules

### File Length Limit — 500 Lines Hard Cap

If a file approaches 500 lines:

1. **Stop adding code.**
2. Ask: What does this file do? Is it doing more than one thing?
3. Extract: constants, utilities, sub-components, domain objects.
4. Commit the refactor first, then the feature.

A file over 500 lines is a cohesion failure. It does more than one thing.

---

## II.9 — Architecture: Decisions That Bite You Later

### The 8 Distributed Systems Fallacies (Every One Causes Outages)

| Fallacy                    | Failure Mode                                                                      | Fix                                                               |
| -------------------------- | --------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| The network is reliable    | Service A hangs forever waiting for B → thread pool exhausted → cascading failure | Timeout on every outbound call. Circuit breaker.                  |
| Latency is zero            | Sync call to service 6,000 miles away on every page load → P99 = 800ms            | Edge caching. Async where possible.                               |
| Bandwidth is infinite      | Uncompressed JSON at scale → CDN bill triples                                     | Compression. Protobuf for internal. Pagination always.            |
| The network is secure      | Internal calls unencrypted → lateral movement after breach                        | mTLS between services. Zero-trust networking.                     |
| Topology doesn't change    | Hardcoded IPs → autoscaling adds new nodes → old nodes unreachable                | Service discovery. Health checks.                                 |
| There is one administrator | Config change breaks another team's service silently                              | Contract testing. Semantic versioning. Separate config from code. |
| Transport cost is zero     | XML over SOAP across 20 microservices → serialization = 30% of CPU                | Binary protocols. Limit chatty inter-service calls.               |
| The network is homogeneous | Assuming all services support the same TLS version                                | Interoperability testing. Open standards.                         |

### Events vs Direct API Calls — When to Use Which

**Use a queue when:**

- Consumer can be temporarily unavailable
- Processing is slow relative to incoming rate
- You need fan-out (one event → many consumers)
- You need retry with backoff on transient failures
- Audit trail or replay capability required

**Use direct API calls when:**

- You need the response synchronously to continue
- Consistency matters more than decoupling
- Downstream failure should fail the upstream request (not fail silently)

**The trap:** Queues for everything creates "fire and forget" culture where failures are invisible. A confirmation email silently failing in a queue for 3 days is worse than a synchronous 500 error.

### Premature Abstraction Is Worse Than Duplication

**Premature:** Building a plugin system before you have two consumers of it. Writing generic event sourcing for a TODO app.

**Why this damages codebases:** Abstractions built too early ossify around the wrong axis. The dimension you thought would vary stays fixed; the dimension you didn't abstract becomes the mess.

**The deferred abstraction rule:** Wait for the third occurrence. The first two show you examples. The third reveals the correct axis of variation. Abstract then.

---

## II.10 — CAP Theorem: Applied to Real Decisions

Partition tolerance is not optional. Networks partition. The choice is Consistency vs. Availability when they do.

| System                   | CAP    | Real implication                            |
| ------------------------ | ------ | ------------------------------------------- |
| PostgreSQL (single node) | CA     | Partition takes the whole node down         |
| Cassandra                | AP     | Stale reads are normal and expected         |
| DynamoDB (single region) | CP-ish | Strong consistency available; costs latency |
| Redis Cluster            | AP     | Master failure → brief availability window  |

**Eventual consistency causing real auth bugs:**
User changes password → write goes to replica A. Auth request hits replica B (not yet synced) → old password still works for 200–2000ms. For authentication, use strong consistency reads even if it costs latency. The business consequence of stale auth state is account takeover.

---

## II.11 — Equality and Type Safety Rules (Language-Specific)

### TypeScript

```typescript
// ALWAYS === — never ==
if (score === 10) {
} // correct
if (score == "10") {
} // wrong — type coercion, ESLint error

// Never assignment in condition
const user = getUser();
if (user) {
} // correct

// Never non-null assertion — use optional chaining + explicit check
const name = user?.name ?? "Unknown"; // correct
const name = user!.name; // wrong — crashes if null

// Never any — use unknown + narrowing
const data: unknown = await response.json();
const validated = Schema.parse(data); // validate at boundary, then trust types
```

### Python

```python
# Value equality — always ==
if score == 10: pass
if status == UserStatus.ACTIVE: pass  # correct — StrEnum

# Identity — only for singletons
if value is None: pass       # correct
if score is 10: pass         # WRONG — mypy strict catches this

# isinstance() not type() ==
if isinstance(score, int): pass   # correct
if type(score) == int: pass       # wrong — doesn't handle subclasses

# No bare except
try:
    ...
except Exception as e:       # correct
    log.error("...", exc_info=True)
    raise
except:                      # wrong — catches KeyboardInterrupt, SystemExit
    pass
```

### C#

```csharp
// Nullable reference types — enabled solution-wide, treated as errors.
// Never suppress with ! ("null-forgiving") to make it compile — fix the flow.
string name = user!.Name;                 // wrong — crashes if null
string name = user?.Name ?? "Unknown";    // correct

// Equality: records give value equality for free — use them for domain values/DTOs
public sealed record Money(long AmountMinor, string Currency);
// money1 == money2 compares values, not references

// Reference types: never == for domain equality unless the type defines it.
// Strings: be explicit about comparison semantics
if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) { }  // correct
if (a.ToLower() == b.ToLower()) { }                               // wrong — culture bugs + allocations

// Pattern matching over type checks
if (shape is Circle { Radius: > 0 } c) { }   // correct — match + deconstruct
if (shape.GetType() == typeof(Circle)) { }   // wrong — no narrowing, breaks on subclass

// No dynamic — it defers type errors to runtime
dynamic data = JsonSerializer.Deserialize<dynamic>(json);   // wrong
var data = JsonSerializer.Deserialize<CreateOrderRequest>(json)
           ?? throw new ValidationException("Invalid body"); // validate at boundary, then trust types

// Exceptions: never catch-and-ignore; never catch Exception except at the top-level middleware
try { ... }
catch (Exception ex) when (ex is not OperationCanceledException)  // don't eat cancellation
{
    _logger.LogError(ex, "order_processing_failed OrderId={OrderId}", orderId);
    throw;
}
```

**Non-negotiables:** `Nullable enable` + warnings-as-errors, no `dynamic` in domain/application code, no `async void`, no `.Result`/`.Wait()`, `CancellationToken` on every I/O signature, records for value semantics, `sealed` by default.

---

## II.12 — Database Engineering: The Complete Guide

> The database is the only part of your system with permanent memory.
> Every other layer is stateless and replaceable. The database is not.
> Bad schema decisions made on day 1 cost months to fix on day 365.
> Design it right once.

---

### Primary Key Strategy — Choose Before You Create a Single Table

This decision propagates to every foreign key, every index, every API response, and every URL. Changing it later requires rewriting the entire schema.

**Option 1: `SERIAL` / `BIGSERIAL` (auto-increment integer)**

```sql
CREATE TABLE users (
    id BIGSERIAL PRIMARY KEY,
    ...
);
```

Pros: Small storage (8 bytes), perfectly sequential (fast inserts), human-readable.
Cons: Exposes row count (attacker sees `/users/1` and knows you have ~N users), impossible to generate without a DB round-trip.

**Option 2: `UUID v4` (random)**

```sql
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ...
);
```

Pros: Globally unique, safe in URLs, no enumeration attack, generated offline.
Cons: 16 bytes (vs 8), **random UUIDs destroy index locality** — each insert goes to a random page in the B-tree, causing page splits, cache misses, and write amplification. At 100M+ rows, inserts become significantly slower.

**Option 3: `ULID` / `UUID v7` — The Correct Default for New Projects**

```sql
-- UUID v7: time-ordered, 128-bit, backward compatible with UUID columns
-- Native uuidv7() is PostgreSQL 18+. On PG17 and below there is NO native v7
-- generator — use a pg extension (e.g. pg_uuidv7) or a custom SQL function,
-- or generate the id in the application layer (UUID v7 libraries exist for
-- every language). Substitute your chosen generator for uuid_generate_v7() below.
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v7(),  -- PG18+: uuidv7(); PG17 and below: extension/app-side
    ...
);
```

ULIDs and UUID v7 are **time-ordered** (first 48 bits are timestamp milliseconds). This gives you:

- Sequential inserts (like BIGSERIAL) → B-tree locality, fast inserts
- Globally unique (like UUID) → safe in URLs, no enumeration
- Offline generation
- Sortable by creation time (implicit)

**Decision rule:** Use UUID v7 / ULID for any table that will be exposed in URLs or APIs. Use BIGSERIAL for internal join tables (e.g., `post_tags`) where the ID is never exposed.

---

### Data Types — Exact Types Prevent Entire Bug Classes

**Integers**

```sql
SMALLINT    -- -32,768 to 32,767 (2 bytes)  — status codes, small counts
INTEGER     -- -2.1B to 2.1B (4 bytes)      — most counters
BIGINT      -- -9.2 quintillion (8 bytes)   — IDs, timestamps in milliseconds, row counts at scale
```

Never use `INTEGER` for a primary key that could exceed 2.1 billion rows.

**Monetary Values — Never Use FLOAT**

```sql
-- WRONG: floating point has precision errors
price FLOAT

-- RIGHT: store smallest unit (cents) as integer
price_cents INTEGER NOT NULL  -- 1999 = $19.99, never loses precision

-- OR use NUMERIC for arbitrary precision
price NUMERIC(12, 2) NOT NULL
```

**Text**

```sql
-- WRONG: VARCHAR without a reason
name VARCHAR(255)   -- why 255? where does this limit come from?

-- RIGHT: TEXT with a CHECK constraint expressing the real business rule
name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100)
```

**Timestamps**

```sql
-- ALWAYS use TIMESTAMPTZ, never TIMESTAMP
-- TIMESTAMP stores local time with no zone info — ambiguous on DST transitions
-- TIMESTAMPTZ stores UTC, displays in session time zone

created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
```

**JSONB vs Relational Columns**

```sql
-- Use JSONB when: structure is genuinely unknown at design time
preferences JSONB    -- user preferences: unpredictable keys, evolves continuously

-- Use relational columns when: you query on it, filter on it, or join on it
-- WRONG: store status as JSONB then filter by status
-- RIGHT: status TEXT NOT NULL — you filter by status constantly
status TEXT NOT NULL CHECK (status IN ('active', 'suspended', 'deleted'))
```

---

### Normalization — The Theory That Prevents Entire Classes of Bugs

**1NF — Atomic Values, No Repeating Groups**

```sql
-- VIOLATES 1NF: comma-separated values in one column
CREATE TABLE users (
    id UUID PRIMARY KEY,
    roles TEXT  -- "admin,editor,viewer" ← atomic violation
);

-- CORRECT: separate table for the multi-valued attribute
CREATE TABLE user_roles (
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role    TEXT NOT NULL,
    PRIMARY KEY (user_id, role)
);
```

**Never store multiple values in a single column.**

**3NF — No Transitive Dependencies**

```sql
-- VIOLATES 3NF: non-key column determines another non-key column
CREATE TABLE orders (
    id          UUID PRIMARY KEY,
    customer_id UUID NOT NULL,
    customer_email TEXT,    -- ← determined by customer_id, not by order id
    customer_name  TEXT     -- ← same — transitive dependency
);

-- CORRECT: customer data belongs in the customers table
CREATE TABLE orders (
    id          UUID PRIMARY KEY,
    customer_id UUID NOT NULL REFERENCES customers(id)
);
```

**Rule: Normalize first. Denormalize only with a measured performance problem and a documented reason.**

---

### Table Creation Best Practices

**The Golden Template:**

```sql
CREATE TABLE items (
    id          UUID        PRIMARY KEY DEFAULT uuid_generate_v7(),
    owner_id    UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status      TEXT        NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft', 'active', 'archived')),
    name        TEXT        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at  TIMESTAMPTZ           -- null = active, timestamp = soft-deleted
);

CREATE TRIGGER set_updated_at
    BEFORE UPDATE ON items
    FOR EACH ROW
    EXECUTE FUNCTION trigger_set_updated_at();
```

**Naming Conventions (PostgreSQL standard — snake_case everywhere):**

```sql
-- Tables: plural snake_case nouns
CREATE TABLE order_items (...);    -- correct
CREATE TABLE OrderItem (...);      -- wrong

-- Foreign keys: [referenced_table_singular]_id
user_id UUID REFERENCES users(id)  -- correct

-- Indexes: [table]_[columns]_idx
CREATE INDEX items_owner_id_idx ON items(owner_id);
CREATE UNIQUE INDEX users_email_unique_idx ON users(email);
```

---

### Indexing Strategy — The Single Biggest Lever on Query Performance

**Always index foreign keys** (PostgreSQL does NOT do this automatically):

```sql
CREATE INDEX items_owner_id_idx ON items(owner_id);
-- Without this: every JOIN on owner_id does a sequential scan of the entire table
```

**Composite Indexes — Left-Most Prefix Rule:**

```sql
-- Supports queries on (owner_id), (owner_id, status), (owner_id, status, created_at)
CREATE INDEX items_owner_status_created_idx
    ON items(owner_id, status, created_at DESC);

-- Cannot use the index (skips owner_id):
SELECT * FROM items WHERE status = 'active' ORDER BY created_at DESC;
-- → needs a separate index on (status, created_at)
```

**Partial Indexes:**

```sql
-- Index only active rows — smaller, faster
CREATE INDEX items_owner_active_idx
    ON items(owner_id, created_at DESC)
    WHERE deleted_at IS NULL;
```

**GIN Indexes — For JSONB and Full-Text Search:**

```sql
CREATE INDEX items_preferences_gin_idx ON items USING GIN (preferences);
CREATE INDEX items_name_fts_idx ON items USING GIN (to_tsvector('english', name));
```

**Rule: Always verify with `EXPLAIN (ANALYZE, BUFFERS)` before and after adding any index.**

---

### Referential Integrity — Foreign Keys Are Not Optional

**Every foreign key must have explicit `ON DELETE` behavior:**

```sql
-- CASCADE: delete children when parent is deleted
item_id UUID NOT NULL REFERENCES items(id) ON DELETE CASCADE

-- RESTRICT: prevent deleting parent if children exist (explicit default)
user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT

-- SET NULL: nullify FK when parent is deleted
editor_id UUID REFERENCES users(id) ON DELETE SET NULL
```

**Never silently orphan records.** Every FK must have a declared delete behavior.

---

### Schema Migrations — Zero-Downtime Required

**Rule 1: Never lock the table.**

```sql
-- UNSAFE on a large production table (VOLATILE default → full table rewrite under exclusive lock):
ALTER TABLE items ADD COLUMN external_ref UUID NOT NULL DEFAULT gen_random_uuid();
-- (Since PG11, a CONSTANT default like FALSE is metadata-only and safe.
--  The rewrite hits volatile defaults — and any backfill of real values still needs the 3-step dance.)

-- SAFE zero-downtime (3 steps):
ALTER TABLE items ADD COLUMN processed BOOLEAN DEFAULT FALSE;  -- constant default: fast, no rewrite
UPDATE items SET processed = FALSE
    WHERE id IN (SELECT id FROM items WHERE processed IS NULL LIMIT 10000);  -- backfill in batches (UPDATE has no LIMIT — use a subquery)
ALTER TABLE items ALTER COLUMN processed SET NOT NULL;  -- fast after backfill
```

**Rule 2: Indexes concurrently.**

```sql
-- WRONG: blocks writes during build
CREATE INDEX items_owner_idx ON items(owner_id);

-- RIGHT: no write lock
CREATE INDEX CONCURRENTLY items_owner_idx ON items(owner_id);
```

**Rule 3: Migrations are append-only.** Never modify a migration that has been applied to any environment.

**Rule 4: Backward-compatible steps only.**

```
Adding a required field:
1. ADD COLUMN (nullable)
2. Deploy app that writes + reads the field (handles null)
3. Backfill
4. ALTER COLUMN SET NOT NULL
5. Deploy app that requires the field
```

---

### Row-Level Security — Authorization at the Database Layer

```sql
ALTER TABLE items ENABLE ROW LEVEL SECURITY;
ALTER TABLE items FORCE ROW LEVEL SECURITY;

CREATE POLICY items_owner_isolation ON items
    USING (owner_id = current_setting('app.current_user_id')::UUID)
    WITH CHECK (owner_id = current_setting('app.current_user_id')::UUID);

-- Set at the start of each connection:
await pool.execute("SET LOCAL app.current_user_id = $1", str(user_id))
```

RLS enforces authorization at the database layer. Even if application code has a bug that forgets to filter by owner_id, the database rejects the query.

---

### Anti-Patterns That Appear in Every Codebase Eventually

**EAV (Entity-Attribute-Value) — The Flexibility Trap**

```sql
-- WRONG: "flexible" schema that makes every query painful
CREATE TABLE user_properties (
    user_id  UUID,
    key      TEXT,   -- "role", "plan", "locale"
    value    TEXT    -- any type, stored as text
);
-- Every query is a multi-JOIN nightmare. Use JSONB or real columns instead.
```

**Polymorphic Associations — Breaks Referential Integrity**

```sql
-- WRONG: target_id cannot be a real FK
CREATE TABLE comments (
    id          UUID PRIMARY KEY,
    target_type TEXT,  -- 'Post', 'Video', 'Product'
    target_id   UUID   -- ← no referential integrity possible
);

-- RIGHT: separate nullable FKs, constraint ensures exactly one is set
CREATE TABLE comments (
    id       UUID PRIMARY KEY,
    body     TEXT NOT NULL,
    post_id  UUID REFERENCES posts(id) ON DELETE CASCADE,
    video_id UUID REFERENCES videos(id) ON DELETE CASCADE,
    CONSTRAINT comments_exactly_one_target CHECK (
        (post_id IS NOT NULL)::INT + (video_id IS NOT NULL)::INT = 1
    )
);
```

---

### The Database Design Checklist

Before creating any table:

```
□ Primary key strategy chosen (UUID v7 for public IDs, BIGSERIAL for internal join tables)?
□ Every column has the correct type (TIMESTAMPTZ not TIMESTAMP, INTEGER cents not FLOAT)?
□ Every optional column that's NOT NULL actually required?
□ Every business rule expressed as a CHECK constraint?
□ Every FK has explicit ON DELETE behavior?
□ Is this in 3NF? If denormalized, documented reason exists?
□ Indexes on every FK column?
□ Composite index order follows left-most prefix rule?
□ EXPLAIN ANALYZE run on critical queries?
□ Migration is backward-compatible with current app code?
□ Migration uses CONCURRENTLY for index creation?
□ RLS policy created if this table holds per-user data?
□ Audit trail needed?
□ Soft delete or hard delete? If soft, view/RLS to filter automatically?
```

---

## The Mindset That Separates Top 1%

1. **Profile before optimizing.** Never guess where the bottleneck is. A 10ms optimization in a function that runs 100 times is worthless next to a 2s DB query.

2. **Know the abstraction below your abstraction.** ORM → SQL. HTTP client → TCP. asyncio → event loop. React → VDOM → DOM. You need to know both layers to debug production.

3. **Design for failure explicitly.** Every external call has a timeout, retry budget, and fallback. Every queue has a dead-letter destination. Every circuit has a fallback response.

4. **Treat security as a correctness property.** Timing-safe comparison isn't a "security feature" — it's correct string comparison for secrets. IDOR prevention isn't a feature — it's correct data access.

5. **Know what your tools don't guarantee.** asyncio doesn't prevent race conditions. 100% test coverage doesn't mean correctness. Transactions don't guarantee consistency without the right isolation level. ORMs don't prevent N+1.

6. **Defer abstraction until the third repetition.** Premature abstraction creates cages; deferred abstraction creates leverage.

7. **Make production observable from day one.** Structured logs, correlation IDs, circuit breakers with telemetry, and the RED/USE metrics. Never bolt observability on after the first outage.

8. **Scope every query to the authenticated user.** Authentication (who you are) and authorization (what you can access) are different. Enforce authorization at the data layer, not the route handler.

9. **The database is permanent. Everything else is ephemeral.** Schema decisions made today cascade for years. Design it right once, migrate safely when you must, and never trade correctness for convenience at the data layer.
