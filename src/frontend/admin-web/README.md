# Wavio Console (admin-web)

Enterprise admin console for the Wavio WhatsApp platform. React 19 + TypeScript + Vite,
TanStack Router/Query/Table, Tailwind CSS v4 with hand-rolled shadcn-style UI primitives
(no Radix dependency). Every screen talks to the real backend through the YARP gateway —
no mocks, no fixtures.

## Run it

```bash
# 1. Infra (repo root): Postgres + RabbitMQ
docker compose up -d

# 2. Backend (src/backend/wavio): all services + gateway on :8080
dotnet run --project wavio.AppHost

# 3. Optional dev stubs for Meta's Graph API (template submit / campaign sends)
dotnet run --project tools/MetaGraphApiStub  --urls http://localhost:5199
dotnet run --project tools/MetaGraphSendApiStub --urls http://localhost:5299

# 4. This app
npm install
npm run dev          # http://localhost:5173 — the gateway's Dev CORS allowlists exactly this origin
```

Dev sign-in: `admin@wavio.local` / `Admin@123` (seeded platform admin). Platform admins act on a
tenant via the topbar tenant switcher (sends `X-Tenant-Id`). Step-up actions (campaign launch,
template delete) prompt for an OTP — in Development the accepted test code is configured in
`core.WebApi/appsettings.Development.json` (`Otp:TestCode`).

## API client

Types are generated from the services' real OpenAPI documents (Development-only endpoints):

```bash
npm run api:fetch     # pull /{service}/openapi/v1.json via the gateway into src/api/openapi/
npm run api:generate  # openapi-typescript → src/api/generated/
```

`src/api/http.ts` owns the cross-cutting behavior the backend actually implements: in-memory
bearer token + silent single-flight refresh via the HttpOnly `lg_refresh` cookie, envelope
unwrapping (`{status,message,data}` from core/WaAdmin/WaBilling) alongside raw DTOs
(WaGateway/WaIntel), 422 field-error dictionaries, the structured 403 `step_up_required`,
`Idempotency-Key` (required by `POST /messaging/api/v1/messages`), and the `X-Tenant-Id`
override. Response DTO types for the raw-DTO services are hand-mirrored in `src/api/types.ts`
with the C# source file cited per type.

## Layout

```
src/
├── api/            http core, hand-typed contracts, generated OpenAPI types, TanStack Query hooks
├── auth/           AuthProvider (JWT claims → permissions), step-up OTP dialog
├── components/     ui/ primitives, layout/ shell, shared/ (StatusBadge, states, StatCard)
└── routes/         TanStack Router file routes; _app.tsx is the authenticated layout guard
```

UI gating mirrors backend authorization: nav items and action buttons check the JWT `permissions`
claim (platform admins bypass, same as `PermissionHandler`).
