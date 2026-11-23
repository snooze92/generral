# syntax=docker/dockerfile:1.7
# Generral container image. See docs/specs/12-deployment.md.

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=24

# --- frontend -----------------------------------------------------------------
# Builds the SPA when frontend/ exists; otherwise produces an empty UI folder (API-only image).
FROM --platform=$BUILDPLATFORM node:${NODE_VERSION}-alpine AS frontend
WORKDIR /src
RUN corepack enable
COPY . /context
RUN mkdir -p /out && \
    if [ -f /context/frontend/package.json ]; then \
      cp -r /context/frontend /src/frontend && cd /src/frontend && \
      corepack install && pnpm install --frozen-lockfile && pnpm build && cp -r dist/. /out/; \
    else \
      echo "No frontend/ in the build context, building an API-only image"; \
    fi

# --- backend ------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS backend
ARG VERSION=0.0.0-dev
ARG COMMIT=unknown
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Generral.Core/Generral.Core.csproj src/Generral.Core/
COPY src/Generral.Http/Generral.Http.csproj src/Generral.Http/
COPY src/Generral.Host/Generral.Host.csproj src/Generral.Host/
RUN dotnet restore src/Generral.Host/Generral.Host.csproj
COPY src/ src/
# Framework-dependent and architecture-neutral (run with `dotnet`), so the same build output
# works on amd64 and arm64 and on musl, and the SDK stage only runs once per multi-arch build.
RUN dotnet publish src/Generral.Host/Generral.Host.csproj \
      -c Release --no-restore -p:UseAppHost=false \
      -p:Version="$VERSION" -p:SourceRevisionId="$COMMIT" \
      -o /app && \
    # The runtime image is Alpine, so only the musl native assets (SkiaSharp, SQLite) are ever loaded.
    find /app/runtimes -mindepth 1 -maxdepth 1 ! -name linux-musl-x64 ! -name linux-musl-arm64 -exec rm -rf {} + && \
    find /app/runtimes -name '*.pdb' -delete

# --- runtime ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS runtime
ARG VERSION=0.0.0-dev
ARG COMMIT=unknown

LABEL org.opencontainers.image.title="Generral" \
      org.opencontainers.image.description="A generic, Sonarr-style PVR for things that aren't TV shows" \
      org.opencontainers.image.licenses="GPL-3.0-or-later" \
      org.opencontainers.image.source="https://github.com/snooze92/generral" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$COMMIT"

RUN apk add --no-cache tzdata icu-libs su-exec && \
    addgroup -g 1000 generral && \
    adduser -D -H -u 1000 -G generral -s /sbin/nologin generral && \
    mkdir -p /config /data && chown generral:generral /config

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS= \
    DOTNET_RUNNING_IN_CONTAINER=true \
    GENERRAL__CONFIGDIR=/config \
    GENERRAL__PORT=8990 \
    PUID=1000 \
    PGID=1000 \
    UMASK=022

COPY --from=backend /app /app
COPY --from=frontend /out /app/UI
COPY LICENSE COPYRIGHT.md /app/
COPY docker/entrypoint.sh /entrypoint.sh

VOLUME ["/config"]
EXPOSE 8990
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD wget -qO- "http://127.0.0.1:${GENERRAL__PORT}/ping" || exit 1

ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "/app/Generral.Host.dll"]
