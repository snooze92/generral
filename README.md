# Generral

> A generic, Sonarr-style PVR for things that aren't TV shows.

Generral watches **arbitrary sources** (JSON APIs, web pages, iCal calendars, RSS feeds, …) for
**events**, lets you **map** those events into a *virtual* TV series (series → seasons → episodes),
then does what Sonarr does: searches your indexers (synced from **Prowlarr**), sends the best release
to **qBittorrent**, and imports / renames the result into a **Plex**-friendly library.

The original use-case is sport:

| Real-world thing                | Becomes in Plex                                                        |
|---------------------------------|------------------------------------------------------------------------|
| UFC numbered events             | `UFC/Season 2025/UFC - S2025E04 - UFC 314 - Main Card.mkv`              |
| Rugby World Cup                 | `Rugby World Cup/Season 2023/Rugby World Cup - S2023E48 - Final - South Africa vs New Zealand.mkv` |
| World Rugby Sevens Series       | `HSBC SVNS/Season 2025/HSBC SVNS - S2025E01 - Dubai - Cup Final.mkv`  |

…but nothing in Generral is sport-specific. Anything that can be scraped into a list of dated,
identifiable events can be turned into a series.

## Legal use

Generral organises media you are legally entitled to access. Only use it with content you have the
rights to. You are responsible for complying with the laws and terms of service that apply to you.
Generral ships with no presets, sources or other content.

## Status

**v0.1.0, the first release (MVP).** It covers the [roadmap](docs/specs/13-roadmap.md)'s M0–M3:
sources and mappings, Prowlarr-synced Torznab indexers, qBittorrent, import, Plex NFO + artwork and
Plex/Webhook/Discord connections. Expect rough edges; see [CHANGELOG.md](CHANGELOG.md) for what's in
and what isn't yet. The specs ([`docs/specs/README.md`](docs/specs/README.md)) describe the
behaviour in detail. Bug reports and pull requests are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

### Getting started

1. Start the container (below) and sign in with the password from the log.
2. *Settings → Media Management*: add a root folder (e.g. `/data/media/generral`).
3. *Settings → Download Clients*: add qBittorrent (category `generral`).
4. In Prowlarr: *Settings → Apps → +* → **Sonarr**, URL `http://generral:8990`, Generral's API key
   (*Settings → General*). Indexers appear in *Settings → Indexers*.
5. *Sources*: add where events come from (TheSportsDB, a JSON API, a web page, an iCal feed, …) and
   *Test* it.
6. *Series → Add New*: pick the source, write the mapping (with a live preview), then the options.
7. In Plex: a *TV Shows* library on the root folder, agent **Plex NFO Series** (PMS ≥ 1.43.1). In
   Generral, *Settings → Connect*: add Plex Media Server so Plex refreshes after each import.

## Running with Docker

```sh
docker run -d --name generral -p 8990:8990 \
  -e PUID=1000 -e PGID=1000 -e TZ=Europe/London \
  -v ./config:/config -v /srv/data:/data \
  ghcr.io/snooze92/generral:latest
docker logs generral   # first start: the initial user's password
```

See [12 – Deployment](docs/specs/12-deployment.md) and
[`docker/docker-compose.example.yml`](docker/docker-compose.example.yml). Tags: `latest` (newest
release), `X.Y.Z` / `X.Y`, and `develop` (every push to `main`). Images are `linux/amd64` and
`linux/arm64`.

## Development

Requirements: the [.NET SDK](https://dotnet.microsoft.com/download) pinned in `global.json`
(10.0.x), Node.js 24 and pnpm (`corepack enable`).

```sh
dotnet test Generral.slnx                      # unit + integration tests
dotnet run --project src/Generral.Host         # API + built UI on http://localhost:8990

cd frontend
pnpm install
pnpm build                                     # → frontend/dist, served by the backend
pnpm dev                                       # or: Vite on http://localhost:8991, proxying the API to :8990
pnpm typecheck && pnpm lint && pnpm test
```

`dotnet run` uses `src/Generral.Host/Properties/launchSettings.json`, which keeps runtime data in
`./config/` (git-ignored). On first start Generral writes `config/config.yaml` with a new API key and
logs the password of the initial `generral` user. Health check: `GET /ping`.

Before pushing, run `dotnet format Generral.slnx` and, if available, `reuse lint`
([REUSE](https://reuse.software)); CI checks both. The UI reuses Sonarr's components: see
[10 – UI § Implementation approach](docs/specs/10-ui.md#implementation-approach) before editing
anything listed in `frontend/scripts/sonarr-files.txt`. After changing entities, add a migration with
`dotnet tool restore && dotnet ef migrations add <Name> --project src/Generral.Core --output-dir Datastore/Migrations`.

## Specs at a glance

| #  | Document                                                             | What it covers                                   |
|----|----------------------------------------------------------------------|--------------------------------------------------|
| 00 | [Overview](docs/specs/00-overview.md)                                | Vision, goals, non-goals, glossary, use-cases    |
| 01 | [Prior art & reuse](docs/specs/01-prior-art.md)                      | What exists, what we reuse, what we don't        |
| 02 | [Architecture](docs/specs/02-architecture.md)                        | Components, tech stack, data flow                |
| 03 | [Domain model](docs/specs/03-domain-model.md)                        | Entities and their relationships                 |
| 04 | [Sources](docs/specs/04-sources.md)                                  | Pluggable event sources (API, HTML, iCal, …)     |
| 05 | [Mapping](docs/specs/05-mapping.md)                                  | Events → Series / Season / Episode               |
| 06 | [Indexers & release matching](docs/specs/06-indexers-and-search.md)  | Prowlarr, Torznab, decision engine, quality      |
| 07 | [Download clients](docs/specs/07-download-clients.md)                | qBittorrent (first), client abstraction          |
| 08 | [Import & organisation](docs/specs/08-import-and-organisation.md)    | Completed download handling, naming, NFO, art    |
| 09 | [Media servers & notifications](docs/specs/09-media-servers-and-notifications.md) | Plex, Jellyfin/Emby later, Connect |
| 10 | [User interface](docs/specs/10-ui.md)                                | Sonarr-consistent UI, page by page               |
| 11 | [HTTP API](docs/specs/11-api.md)                                     | Native API + Sonarr-compat shim for Prowlarr     |
| 12 | [Deployment](docs/specs/12-deployment.md)                            | Docker image, volumes, compose, CI               |
| 13 | [Roadmap](docs/specs/13-roadmap.md)                                  | Milestones, MVP scope, open questions            |

## License

Generral is free software, licensed under the **GNU General Public License v3.0 or later**
([`LICENSE`](LICENSE)). It reuses code from Sonarr (GPL-3.0-or-later) with attribution; see
[`COPYRIGHT.md`](COPYRIGHT.md) and [01 – Prior art § Licensing](docs/specs/01-prior-art.md#licensing).
