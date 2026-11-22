# 06 – Indexers, search & release matching

## Indexers

### Types

| Type | MVP | Notes |
|------|-----|-------|
| **Torznab** | MVP | Primary. Normally created by Prowlarr sync, but can be added by hand. |
| Newznab | Later | Same code path as Torznab. Requires a Usenet download client. |
| Torrent RSS feed | Later | Plain RSS feed from a tracker (Sonarr parity). |

Indexer settings follow Sonarr exactly (field names matter for Prowlarr, see below):
`name`, `enableRss`, `enableAutomaticSearch`, `enableInteractiveSearch`, `priority` (1–50, default 25),
`downloadClientId` (optional pin), `tags`, and `fields[]`: `baseUrl`, `apiPath` (`/api`), `apiKey`,
`categories` (int[]), `animeCategories` (int[], accepted and ignored), `animeStandardFormatSearch`
(accepted and ignored), `additionalParameters`, `minimumSeeders` (default 1),
`seedCriteria.seedRatio`, `seedCriteria.seedTime` (minutes), `seedCriteria.seasonPackSeedTime`,
`rejectBlocklistedTorrentHashesWhileGrabbing`.

Default categories for a new Torznab indexer: `5000` (TV), `5060` (TV/Sport). Users commonly add
`2000`-range (Movies) too, because some trackers file events like boxing PPVs there.

### Prowlarr integration

Prowlarr has no "Generral" application type, so **the user adds Generral in Prowlarr as a
`Sonarr` application**: *Prowlarr → Settings → Apps → + → Sonarr*, Prowlarr Server
`http://prowlarr:9696`, Sonarr Server `http://generral:8990`, API key from *Generral → Settings →
General*, Sync Categories `TV` + `TV/Sport` (+ `Movies` if wanted), Sync Level *Full Sync*.

For this to work, Generral MUST implement the subset of the Sonarr v3 API that Prowlarr's
`SonarrV3Proxy` calls (verified against Prowlarr `develop`, 2026-11):

| Call | Requirement |
|------|-------------|
| `GET /api/v3/system/status` | Returns Sonarr-shaped status JSON with `version` ≥ `4.0.0.0`. |
| `GET /api/v3/indexer/schema` | MUST include entries with `implementation: "Torznab"` **and** `implementation: "Newznab"` (Prowlarr calls `.First()` on both and fails if either is missing), each with `configContract` and the full `fields[]` list above. |
| `POST /api/v3/indexer/test` | Validates the posted indexer (tries a `t=caps` call to `baseUrl`). The response MUST carry the header `X-Application-Version` ≥ `3.0.5.0`. In practice it is added to every response. |
| `GET /api/v3/indexer`, `GET /api/v3/indexer/{id}` | List/get. Prowlarr identifies its own indexers by `baseUrl` prefix = its URL, or `apiKey` = its key. `apiKey` MUST be returned (not masked) or as `********`. Prowlarr treats `********` as equal. |
| `POST /api/v3/indexer[?forceSave=true]` | Create. If validation fails (e.g. caps unreachable), return 400. Prowlarr retries with `forceSave=true`, which MUST bypass validation. |
| `PUT /api/v3/indexer/{id}[?forceSave=true]` | Update; same semantics. Unknown fields Prowlarr doesn't manage MUST be preserved. |
| `DELETE /api/v3/indexer/{id}` | Delete. |
| Auth | `X-Api-Key` header (and `?apikey=` query, or `Authorization: Bearer`). |

Newznab-implementation indexers that Prowlarr pushes (Usenet) are stored as sent but never searched
(`supportsRss`/`supportsSearch: false`, provider `message` "Usenet is not supported yet"), until
Usenet support lands. Their `enable*` flags are kept as sent, because Prowlarr compares them and
would otherwise push an update on every sync.

Implementation notes (M2a):
- The schema also carries Sonarr's other Torznab fields (`multiLanguages`, `failDownloads`,
  `seedCriteria.seasonPackSeedGoal`/`seasonPackSeedRatio`,
  `rejectTorrentFilesWithBlockedExtensionsWhileGrabbing`). Settings are stored by field name, and
  fields nobody knows about are kept and returned, so a Prowlarr update never loses user values.
  A posted `null` clears a field; `********` keeps the stored secret.
- Validation (skipped by `forceSave`): name required and unique, `priority` 1–50, `baseUrl`
  http(s), `apiPath` a path, `additionalParameters` like `&a=b`, seed criteria > 0, known tags, and,
  for enabled indexers, the `t=caps` test (must support `q` in `search` or `tv-search`).
- Requests use the `Indexer` HTTP client: same SSRF guard as sources (only link-local blocked, so
  a LAN Prowlarr works), 100 s timeout, and at most one request per 2 s per indexer host.

A **native Prowlarr app type** (like the one Sportarr has open as Prowlarr PR #2758) is a `Later`
upstream contribution. The Sonarr shim stays.

## Per-series search settings

Sonarr builds searches from TVDB metadata and scene naming. Generral builds them from **templates
defined by the mapping** (see [05](05-mapping.md#shape)), with optional series-level defaults:

```yaml
search:
  categories: [5000, 5060]          # optional; else indexer categories
  queries:                          # tried in order until results are found (or all, see below)
    - 'UFC {{ vars.number }}'
    - 'UFC{{ vars.number }}'
  queryMode: first-with-results     # | all  (union, de-duplicated by guid/infohash)
  must:     [ '\bUFC[ ._-]?{{ vars.number }}\b' ]   # all must match (regex, case-insensitive)
  mustNot:  [ 'fight[ ._-]?night' ]                  # none may match
  any:      []                                        # at least one must match (if non-empty)
  dateWindow: { before: 2d, after: 60d }  # release publishDate relative to airDateUtc
  parts: { <partKey>: { must: [...], mustNot: [...] } }   # merged with event-level rules
  pack:                                    # optional: releases covering several episodes
    must: [ '\bUFC[ ._-]?{{ vars.number }}\b' ]
    mustNot: [ 'prelim', 'main[ ._-]?card' ]
    covers: all-parts                      # | season
```

- Regexes are evaluated against a **normalised title**: `.`, `_`, `+` → space, collapsed spaces,
  diacritics removed. `\b` works as expected after normalisation. The raw title is also available
  (`raw:` prefix).
- Torznab requests use `t=search&q=<query>&cat=<cats>`. `t=tvsearch` is **not** used by default
  (season/ep params make no sense for virtual series). A per-indexer *Use TV search* toggle exists
  for trackers that only index sport under tvsearch.
- The rendered queries and rules for an episode are shown in *Episode details → Search*.

## Search triggers

| Trigger | Behaviour |
|---------|-----------|
| **RSS Sync** (every 15 min) | Fetch each RSS-enabled indexer's recent releases (Torznab `t=search` with no `q`, the cats from config). Evaluate each release against **all wanted episodes** (monitored, no file or upgradable, aired or airing within `dateWindow`). |
| **Automatic search (on air)** | For monitored episodes, schedule searches at `airDateUtc` + `[1h, 3h, 6h, 12h, 24h, 48h]`, then daily for 7 days, then stop (configurable per series: *Search schedule*). Sport releases appear a few hours to a day after the event, and RSS can miss them on busy trackers. |
| **Missing search** | *Wanted → Missing → Search All*, per season, per series. Throttled (one indexer query per 2 s per indexer, Sonarr parity). |
| **Cutoff unmet search** | Same, for upgradable episodes. |
| **Episode search** (automatic) | 🔍 button on an episode: searches and grabs the best accepted release. |
| **Interactive search** | 👤 button: shows all results with accept/reject reasons; the user can grab any, including rejected ones ("override"). The user can also **assign** a release to a different episode or to several episodes. |
| **Release push** (`POST /api/v3/release/push`) | For autobrr and similar tools: a single release is evaluated as if it came from RSS. |

## Decision engine

For each `(release, candidate episodes)` the engine runs **specifications**, like Sonarr's. Every
rejection carries a human-readable reason shown in interactive search and logs.

### Matching (Generral-specific)

1. **Episode match:** for each wanted episode in scope, evaluate `must`/`mustNot`/`any` (event-level
   merged with part-level). A release that matches exactly **one** episode is a single-episode
   candidate.
2. **Pack match:** if the release matches an event's `pack` rules, it is a candidate for **all
   wanted episodes it covers** (e.g. all parts of UFC 322). At import, files are split per part if
   the torrent contains several files, or the single file is imported as a **multi-episode file**
   (`S2026E11-E13`), which Plex supports.
3. **Ambiguity:** a release that matches several single episodes and no pack is **rejected**
   ("Ambiguous: matches S2026E11, S2026E12") unless the user assigns it in interactive search. Seeing
   this in the preview is a signal that the mapping's rules are too loose.
4. **Date window:** reject if `publishDate` is outside `[airDateUtc - before, airDateUtc + after]`.
   Without this, `UFC 300` would match a 2024 replay posted in 2027.
5. **Not yet aired:** reject releases for episodes whose `airDateUtc` is more than `before` in the
   future (protects against fake "early" uploads).

### Standard specifications (Sonarr parity)

| Specification | Notes |
|---------------|-------|
| Quality allowed by profile | Parsed by the Sonarr-derived `QualityParser` (resolution, source: HDTV/WEB-DL/WEBRip/Bluray, proper/repack). |
| Upgrade allowed / cutoff not met / is an upgrade | Against existing file. |
| Size within quality definition limits | Uses the episode `runtime` (from mapping; default 120 min for sport events) × MB/min limits. |
| Minimum seeders | Indexer field. |
| Release profiles | *Must contain* / *Must not contain* / (Sonarr v3-style) *Preferred words* with scores, scoped by tags. Useful for broadcaster or commentary-language preferences (`Sky Sports`, `ESPN`, `French`, `English`). |
| Custom formats | `Later`. Port Sonarr v4 custom formats (also covers e.g. `50fps` and broadcaster preferences). |
| Blocklisted | By title+indexer or by infohash. |
| Already in queue / already imported | |
| Retention / max age | Optional. |
| Delay profile | `Later`. |
| Indexer/download client tags | Sonarr tag semantics. |

### Prioritisation

Among accepted candidates for the same episode set: quality (profile order) → release-profile
score → *(Later: custom format score)* → proper/repack → indexer priority → seeders → size (closer to
preferred) → age (newer). This is the same ordering as Sonarr's `DownloadDecisionComparer`, with
seeders before size.

## Quality

Port Sonarr's quality list unchanged so that profiles look identical:
`Unknown, SDTV, DVD, WEBRip-480p, WEBDL-480p, Bluray-480p, HDTV-720p, WEBRip-720p, WEBDL-720p,
Bluray-720p, HDTV-1080p, WEBRip-1080p, WEBDL-1080p, Bluray-1080p, Bluray-1080p Remux, HDTV-2160p,
WEBRip-2160p, WEBDL-2160p, Bluray-2160p, Bluray-2160p Remux, Raw-HD`.

Default quality profiles: *Any*, *SD*, *HD-720p*, *HD-1080p*, *Ultra-HD*, *HD - 720p/1080p* (Sonarr
defaults). Default quality definitions (size limits) are tuned for sport: an event-length (3–5 h)
1080p WEB release is commonly 5–12 GB, so the default max for WEBDL-1080p is raised compared to
Sonarr's TV defaults.

Sport release titles often lack a source tag (`UFC 314 1080p h264-GRP`). The parser treats
`1080p` with no source as `WEBDL-1080p` when the codec is h264/h265/x264/x265 and there is no HDTV
marker, and as `HDTV-1080p` when it sees a broadcaster/TV marker (`HDTV`, `PDTV`, `DSR`, `SKY`,
`ESPN+`…).
