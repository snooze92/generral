# 12 – Deployment

## Docker image

- **Name:** `ghcr.io/<org>/generral` (also Docker Hub `<org>/generral`, optional).
- **Platforms:** `linux/amd64`, `linux/arm64`.
- **Tags:** `latest` (stable), `develop` (main branch), `X.Y.Z`, `X.Y`, `sha-<short>`.
- **Port:** `8990`.
- **Conventions (linuxserver.io-compatible):** `PUID`, `PGID`, `UMASK` (default `022`), `TZ`.
- **Volumes:** `/config` (DB, `config.yaml`, logs, backups, MediaCover cache, user presets), and
  `/data` (recommended single mount containing both `torrents/` and `media/`, so hardlinks work,
  following the [TRaSH Guides](https://trash-guides.info/File-and-Folder-Structure/) layout).
- **Healthcheck:** `GET /ping` (unauthenticated, returns `{"status":"OK"}`).
- **Runs as** `PUID:PGID` via a small entrypoint (`su-exec`/`gosu`), and never as root after start.
- **Size target:** < 150 MB compressed.

### Dockerfile (outline)

```dockerfile
# syntax=docker/dockerfile:1.7

# --- frontend ---
FROM --platform=$BUILDPLATFORM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN corepack enable && pnpm install --frozen-lockfile
COPY frontend/ ./
RUN pnpm build                                   # → /src/frontend/dist

# --- backend ---
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
WORKDIR /src
COPY src/ ./src/
COPY Directory.Build.props global.json ./
RUN dotnet publish src/Generral.Host/Generral.Host.csproj \
      -c Release -a $TARGETARCH --self-contained false \
      -p:Version=${VERSION:-0.0.0} -o /app

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache tzdata icu-libs su-exec sqlite-libs ffmpeg   # ffprobe for MediaInfo (Later)
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    GENERRAL__PORT=8990 \
    GENERRAL__CONFIGDIR=/config
COPY --from=backend /app /app
COPY --from=frontend /src/frontend/dist /app/UI
COPY presets/ /app/presets/
COPY docker/entrypoint.sh /entrypoint.sh
VOLUME ["/config"]
EXPOSE 8990
HEALTHCHECK --interval=30s --timeout=5s CMD wget -qO- http://127.0.0.1:8990/ping || exit 1
ENTRYPOINT ["/entrypoint.sh"]
CMD ["/app/Generral.Host"]
```

## Configuration

Precedence: environment variables > `/config/config.yaml` > defaults. Like Sonarr's `config.xml`,
`config.yaml` holds only **host-level** settings. Everything else lives in the DB and is edited in
the UI.

| Env var | `config.yaml` key | Default |
|---------|-------------------|---------|
| `GENERRAL__PORT` | `port` | `8990` |
| `GENERRAL__URLBASE` | `urlBase` | empty (e.g. `/generral` behind a reverse proxy) |
| `GENERRAL__BINDADDRESS` | `bindAddress` | `*` |
| `GENERRAL__APIKEY` | `apiKey` | generated on first start |
| `GENERRAL__AUTHMETHOD` | `authenticationMethod` | `Forms` (`External` for SSO proxies; Sonarr's deprecated `Basic` is not supported) |
| `GENERRAL__AUTHREQUIRED` | `authenticationRequired` | `Enabled` (`DisabledForLocalAddresses`) |
| `GENERRAL__LOGLEVEL` | `logLevel` | `info` |
| `GENERRAL__DB__TYPE` / `__CONNECTIONSTRING` | `database.*` | `sqlite` / `/config/generral.db` |
| `GENERRAL__ALLOWSCRIPTSOURCES` | `allowScriptSources` | `false` |
| `GENERRAL__INSTANCENAME` | `instanceName` | `Generral` |
| `GENERRAL__CONFIGDIR` | n/a | `/config` in Docker, `~/.config/Generral` otherwise |
| `GENERRAL__UIDIR` | n/a | `<app>/UI` (the built SPA; for development, point it at `frontend/dist`) |
| `GENERRAL__AUTH__USERNAME` / `__PASSWORD` | n/a (first start only) | `generral` / random, printed once to the log |

Invalid values (e.g. `GENERRAL__PORT=abc`) stop the process at startup with a message naming the
variable. Settings fixed by an environment variable are shown read-only in *Settings → General*.

On first start with Forms authentication, Generral creates the single user from
`GENERRAL__AUTH__USERNAME`/`GENERRAL__AUTH__PASSWORD`, or `generral` with a random password written
to the log (Sonarr does the same since v4.0.17). After 5 failed sign-ins from one address within
10 minutes, that address is locked out for 5 minutes.

## Example `docker-compose.yml`

```yaml
services:
  generral:
    image: ghcr.io/<org>/generral:latest
    container_name: generral
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/London
    volumes:
      - ./config/generral:/config
      - /srv/data:/data            # /data/torrents/generral, /data/media/sports
    ports:
      - 8990:8990
    restart: unless-stopped

  prowlarr:
    image: lscr.io/linuxserver/prowlarr:latest
    environment: [PUID=1000, PGID=1000, TZ=Europe/London]
    volumes: [./config/prowlarr:/config]
    ports: [9696:9696]
    restart: unless-stopped

  qbittorrent:
    image: lscr.io/linuxserver/qbittorrent:latest
    environment: [PUID=1000, PGID=1000, TZ=Europe/London, WEBUI_PORT=8080]
    volumes:
      - ./config/qbittorrent:/config
      - /srv/data/torrents:/data/torrents   # same path as seen by Generral → no remote path mapping
    ports: [8080:8080, 6881:6881, 6881:6881/udp]
    restart: unless-stopped

  plex:
    image: lscr.io/linuxserver/plex:latest
    network_mode: host
    environment: [PUID=1000, PGID=1000, TZ=Europe/London, VERSION=docker]
    volumes:
      - ./config/plex:/config
      - /srv/data/media:/data/media
    restart: unless-stopped
```

Wiring: Generral → Download client qBittorrent `qbittorrent:8080`, category `generral` (qBittorrent
save path `/data/torrents/generral`). Prowlarr → Apps → Sonarr → `http://generral:8990`. Generral →
Connect → Plex `host.docker.internal` / LAN IP `:32400`. Plex TV library on `/data/media/sports`
with agent *Plex NFO Series*.

## Repository layout (target)

```
generral/
├── docs/specs/               # this spec
├── src/
│   ├── Generral.Host/        # ASP.NET Core host, Program.cs, static UI
│   ├── Generral.Http/        # endpoints, auth, SignalR, Sonarr shim
│   ├── Generral.Core/        # domain, services, plugins
│   ├── Generral.Core.Test/
│   └── Generral.Integration.Test/   # Testcontainers: qBittorrent, Prowlarr, mock Torznab, mock sources
├── frontend/                 # React/TS (Vite); src/Components vendored from Sonarr
├── presets/                  # shipped presets (YAML) + tests with recorded source fixtures
├── docker/                   # entrypoint.sh, s6/compose examples
├── Dockerfile
├── COPYRIGHT.md  LICENSE (GPL-3.0)  LICENSES/  REUSE.toml
└── .github/workflows/        # ci.yml, docker.yml, presets.yml
```

## CI (GitHub Actions)

| Workflow | Trigger | Steps |
|----------|---------|-------|
| `ci.yml` | PR, push to `main` | `dotnet build`, `dotnet format --verify-no-changes`, pending-migration check, `dotnet test` (unit + integration; Testcontainers from M2), `pnpm lint/typecheck/test`, preset fixture tests (M1), `reuse lint` |
| `docker.yml` | push to `main` (→ `develop`), tag `v*` (→ `X.Y.Z`, `X.Y`, `latest`) | `docker/setup-qemu` + `setup-buildx`, multi-arch `build-push-action` with GHA cache, push to GHCR, SBOM + provenance attestations, Trivy scan |
| `presets.yml` | nightly | Run every shipped preset against the **live** source (not fixtures) and open an issue when one breaks (layout change detection) |
