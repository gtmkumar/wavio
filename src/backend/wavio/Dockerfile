# Shared multi-stage Dockerfile for every Wavio service (core.WebApi, the 5 wa-*
# WebApis, wavio.Gateway). One Dockerfile + build args (issue #12) instead of 7
# near-identical copies — build context is this directory (src/backend/wavio).
#
# Build (matches docker-compose.prod.yml's `build:` blocks):
#   docker build -f Dockerfile \
#     --build-arg PROJECT_PATH=core.WebApi/core.WebApi.csproj \
#     --build-arg ASSEMBLY_NAME=core.WebApi \
#     -t wavio-core .
#
# CI builds+pushes these to GHCR (see .github/workflows/deploy.yml); the VPS only
# ever pulls, never builds (docs/BUILD_PLAN.md "Runtime: Docker Compose on the VPS").

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${DOTNET_SDK_IMAGE} AS build
ARG PROJECT_PATH
WORKDIR /src
COPY . .
RUN dotnet restore "${PROJECT_PATH}"
RUN dotnet publish "${PROJECT_PATH}" \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM ${DOTNET_RUNTIME_IMAGE} AS final
ARG ASSEMBLY_NAME

# curl: used by the compose healthcheck. wavio.ServiceDefaults only maps /health
# and /alive in Development (security consideration, by design — see
# Extensions.cs) — production has no dedicated health route, so the healthcheck
# instead confirms Kestrel is accepting and answering HTTP requests at all
# (any response, including 404, means the app is alive; curl without --fail
# treats that as success).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

RUN groupadd --system wavio \
    && useradd --system --gid wavio --no-create-home --shell /usr/sbin/nologin wavio \
    && chown -R wavio:wavio /app
USER wavio

ENV ASSEMBLY_NAME=${ASSEMBLY_NAME}
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080

# "$@" must be forwarded explicitly — without it, any args passed at `docker run
# <image> <args>` (e.g. wavio.DbMigrator's --connection-string/--migrations-dir)
# are silently swallowed by the shell wrapper instead of reaching dotnet. The
# trailing "--" becomes $0 inside the sh -c script; docker run's CMD args become
# $1, $2, ... which "$@" then expands.
ENTRYPOINT ["sh", "-c", "exec dotnet ${ASSEMBLY_NAME}.dll \"$@\"", "--"]
