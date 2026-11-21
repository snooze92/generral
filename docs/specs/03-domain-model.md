# 03 – Domain model

Entity names follow Sonarr where a concept exists there. New concepts are **Source**, **Event**,
**Mapping**, **EpisodeLink**.

```
Source 1───* Event
   │            │
   │            │ (filtered, transformed by)
   ▼            ▼
Mapping *───1 Series 1───* Season 1───* Episode 1───0..1 EpisodeFile
   │                                       │
   └───────── EpisodeLink (event_key[,part] ⇄ episode) ──┘

Episode *───* History, Blocklist, TrackedDownload (via Queue)
Series *───1 QualityProfile, *───* Tag, 1───1 RootFolder
```

## Source

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | |
| `name` | string | Unique. |
| `type` | string | Plugin id: `thesportsdb`, `http-json`, `http-html`, `ical`, `rss`, `static`, `script` (see [04](04-sources.md)). |
| `settings` | JSON | Type-specific, validated by the plugin schema. Secrets are stored encrypted-at-rest with a key in `/config`. |
| `refreshInterval` | duration | Default 12 h. |
| `enabled` | bool | |
| `lastRefresh`, `lastSuccess`, `lastError` | timestamps / string | Shown in UI and used by health checks. |
| `meta` | JSON | Source-level data from the last successful fetch (e.g. TheSportsDB league), `source.meta` in mappings. |
| `tags` | int[] | |

## Event

A normalised record produced by a source. Events are **upserted by `(sourceId, key)`**, and updates
are versioned so that the UI can show what changed.

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | |
| `sourceId` | int | |
| `key` | string | **Stable unique id within the source** (e.g. TheSportsDB `idEvent`, a URL slug, an ICS `UID`). Required. If a source has no natural key, the source config MUST define one (template over fields). |
| `title` | string | Required. |
| `start` | datetime (UTC) | Optional but needed for date-based features. |
| `end` | datetime (UTC) | Optional. |
| `timezone` | IANA tz | Original timezone if known. |
| `status` | enum | `scheduled`, `postponed`, `cancelled`, `completed`, `unknown`, `gone` (set by refresh, see [04](04-sources.md#fetch-semantics)). |
| `fields` | JSON object | **All extracted fields**, plus raw source payload under `_raw` (truncated to a size cap). This is what mapping templates read. |
| `images` | `{poster?, fanart?, thumb?, banner?, logo?}` URLs | Optional. |
| `firstSeen`, `lastSeen`, `lastChanged` | timestamps | An event missing from refreshes for N consecutive runs is marked `gone` (never auto-deleted while it is linked to a file). |
| `missedRefreshes` | int | Consecutive successful refreshes that did not return the event (drives `gone`). |
| `hash` | string | Content hash, used to detect changes. |

### EventRevision

Snapshot of an event *before* a change (`title`, `start`, `end`, `status`, `fields`, `images`, `hash`,
`created`), one row per detected change, kept for 30 days. Used for the "what changed" view.

## Series

Mirrors Sonarr's Series where sensible.

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | |
| `title`, `sortTitle`, `cleanTitle` | string | `cleanTitle` is used for path generation and matching. |
| `titleSlug` | string | URL slug. |
| `year` | int? | Optional; used in folder name if the naming format includes it. |
| `overview`, `network`, `genres[]`, `status` (`continuing`/`ended`) | | Shown in UI and written to `tvshow.nfo`. |
| `images` | poster/fanart/banner/logo | From a source, a URL, or an upload. |
| `seriesType` | enum | `standard` (SxxEyy), `daily` (date-based), see [05](05-mapping.md#numbering-strategies). |
| `path` | string | Absolute path = root folder + folder name. |
| `rootFolderPath` | string | |
| `qualityProfileId` | int | |
| `monitored` | bool | |
| `monitorNewItems` | enum | `all`, `none`. Sonarr parity: whether newly created *seasons* get monitored. |
| `seasonFolder` | bool | Sonarr parity. |
| `tags` | int[] | Used for indexer/download client/notification/release-profile scoping (Sonarr semantics). |
| `searchSettings` | object | Default query templates and match rules (overridable per mapping). See [06](06-indexers-and-search.md#per-series-search-settings). |
| `guid` | string | Stable `generral://series/<uuid>`; written to NFO `<uniqueid type="generral">`. |
| `added` | datetime | |

## Season

| Field | Type | Notes |
|-------|------|-------|
| `seriesId`, `seasonNumber` | int, int | Composite identity. `0` = Specials. Season numbers may be years (e.g. `2025`). |
| `title` | string? | Optional display name (e.g. `2024-25`), written as `<namedseason>` in `tvshow.nfo`. |
| `monitored` | bool | |
| `images` | poster? | |

## Episode

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | |
| `seriesId`, `seasonNumber`, `episodeNumber` | | `episodeNumber` unique within a season. |
| `absoluteEpisodeNumber` | int? | Optional; e.g. the UFC event number. Usable in naming and search. |
| `title` | string | Rendered by mapping. |
| `overview` | string? | Rendered by mapping. Spoiler-free by default (see [05](05-mapping.md#spoilers)). |
| `airDateUtc` | datetime? | |
| `airDate` | date? | Local date (series timezone) – used for daily series and for NFO `<aired>`. |
| `runtime` | minutes? | |
| `monitored` | bool | |
| `hasFile`, `episodeFileId` | | |
| `images` | thumb? | |
| `searchTerms` | object | Rendered query strings + match rules for this episode (computed from mapping, cached, shown in UI). |
| `lastSearchTime` | datetime? | |
| `numberLocked` | bool | `true` once the number has been published (see [05](05-mapping.md#numbering-stability)). |
| `source` | `mapping` \| `manual` | Manual episodes are user-created and never touched by mappings. |
| `status` | `active` \| `orphaned` | `orphaned` = no longer produced by any mapping (event gone, or filtered out). Orphaned episodes with files are kept. Without files they are hidden, and deleted after a grace period. |

## EpisodeLink

Joins an **Event (+ optional Part)** to the **Episode** produced from it. This is what keeps
numbering stable across refreshes.

| Field | Type | Notes |
|-------|------|-------|
| `mappingId` | int | |
| `eventId` | int | |
| `partKey` | string? | e.g. `main-card`. Null when the mapping does not expand parts. |
| `episodeId` | int | |
| `createdAt` | datetime | |

Unique on `(mappingId, eventId, partKey)`. Storage: "no part" is stored as `""` rather than NULL
(SQLite treats NULLs as distinct in unique indexes), exposed as `EpisodeLink.NoPart`.

## Mapping

See [05 – Mapping](05-mapping.md) for full semantics.

| Field | Type | Notes |
|-------|------|-------|
| `id` | int | |
| `name` | string | |
| `seriesId` | int | Target series. A series may have several mappings (e.g. two sources feeding one series). |
| `sourceId` | int | |
| `priority` | int | When two mappings want the same slot (season+episode), the lower number wins and the loser gets a health warning. |
| `definition` | YAML/JSON | `filter`, `parts`, `season`, `episode`, `title`, …, `search`. |
| `enabled` | bool | |
| `version` | int | Incremented on change. Every version is kept for diff/rollback in **MappingVersion** (`mappingId`, `version`, `definition`, `created`; unique on `(mappingId, version)`). |

Deleting a Source referenced by a Mapping is refused (FK `Restrict`); deleting a Series cascades to
its seasons, episodes, mappings and links. Events cascade from their Source.

### Storage notes

JSON-shaped values (`settings`, `fields`, `images`, `definition`, `tags`, `genres`, `searchSettings`,
`searchTerms`) are TEXT columns holding camelCase JSON. Enums are stored as their names. All
timestamps are UTC. `Series.titleSlug` and `Series.guid` are unique; `Source.name` and `Tag.label`
are unique case-insensitively.

## EpisodeFile

Sonarr parity: `path`, `relativePath`, `size`, `dateAdded`, `quality` (+ revision), `releaseGroup`,
`sceneName` (original release title), `mediaInfo` (via ffprobe, `Later`), `languages`.

## Indexer, DownloadClient, Notification, QualityProfile, QualityDefinition, ReleaseProfile, Tag, RootFolder, RemotePathMapping, History, Blocklist, Command, Backup, Log

Same shape and semantics as Sonarr v4 (v3 API). The API resources MUST be shape-compatible with
Sonarr's (field names, `fields[]` arrays for provider settings, `implementation`, `configContract`,
`tags`), because both the UI components and Prowlarr depend on that shape. See [11](11-api.md).
