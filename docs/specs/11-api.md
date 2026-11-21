# 11 – HTTP API

Three route families, a split also used by Sportarr:

| Prefix | Purpose | Stability |
|--------|---------|-----------|
| `/api/v1/*` | **Native Generral API**, used by the UI and documented with OpenAPI at `/api/v1/openapi.json` (Scalar/Swagger UI at `/docs`, `Later`). | Versioned; breaking changes go to `/api/v2`. |
| `/api/v3/*` | **Sonarr v3 compatibility shim** for Prowlarr, autobrr, dashboards (Homepage, Organizr), Notifiarr-style tools, Maintainerr, … | Matches Sonarr's contract for the endpoints listed below. No Generral-only features go here. |
| `/feed/v1/*` | iCal feed | |

> Sonarr itself serves its v3 API at `/api/v3` and has no `/api/v1` (that is Prowlarr's prefix), so
> using `/api/v1` for the native API doesn't collide with anything a Sonarr client expects.

Auth for all: `X-Api-Key` header or `?apikey=`. Forms-auth cookies are also accepted for the UI.
Every response carries `X-Application-Version: <generral version>`, and the shim reports a
Sonarr-compatible version (see below).

## Native API (selected resources)

Resource shapes reuse Sonarr's where the concept is the same, so that vendored UI components work.

| Resource | Endpoints |
|----------|-----------|
| `series` | `GET/POST /series`, `GET/PUT/DELETE /series/{id}`, `GET /series/{id}/renumber-preview`, `POST /series/{id}/export` (preset YAML) |
| `episode` | `GET /episode?seriesId=&seasonNumber=`, `GET/PUT /episode/{id}`, `PUT /episode/monitor`, `PUT /episode/{id}/lock` |
| `source` | CRUD, `GET /source/schema`, `POST /source/test`, `POST /source/{id}/refresh`, `GET /source/{id}/events`, `GET /source/{id}/raw` (cached last response) |
| `event` | `GET /event/{id}` (with revisions) |
| `mapping` | CRUD, `POST /mapping/preview` (unsaved definition → preview rows), `GET /mapping/{id}/versions`, `POST /mapping/{id}/apply` |
| `preset` | `GET /preset` (gallery), `POST /preset/import` (file or URL), `POST /preset/{id}/instantiate` |
| `release` | `GET /release?episodeId=` (interactive search), `POST /release` (grab, optionally with episode override), `POST /release/push` |
| `queue`, `history`, `blocklist`, `wanted/missing`, `wanted/cutoff`, `calendar`, `manualimport` | Sonarr-shaped |
| `indexer`, `downloadclient`, `notification`, `metadata`, `qualityprofile`, `qualitydefinition`, `releaseprofile`, `tag`, `rootfolder`, `remotepathmapping`, `config/{naming,mediamanagement,host,ui,indexer,downloadclient}` | Sonarr-shaped, including `/schema` and `/test` for providers |
| `command` | `GET/POST /command`, `GET /command/{id}`. Names: `RefreshSources`, `RefreshSource`, `ApplyMappings`, `RssSync`, `EpisodeSearch`, `SeasonSearch`, `SeriesSearch`, `MissingEpisodeSearch`, `CutoffUnmetEpisodeSearch`, `RenameFiles`, `RenameSeries`, `Renumber`, `DownloadedEpisodesScan`, `Backup`, `CheckHealth`, `Housekeeping` |
| `system` | `/system/status`, `/system/task`, `/system/backup`, `/health`, `/log`, `/log/file` |

Source, event and command resources (M1a):

- `source`: `{ id, name, type, typeName, enabled, refreshInterval (minutes, ≥ 15), tags, fields[],
  lastRefresh, lastSuccess, lastError, nextRefresh, eventCount (not gone) }`. `fields[]` is Sonarr's
  provider field list (`order`, `name`, `label`, `value`, `type`, `helpText`, `unit`, `placeholder`,
  `advanced`, `privacy`, `selectOptions`); only `name`/`value` are read back. Password fields are
  returned as `********`, and sending that value back keeps the stored one. `GET /source/schema`
  returns one such resource per type with the default values. Validation (400, Sonarr's shape):
  unique name (case-insensitive), known type, the type's own validation, `refreshInterval`.
  `DELETE` → 204, or 400 while a mapping uses the source.
- `POST /source/test` takes a (possibly unsaved) source resource and returns
  `{ success, error?, events[], itemErrors[], responses[], meta, warnings[] }`; nothing is stored.
  A failed fetch is `200` with `success: false`.
- `POST /source/{id}/refresh` enqueues `RefreshSource { sourceId }` and returns the command (201).
  `GET /source/{id}/events?includeGone=false` lists events by start; `GET /event/{id}` adds
  `revisions[]` (newest first). `GET /source/{id}/raw` is not implemented yet.
- `command`: `{ id, name, commandName, body, status, message, queued, started, ended, duration,
  trigger }`. `POST /command` takes `{ name, ...body }` like Sonarr (e.g.
  `{ "name": "RefreshSources", "force": true }`); unknown names and invalid bodies → 400.
  `GET /command` lists the retained commands, newest first.
- `tag`: `{ id, label }`, Sonarr's validation (required, `a-z`, `0-9`, `-`), stored lowercase and
  unique regardless of case; `POST` with an existing label returns that tag. `GET /tag/detail`
  (and `/tag/detail/{id}`) returns `{ id, label, sourceIds, seriesIds }`. Unlike Sonarr, `DELETE`
  also works for a tag in use: it removes the id from every source and series in one transaction.
  `source.tags` must reference existing tags (400 on `tags` otherwise).

SignalR hub `/signalr/messages` broadcasts `{ name, body: { action: "updated"|"deleted"|"sync", resource } }`
for `series`, `episode`, `episodefile`, `queue`, `command`, `health`, `source`, `event`,
`mapping` (Sonarr's message format).

## Sonarr compatibility shim

### Required for Prowlarr (MVP)

See [06 – Prowlarr integration](06-indexers-and-search.md#prowlarr-integration) for the behaviour.

| Endpoint | Notes |
|----------|-------|
| `GET /api/v3/system/status` | `appName: "Generral"`, `instanceName: "Generral"`, and `version` set to a plain 4-part string that parses as a .NET `System.Version` ≥ 4.0 (e.g. `4.90.0.123`, with major `4` reserved for the shim and the real Generral version in `generralVersion`). Prowlarr only parses `version`. |
| `GET /api/v3/indexer/schema` | Torznab + Newznab schemas. |
| `GET/POST /api/v3/indexer`, `GET/PUT/DELETE /api/v3/indexer/{id}`, `POST /api/v3/indexer/test` | `forceSave` query support. |

The `appName` question is settled in favour of **`Generral`**: Prowlarr doesn't check it, and
reporting `Sonarr` would mislead dashboards and support requests.

### Nice to have (Later)

| Endpoint | Consumer |
|----------|----------|
| `POST /api/v3/release/push` | autobrr (instant grabs from IRC announces) |
| `GET /api/v3/series`, `GET /api/v3/series/{id}` | Homepage/Organizr widgets, Maintainerr. Virtual series get `tvdbId: 0`, or a stable synthetic id in a reserved range (900 000 000+ like Sportarr) if a consumer requires non-zero. |
| `GET /api/v3/calendar`, `GET /api/v3/queue`, `GET /api/v3/wanted/missing`, `GET /api/v3/history` | Dashboards, Notifiarr |
| `GET /api/v3/episodefile`, `DELETE /api/v3/episodefile/{id}` | Maintainerr |
| `POST /api/v3/command` (`RefreshSeries`, `RssSync`, `MissingEpisodeSearch`) | Scripts, Unpackerr's "notify Sonarr" |
| `GET /api/v3/health`, `GET /api/v3/diskspace` | Dashboards |
