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
  a LAN Prowlarr works), 100 s timeout, and at most one request per 2 s per indexer (keyed by URL
  path, not host: all Prowlarr proxies share one host).
- Searches and RSS syncs query up to 8 indexers in parallel; each indexer gets 30 s (15 s for an
  interactive search) for all its queries of that search. A failing or timed-out indexer is logged
  and only loses its own results; interactive search returns what finished and names the timed-out
  indexers in the `X-Generral-Timed-Out-Indexers` response header (the UI shows a warning).
- **Indexer back-off** (Sonarr's `IndexerStatus`, kept in memory: a restart clears it): each failed
  request escalates the indexer's level and disables it for 1 min, 5 min, 15 min, 30 min, 1 h, 3 h,
  6 h, 12 h or 24 h; a success steps the level down and re-enables it; a successful *Test* or deleting
  the indexer resets it. Connection errors don't escalate; in the first 15 min after a start the
  back-off doesn't escalate and is capped at 5 min. A request limit (HTTP 429, `Request limit
  reached`, or Prowlarr's `Indexer is disabled till {time} due to recent failures.`, read as local
  time) disables it at least until `Retry-After`/that time, else for 1 h. Disabled indexers are
  skipped by RSS sync, automatic and interactive search (also for their remaining queries), and
  listed by the *Indexer Status* health check (with their re-enable time) while their first failure
  is less than 6 h old. A per-indexer search timeout is not a failure.

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
  searchSchedule: [1h, 3h, 6h, 12h, 24h, 48h, 3d, 4d, 5d, 6d, 7d, 8d, 9d]  # on-air searches (default); [] = off
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
| Language | Profile `language` (see § Languages). |
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

### Implementation notes (M2c)

- Search settings of an episode are its `searchTerms` with the series' `searchSettings` as
  per-key defaults. Without `dateWindow`, the window is `{ before: 1d, after: 365d }`. An episode
  with no `must` and no `any` rule never matches (otherwise every title would). Invalid regexes
  reject with "Invalid search rule"; regexes time out after 100 ms.
- Scope: a search matches releases against **all active episodes of the searched series**, so a
  prelims release found while searching the main card is recognised (rejected "Matches S2026E12,
  which wasn't requested") rather than mis-assigned; releases matching nothing get "Does not match
  S…: <first failing rule>" when one episode was searched. If a title matches several episodes
  but only one is inside its date window, that one wins.
- Queries: per indexer, each query in order (`first-with-results` stops at the first non-empty
  answer; `all` runs every one); identical `(indexer, query, categories)` run once per search.
  Results are de-duplicated by infohash, else indexer+guid. Only Torznab indexers are queried.
- Implemented specifications: protocol (Usenet → "Usenet is not supported"), quality allowed by
  profile, existing file (no upgrades until episode files exist), size (MB/min × runtime, default
  120 min), minimum seeders (indexer setting), release profile required/ignored terms (Sonarr
  term matcher: `/regex/i` or substring; tags and indexer scope), monitored (automatic/RSS only).
  M2d added blocklisted (Sonarr's comparer: infohash, else title + indexer, per series) and already in queue
  (any tracked, non-failed download for one of the episodes rejects: "Release in queue already: …").
  Not yet: already imported, retention, packs, release-profile scores.
- Commands: `EpisodeSearch { episodeIds }` (any state), `SeasonSearch { seriesId, seasonNumber }`,
  `SeriesSearch { seriesId }` and `MissingEpisodeSearch` (monitored, aired, no file; Missing also
  needs a monitored series). Each grabs the best accepted release per episode set and never two
  for the same episode. `LastSearchTime` is set on searched episodes. Unmonitored episodes may be
  grabbed only when the command is manual.
- RSS sync: indexers with *Enable RSS*; candidates are active episodes without a file whose date
  window contains now. Interval: `PUT /settings/indexer { rssSyncInterval }` (minutes, 0 off, else
  10–120, default 15); the last run is kept in memory, so it runs at the first scheduler check after
  a start.
- Grabs go through `IDownloadService`, which sends them to a download client (spec 07 § Implementation notes (M2d)).
  A failed grab is logged and the next best release for the same episodes is tried.
- Interactive results are ordered accepted → rejected matches → unmatched, each best first. The UI
  table sorts client-side by any column (Sonarr's predicates: age, title, indexer, size, peers,
  languages, quality weight, rejections); its default order is approved first → quality (in the
  series' profile order, best first) → seeders descending, i.e. by `releaseWeight` with the server
  order as tie-breaker.
  Any matched release can be grabbed (`POST /release`), rejected ones included (date window, quality…);
  unmatched ones need `episodeIds`.
- On-air search: the scheduler (every minute) queues `OnAirSearch` when a monitored episode of a
  monitored series, without a file or queued download, has a passed slot it wasn't searched since
  (`lastSearchTime` < slot). Slots: `airDateUtc` + 1h, 3h, 6h, 12h, 24h, 48h, then daily from day 3 to
  day 9. A slot missed by more than 1 day (downtime) is skipped; offsets beyond 60 d are ignored.
  Per series/mapping: `searchSchedule: [1h, 6h, 2d]` in the search settings (`[]` disables).
  It is an automatic search (monitored only, grabs the best accepted release).
- `CutoffUnmetEpisodeSearch` searches monitored, aired episodes of monitored series whose file is below the cutoff.
  Upgrades (§ Specifications) follow Sonarr's `UpgradableSpecification` without custom formats: propers/repacks are always
  preferred.

## Languages

Sonarr's `LanguageParser` and language list (ids unchanged) are ported. Release languages are
parsed from the title, ignoring the text matched by the episode's `must`/`any` rules (so `FRA` in
`NZL v FRA` isn't French). Like Sonarr, an untagged title is `Unknown` and a `MULTi` title adds
`Original`. Series have no original language: **Unknown and Original count as English**.

The quality profile has one wanted `language` (Radarr-style; default English, seeded English for
existing profiles), or the special values `Any` (-1, no check) and `Original` (-2, = English). The
language specification rejects releases whose languages don't include it: "Language French is not
wanted (profile: English)". The indexer `multiLanguages` setting is accepted but not used yet.

## Quality

Port Sonarr's quality list unchanged so that profiles look identical:
`Unknown, SDTV, DVD, WEBRip-480p, WEBDL-480p, Bluray-480p, HDTV-720p, WEBRip-720p, WEBDL-720p,
Bluray-720p, HDTV-1080p, WEBRip-1080p, WEBDL-1080p, Bluray-1080p, Bluray-1080p Remux, HDTV-2160p,
WEBRip-2160p, WEBDL-2160p, Bluray-2160p, Bluray-2160p Remux, Raw-HD`.

Default quality profiles: *Any*, *SD*, *HD-720p*, *HD-1080p*, *Ultra-HD*, *HD - 720p/1080p* (Sonarr
defaults). Default quality definitions (size limits) are tuned for sport: an event-length (3–5 h)
1080p WEB release is commonly 5–12 GB, so the default max for WEBDL-1080p is raised compared to
Sonarr's TV defaults: max MB/min 200 for 720p (HDTV/WEB/Bluray), 250 for HDTV-1080p and WEB 1080p,
300 for Bluray-1080p (Sonarr: 125–155); everything else keeps Sonarr's values.

Sport release titles often lack a source tag (`UFC 314 1080p h264-GRP`). The parser treats
`1080p` with no source as `WEBDL-1080p` when the codec is h264/h265/x264/x265 and there is no HDTV
marker, and as `HDTV-1080p` when it sees a broadcaster/TV marker (`HDTV`, `PDTV`, `DSR`, `SKY`,
`ESPN+`…). Implemented markers: `SKY`, `ESPN`/`ESPN+`, `BT Sport(s)`, `TNT Sports`, `Fox Sports`,
`Eurosport`, `BBC`, `ITV`, `Canal+`, `beIN` (whole words: `SkyF1` stays WEB). Sonarr's own
source tags (`HDTV`, `PDTV`, `DSR`, `WEB`…) are handled before the heuristic. `1080p` with neither a
codec nor a source stays HDTV-1080p (Sonarr).
