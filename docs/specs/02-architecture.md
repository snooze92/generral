# 02 – Architecture

## Tech stack

| Layer | Choice | Rationale |
|-------|--------|-----------|
| Backend | **C# / .NET 10 (LTS)**, ASP.NET Core minimal APIs | Same stack as Sonarr/Prowlarr/Sportarr, so code can be ported (see [01](01-prior-art.md#what-we-reuse)). Single self-contained binary. |
| Persistence | **EF Core** + **SQLite** (default), PostgreSQL (optional, `Later`) | Sonarr parity: single-file DB in `/config`, trivial backups. |
| Frontend | **React + TypeScript**, Vite, CSS modules, TanStack Query | Sonarr's frontend is React/TS with CSS modules, so vendored components drop in. TanStack Query instead of Redux for new code. |
| Real-time | **SignalR** hub at `/signalr/messages` | Pushes queue/progress/command/episode changes to the UI (Sonarr pattern). |
| Templating | **Scriban** | Sandboxed expressions in mappings and naming. |
| Packaging | **Docker** (multi-arch amd64/arm64) | See [12](12-deployment.md). |

## Process & components

Generral is one process (an ASP.NET Core host) that serves both the API and the built SPA.

```
                    ┌────────────────────────────── Generral ──────────────────────────────┐
                    │                                                                      │
 Browser ── HTTP ──►│  Web host  ─┬─ /api/v1/*        native API (UI)                      │
          ◄─ WS ────│             ├─ /api/v3/*        Sonarr-compat shim (Prowlarr, autobrr)│
                    │             ├─ /feed/v1/*       iCal feed                            │
                    │             ├─ /signalr         live updates                         │
                    │             └─ /*               SPA static files                     │
                    │                                                                      │
                    │  Command queue + Scheduler (Sonarr-style tasks)                      │
                    │     │                                                                │
                    │     ├─► Source engine ──fetch──► Events store                        │
                    │     │        (thesportsdb / http-json / http-html / ical / rss / …)  │
                    │     │                                                                │
                    │     ├─► Mapping engine ── Events ──► Series / Seasons / Episodes     │
                    │     │                                                                │
                    │     ├─► Search engine ── Torznab ──► Releases ──► Decision engine    │
                    │     │                                                 │              │
                    │     ├─► Download service ──► Download client (qBittorrent)           │
                    │     │                                                                │
                    │     ├─► Tracked downloads ──► Completed download handling ──► Import │
                    │     │                                            │                   │
                    │     │                                  Rename · NFO · artwork        │
                    │     │                                                                │
                    │     └─► Connect (Plex refresh, notifications)                        │
                    │                                                                      │
                    │  SQLite (/config/generral.db) · MediaCover cache · Logs · Backups    │
                    └──────────────────────────────────────────────────────────────────────┘
                         ▲                    ▲                    ▲                ▲
                    Sources (internet)   Prowlarr (Torznab)   qBittorrent      Plex Media Server
```

### Backend modules (namespaces)

| Module | Responsibility | Spec |
|--------|---------------|------|
| `Generral.Core.Sources` | Source type plugins, fetch scheduling, HTTP politeness (rate limit, caching, UA), raw → `Event` extraction. | [04](04-sources.md) |
| `Generral.Core.Mappings` | Turning events into Series/Season/Episode; numbering stability; preview; presets import/export. | [05](05-mapping.md) |
| `Generral.Core.Tv` | Series, Season, Episode entities and services; monitoring; root folders. (Not `Series`, which would clash with the entity name.) | [03](03-domain-model.md) |
| `Generral.Core.Indexers` | Indexer definitions (Torznab, later Newznab), capabilities, RSS sync, search requests. | [06](06-indexers-and-search.md) |
| `Generral.Core.DecisionEngine` | Release → episode matching, specifications (accept/reject with reasons), prioritisation. | [06](06-indexers-and-search.md) |
| `Generral.Core.Qualities` / `Profiles` | Quality parser, definitions, profiles, release profiles, (later) custom formats. | [06](06-indexers-and-search.md) |
| `Generral.Core.Download` | Download client plugins, tracked downloads, queue, blocklist, failed download handling. | [07](07-download-clients.md) |
| `Generral.Core.MediaFiles` | Import, rename, file naming, hardlink/copy, extras, recycle bin. | [08](08-import-and-organisation.md) |
| `Generral.Core.Metadata` | NFO writers, artwork download, MediaCover cache. | [08](08-import-and-organisation.md) |
| `Generral.Core.Notifications` | Connect providers (Plex, webhook, Discord, …). | [09](09-media-servers-and-notifications.md) |
| `Generral.Core.Messaging` | Command queue, events bus, scheduled tasks. | below |
| `Generral.Http` | API endpoints (native + Sonarr shim), auth, SignalR, static files. | [11](11-api.md) |

### Command queue & scheduled tasks

Like Sonarr, every long-running action is a **Command** (with a name, body, status, progress
message, start/end time), executed by a bounded worker pool and visible under *System → Tasks* and
in the UI's activity spinner. Scheduled tasks enqueue commands.

| Task | Default interval | Sonarr equivalent |
|------|------------------|-------------------|
| `RefreshSources` | 12 h (per-source override) | Refresh Series |
| `ApplyMappings` | after every source refresh, plus on mapping save | (n/a) |
| `RssSync` | 15 min (min 10, configurable; 0 disables) | RSS Sync |
| `CheckForFinishedDownloads` | 1 min | Check For Finished Download |
| `MissingEpisodeSearch` | manual / optional schedule | Missing Episode Search |
| `EpisodeSearch` (on air) | at `airDateUtc + searchDelay` per episode | (Sonarr searches on air via RSS only) |
| `Housekeeping` | 24 h | Housekeeping |
| `Backup` | 7 days | Backup |
| `HealthCheck` | 6 h + on relevant events | Check Health |
| `ApplicationUpdateCheck` | n/a in Docker (image updates) | Application Update |

### Event flow (happy path, UC1)

1. `RefreshSources` fetches TheSportsDB league 4443 for seasons `2025`, `2026` → upserts 40 `Event`s.
2. `ApplyMappings` runs the "UFC Numbered" mapping: filter keeps 14 events → upserts
   Season 2026 + Episodes, and assigns a number to any new episode. New future episodes are
   *monitored* (series monitor mode = *Future*).
3. Every 15 min, `RssSync` fetches the latest releases from all RSS-enabled indexers. The decision
   engine evaluates each release against every *wanted* episode's match rules.
4. Release `UFC.322.Main.Card.1080p.WEB.h264-GRP` matches S2026E11 and passes quality/size/seeders →
   **grab**. The download service sends it to qBittorrent with category `generral` and records a
   `TrackedDownload` + History *Grabbed*.
5. `CheckForFinishedDownloads` sees the torrent complete → import: hardlink + rename into
   `/data/media/sports/UFC/Season 2026/UFC - S2026E11 - UFC 322 - Main Card [WEBDL-1080p].mkv`,
   write `.nfo` and thumb, History *Imported*.
6. Connect: Plex refresh of `/data/media/sports/UFC`. Discord notification (if configured).
7. When seeding goals are met, the torrent is removed from qBittorrent (if *Remove Completed* is on).

## Cross-cutting concerns

- **Time:** store UTC. The UI renders in the browser's timezone, and the backend uses the container
  `TZ` for schedules and the "today" boundary. Source events may carry local times + timezone. The
  mapping normalises them to UTC (see [05](05-mapping.md#dates-and-timezones)).
- **HTTP politeness:** a shared `HttpClientFactory` with named clients (`Source`, `Indexer`,
  `DownloadClient`, `MediaServer`) gives per-host rate limits, timeouts, retries with jitter, a
  `User-Agent: Generral/<version> (+https://github.com/<org>/generral)`, and conditional GET
  (ETag/Last-Modified) for sources.
- **Logging:** Serilog to console + rolling files in `/config/logs`, levels Info/Debug/Trace
  switchable at runtime (Sonarr parity). Secrets (API keys, passwords, cookies) are redacted.
- **Health checks:** surfaced in *System → Status* and as a badge on the sidebar (Sonarr parity).
  Examples: source failing for > 24 h, mapping produces numbering conflicts, no indexers, Prowlarr
  sync stale, download client unreachable, root folder missing/unwritable, remote path mapping
  likely wrong, Plex unreachable.
- **Security:** auth required by default (Forms), with an optional "disabled for local addresses"
  setting. API key for programmatic access. Scriban templates run sandboxed (no I/O, CPU/time/loop
  limits). SSRF note: sources may fetch arbitrary URLs. This is by design for a self-hosted admin
  tool, but fetching link-local/metadata IPs (169.254.0.0/16) is blocked by default.
- **Backups:** zip of DB + `config.yaml` to `/config/Backups`, scheduled and manual, restore from UI.
