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
Every response carries `X-Application-Version: <generral version>`, except `/api/v3/*` responses,
which carry the Sonarr-compatible shim version (see below) because Prowlarr parses that header.

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
| `command` | `GET/POST /command`, `GET /command/{id}`. Names: `RefreshSources`, `RefreshSource`, `ApplyMappings`, `RssSync`, `RefreshMonitoredDownloads`, `EpisodeSearch`, `SeasonSearch`, `SeriesSearch`, `MissingEpisodeSearch`, `CutoffUnmetEpisodeSearch`, `RenameFiles`, `RenameSeries`, `Renumber`, `DownloadedEpisodesScan`, `Backup`, `CheckHealth`, `Housekeeping` |
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

Series and episode resources (M1b):

- `series`: Sonarr's SeriesResource without metadata-provider ids: `{ id, title, sortTitle,
  cleanTitle, titleSlug, status, ended, overview, network, year (0 = none), genres, images[],
  seasons[], path, rootFolderPath, qualityProfileId, seasonFolder, monitored, monitorNewItems,
  seriesType, tags, added, nextAiring, previousAiring, statistics, guid, searchSettings }`.
  `sortTitle`/`cleanTitle` are derived from the title (Sonarr's rules); `titleSlug` is generated
  (unique, `-2` suffix) when empty and kept on rename. `POST` needs `path` or `rootFolderPath` (the
  path then is root + series folder name, spec 08). `images[]` are Sonarr's `{ coverType, url,
  remoteUrl }` (`poster`, `banner`, `fanart`, `screenshot` = thumb, `clearlogo`); `images` and
  `searchSettings` omitted on `PUT` keep the stored values. `seasons[]` is `{ seasonNumber, title,
  monitored, statistics, images }`; `POST` creates them, `PUT` only applies `monitored` to existing
  seasons and, like Sonarr, sets it on all of that season's episodes. `statistics` follow Sonarr's
  counting (`episodeCount` = monitored aired episodes + those with a file; `seasonCount` excludes
  specials); orphaned episodes without a file are not counted. Validation (400): title required and
  unique case-insensitively, slug format/unique, absolute path unique, root folder absolute,
  distinct non-negative season numbers, existing tags. `DELETE` → 204; seasons, episodes, mappings
  (and versions) and links cascade. `qualityProfileId` must exist (400); `0`/missing on `POST`
  means the profile with the lowest id (*Any* on a fresh install), on `PUT` keeps the stored one.
  Root folders are not validated yet.
- `episode`: Sonarr's EpisodeResource (`seriesId, episodeFileId, seasonNumber, episodeNumber,
  absoluteEpisodeNumber, title, airDate, airDateUtc, lastSearchTime, runtime, overview, hasFile,
  monitored, unverifiedSceneNumbering: false, grabbed: false, images`) plus `numberLocked`,
  `source` (`mapping`/`manual`), `status` (`active`/`orphaned`) and `links[] { mappingId, eventId,
  partKey }`. `GET /episode` needs `seriesId` (optionally `seasonNumber`) or `episodeIds`, else 400;
  orphaned episodes without a file are hidden unless `includeOrphaned=true`. `PUT /episode/{id}`
  reads `monitored` only; `PUT /episode/monitor { episodeIds[], monitored }` returns the episodes
  (unknown ids ignored, empty list → 400); `PUT /episode/{id}/lock { numberLocked }` locks or
  unlocks the number.

Quality resources (M2b), Sonarr v3-shaped:

- `qualityprofile`: CRUD + `GET /schema` (all qualities, Sonarr's groups, nothing allowed).
  Sonarr's six default profiles are seeded on start when none exist. Validation follows Sonarr's
  (name required and unique, every quality exactly once, named groups with ids and ≥ 2 qualities,
  cutoff = an allowed item, sizes 0–1000 and ordered). `formatItems`/`*FormatScore` are returned
  fixed (`[]`, `0`, `0`, `1`) and ignored until custom formats. `DELETE` of a profile used by a
  series → 400 `QualityProfile [name] is in use.`
- `language`: `GET /language` (Sonarr's list plus `Any` -1 and `Original` -2, `{ id, name }`),
  `GET /language/{id}`. Quality profiles carry `language: { id, name }` (Radarr-style; read by
  `id`, defaults to English when omitted).
- `qualitydefinition`: `GET`, `GET /{id}`, `PUT /{id}`, `PUT /update` (list), `GET /limits`. Only
  `title` and sizes are writable; size changes are copied into every profile item (Sonarr's
  `UpdateAllSizeLimits`). `quality` is `{ id, name, source, resolution }` and is read by `id`.
- `release` (M2c): `GET /release?episodeId=` runs an interactive search and returns Sonarr's
  `ReleaseResource` (`guid, quality, qualityWeight, age, ageHours, ageMinutes, size, indexerId,
  indexer, title, mappedSeriesId, mappedSeasonNumber, mappedEpisodeNumbers, mappedEpisodeInfo[],
  approved, rejected, rejections[], publishDate, downloadUrl, magnetUrl, infoHash, infoUrl,
  commentUrl, seeders, leechers, protocol, indexerFlags, episodeRequested, downloadAllowed,
  releaseWeight, languages[]`; no scene/TVDB/custom-format fields). Results are cached 30 min;
  `POST /release { guid, indexerId, episodeIds? }` grabs a cached one (rejected ones too),
  `episodeIds` (one series) overriding the match; unknown → 404, missing guid/indexerId → 400.
  `/release/push` is not implemented yet. `GET/PUT /settings/indexer { rssSyncInterval }`.
- `releaseprofile`: `{ id, name, enabled, required[], ignored[], indexerId, tags[], excludedTags[] }`
  (`required`/`ignored` also accept a comma-separated string). At least one term is required; the
  indexer and tags must exist; a tag can't be both included and excluded. Sonarr's air-date and
  season-pack fields are not supported.

Mapping resources (M1b):

- `mapping`: `{ id, name, seriesId, sourceId, priority, enabled, definition, version }`. `definition`
  is the YAML (or JSON) text as authored and is returned verbatim. `GET /mapping?seriesId=` filters
  by series. Validation (400): name required, series and source exist (and cannot change on `PUT`),
  definition parses and its templates compile (parser/YAML errors, with the line when known, on
  `definition`). `POST`/`PUT` record a new version when the definition text differs from the last
  one and, for enabled mappings, queue `ApplyMappings { mappingId }`. `DELETE` → 204; versions and
  links cascade and the episodes no other mapping links become `orphaned`.
- `GET /mapping/{id}/versions`: `[{ version, definition, created }]`, newest first.
  `POST /mapping/{id}/apply` queues `ApplyMappings { mappingId }` and returns the command (201).
- `POST /mapping/preview { id?, seriesId?, sourceId, definition }` evaluates an unsaved definition
  against the source's stored events (no fetch) and stores nothing. With `seriesId` the series'
  episodes count (numbers in use are not reused); with `id` (a saved mapping, same series and
  source) its links count too, so `change` is what an apply would do. Without either it is a
  fresh series. Returns `{ rows[], summary, seriesType, seasons[{ seasonNumber, title }], errors[] }`;
  a row is `{ event { id, key, title, start, status }, kept, rejectedBy, episodeId,
  currentSeasonNumber, currentEpisodeNumber, seasonNumber, episodeNumber, absoluteEpisodeNumber,
  title, overview, airDate, airDateUtc, runtime, thumb, searchTerms, numberLocked, renumbered,
  change (new|changed|unchanged|orphaned|filtered|error), errors[] }`, `summary` is `{ events, kept,
  episodes, new, changed, unchanged, orphaned, filtered, errors, renumbered }`, and `errors[]` lists
  the row errors as `key: message`. An invalid definition, series or source → 400. The rendered
  path column is not returned yet (naming is not implemented).
  With `parts`, there is one row per event and part, with `partKey`, `partTitle` and `monitored`;
  `duplicateOf` names the higher-priority mapping for `change: duplicate` rows (summary
  `duplicates`).

Preset resources (M1):

- `GET /preset`: shipped then imported presets, `[{ id, name, description, seriesTitle, tags,
  sourceType (first source), sourceTypes[], requiredSecrets[] (secrets without a default),
  parameters[{ name, label, default, secret }], shipped }]`. A file that doesn't parse is skipped
  (logged).
- `POST /preset/import`: the YAML document as the body, or JSON `{ url }` / `{ yaml }`. URLs are
  fetched with the source HTTP client (SSRF guard). The document is fully validated (placeholders
  filled with defaults) and stored as `<config>/presets/<id>.yaml`, replacing an imported preset
  with the same id; a shipped preset's id → 400. Returns the preset (201).
- `POST /preset/{id}/instantiate { title?, rootFolderPath | path, monitored?, monitorNewItems?,
  seasonFolder?, qualityProfileId?, tags[], parameters{}, secrets{} }`: fills the placeholders (empty values → the
  defaults; a missing required one → 400), validates the sources (as `source.settings.<field>`),
  mappings and series (as on `POST /series`), then in one transaction reuses an existing source of
  the same type with identical settings or creates one (name suffixed ` (2)` when taken), creates
  the series and the mappings, and queues `RefreshSource` for each source (which applies the
  mappings). Returns the series (201).
- `POST /series/{id}/export`: the series, its mappings (priority order) and their sources as a
  preset (`application/yaml`, id = the title slug). Password settings and header values become
  `{{ secret.NAME }}` placeholders, declared as secret parameters without a default.
- `GET /series/{id}/renumber-preview` → `{ episodes[{ episodeId, mappingId, eventKey, partKey,
  title, seasonNumber, episodeNumber, newSeasonNumber, newEpisodeNumber }], errors[] }` (404 for an
  unknown series); nothing is stored. Command `Renumber { seriesId }` applies it (see
  [05](05-mapping.md#renumber)).

Calendar (M1):

- `GET /calendar?start=&end=&unmonitored=false&includeSeries=false&tags=` returns episode resources
  with `airDateUtc` in `[start, end]` (default: server-local today 00:00 to +2 days, like Sonarr;
  times without an offset are UTC), ordered by `airDateUtc`. Specials are included; orphaned
  episodes without a file are not. `unmonitored=false` also drops episodes of unmonitored series.
  `tags` is Sonarr's comma-separated list of tag ids or labels (a series matches any; unknown ones
  match nothing). `includeSeries=true` adds `series` (the series resource) to each episode.
- `GET /feed/v1/calendar/generral.ics?pastDays=7&futureDays=28&tags=&unmonitored=false&premieresOnly=false&asAllDay=false`
  (`text/calendar`): same selection, from today − `pastDays` to today + `futureDays`. It accepts
  `?apikey=` (calendar apps send no headers) or a UI session, and answers 401 rather than redirecting
  to the login page. Events: `UID generral_episode_<id>` (stable), `SUMMARY` "Series - SxxEyy - Title"
  (Sonarr uses `1x02`, but a year season reads better as `S2026E02`) or "Series - Title" for daily
  series, `DESCRIPTION` = overview, `CATEGORIES` = network, `STATUS` confirmed with a file and
  tentative otherwise. Times are UTC. `DTEND` = start + runtime or, without a runtime, the latest end
  of the linked events. Without either there is no `DTEND`. `asAllDay` uses the episode's
  series-local `airDate`. `premieresOnly` keeps episode 1 of non-special seasons. Titles and overviews
  are already spoiler-free when rendered (spec 05 § Spoilers), so the feed adds no further rule.

SignalR hub `/signalr/messages` broadcasts `{ name, body: { action: "updated"|"deleted"|"sync", resource } }`
for `series`, `episode`, `episodefile`, `queue`, `command`, `health`, `source`, `event`,
`mapping` (Sonarr's message format).

## Sonarr compatibility shim

### Required for Prowlarr (MVP)

See [06 – Prowlarr integration](06-indexers-and-search.md#prowlarr-integration) for the behaviour.

| Endpoint | Notes |
|----------|-------|
| `GET /api/v3/system/status` | `appName: "Generral"`, `instanceName: "Generral"`, and `version` set to a plain 4-part string that parses as a .NET `System.Version` ≥ 4.0: `4.<major>.<minor>.<patch>` of Generral (0.1.0 → `4.0.1.0`), with the real Generral version in `generralVersion`. Prowlarr only parses `version`. |
| `GET /api/v3/indexer/schema` | Torznab + Newznab schemas. |
| `GET/POST /api/v3/indexer`, `GET/PUT/DELETE /api/v3/indexer/{id}`, `POST /api/v3/indexer/test` | `forceSave` query support. Same handlers as `/api/v1/indexer`. |
| `GET/POST /api/v3/tag`, `GET/PUT/DELETE /api/v3/tag/{id}` | Not called by Prowlarr today; cheap, kept for Sonarr clients. |

The shim accepts the API key only (no UI cookie).

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
