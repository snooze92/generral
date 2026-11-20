# 01 – Prior art & reuse

Research done 2026-11. This document records what already exists, what Generral reuses, and why
Generral is a separate project rather than a configuration of an existing tool.

## Landscape

| Project | What it is | Relevance |
|---------|-----------|-----------|
| **[Sonarr](https://github.com/Sonarr/Sonarr)** (C#/.NET, React; GPL-3.0) | TV PVR. Catalogue comes from TheTVDB through Sonarr's *SkyHook* metadata proxy. | **The UX and behaviour we copy.** Also the source of reusable parsing, download client and notification code. |
| **[Prowlarr](https://github.com/Prowlarr/Prowlarr)** (C#/.NET; GPL-3.0) | Indexer manager. Pushes indexers to "apps" (Sonarr, Radarr, …) over their APIs. | **Integration target.** Its Sonarr app driver (`SonarrV3Proxy`) defines exactly which Sonarr API endpoints we have to emulate. Its *Cardigann* YAML engine is a proven pattern for declarative HTML scraping. |
| **[Sportarr](https://github.com/Sportarr/Sportarr)** (C#/.NET 8+, React 19 + Vite + Tailwind; GPL-3.0) | "Sonarr for sports". Forked from Sonarr in Oct 2025, then mostly rewritten. Catalogue comes from its own hosted API (sportarr.net, built on TheSportsDB data). | **Closest existing project.** It covers a lot of the sport use-case. It also validates several design choices (below). Its fixed, hosted catalogue is what Generral generalises away from. |
| **[TheSportsDB](https://www.thesportsdb.com/documentation)** | Community sports database with a JSON API. v1 free key `123` (30 req/min, tight per-endpoint limits, e.g. a season schedule is capped at 15 events), v2 needs premium ($9/month). UFC is league `4443`. | A **first-class built-in source type**. Not a hard dependency. |
| **[TVSportsDB](https://tvsportsdb.com/setup)** | Sports metadata in a Show/Season/Episode shape with a Plex provider and a Jellyfin plugin; YAML/CSV import/export. | Confirms that people want sport modelled as TV. Its file-naming convention is compatible with ours. |
| **SportScanner / legacy Plex agents** | Python Plex scanner and agent for TheSportsDB. | Legacy Plex plugin agents are being retired, so we don't build on them. |
| **Plex NFO agent** (PMS ≥ 1.43.1) | Built-in *Plex NFO Series* agent that reads Kodi-format `tvshow.nfo` and per-episode `.nfo` files. | **The main Plex output path.** Generral writes NFO and local artwork, and Plex shows our titles, plots and dates without any custom agent. |
| **Plex Custom Metadata Providers** (PMS ≥ 1.43.0) | An HTTP API (`MediaProvider` → `match` / `metadata` features) that Plex queries for metadata. [Example provider](https://github.com/plexinc/tmdb-example-provider). | **Later.** Generral already *is* the catalogue, so it can expose itself as a provider (Sportarr does this). |
| **FlexGet**, qBittorrent RSS rules, autobrr | Rule-based RSS or IRC grabbers. | Good at grabbing, but none of them have a catalogue (missing/wanted, upgrades, import/rename). autobrr can *push* releases into Sonarr-compatible apps; see [11 – API](11-api.md#sonarr-compatibility-shim). |
| **FileBot** | Renamer that matches against TheTVDB/TMDB. | It matches files against online databases, which is the step we replace. |

## Why not just use Sportarr?

Sportarr is worth recommending to users who only want mainstream sport that its catalogue covers.
Generral exists because:

1. **The catalogue is fixed and hosted.** If an event, competition or numbering isn't in
   sportarr.net/TheSportsDB, or isn't modelled the way you want (e.g. "numbered UFC events only, one
   season, episode = event number"), you can't change it.
2. **It is sport-only.** Generral's core abstraction (source → events → mapping → series) works for
   any content.
3. **The numbering is decided for you.** Sportarr uses year seasons and chronological episodes. Generral
   makes season/episode strategy part of user configuration.

## Why not fork Sonarr?

We considered replacing Sonarr's metadata provider (SkyHook/TVDB) with a local catalogue built from
sources and mappings, and keeping the rest of Sonarr. We rejected it:

- TVDB ids run through Sonarr's whole model (`Series.TvdbId` is the identity, used in search
  criteria, import lists, Plex/Kodi metadata and parsing). Removing that assumption touches hundreds of
  files.
- Sonarr's release parser assumes scene-style `SxxEyy`, daily or absolute (anime) naming. Sport
  releases (`UFC.314.Prelims.1080p.WEB.h264-GRP`, `Rugby.World.Cup.2023.Final.RSA.v.NZL…`) fail at
  parse time, before any decision is made. We need *per-series, user-configured* matching instead
  (see [06](06-indexers-and-search.md)).
- Keeping up with upstream Sonarr in a deep fork is expensive. Sportarr forked Sonarr and then
  "rewritten shortly afterwards"; we take that as a strong signal.

## What we reuse

Generral uses **the same stack as Sonarr/Prowlarr/Sportarr** (C#/.NET backend, React/TypeScript
frontend) mainly so that GPL-3.0 code from those projects can be **ported file-by-file, with
attribution**, instead of re-implemented.

| Area | Reuse from | How |
|------|-----------|-----|
| Quality parsing (resolution, source, codec, proper/repack, release group) | Sonarr `NzbDrone.Core/Parser/QualityParser.cs`, `ReleaseGroupParser` | Port, trimming TV-specific branches. |
| Quality model, quality definitions (size limits), quality profiles, (later) custom formats | Sonarr | Port the model and UI so settings look identical. |
| Torznab client and capabilities parsing | Sonarr/Prowlarr `Newznab`/`Torznab` indexer code | Port. |
| Sonarr indexer API contract for Prowlarr sync | Prowlarr `Applications/Sonarr/*` (consumer side) | Emulate the endpoints/fields it calls; see [11](11-api.md). |
| qBittorrent WebUI API v2 client | Sonarr `Download/Clients/QBittorrent/QBittorrentProxyV2.cs` | Port. |
| Completed download handling & import flow, remote path mappings, hardlink-or-copy, recycle bin | Sonarr | Port the concepts. The episode-matching step is replaced. |
| Plex Media Server "Connect" (library section refresh) | Sonarr `Notifications/Plex/Server/*` | Port. |
| Kodi/Plex NFO writer | Sonarr `Extras/Metadata/Consumers/Xbmc/*` | Port and adapt to the Plex NFO agent tag subset. |
| Notification providers (Discord, webhook, ntfy, Apprise, …) | Sonarr `Notifications/*` | Port, one by one, as needed. |
| HTML scraping engine (selectors, filters such as `re_replace`, `dateparse`, `split`) | Prowlarr **Cardigann** engine | Port the selector/filter pipeline for the `http-html` source type (see [04](04-sources.md)). Users who have written Prowlarr definitions already know the syntax. |
| UI shell, components, themes, page layouts | Sonarr `frontend/src` (React + TypeScript + CSS modules) | Vendor the generic components (`Components/Page`, `Table`, `Form`, `Modal`, `Menu`, `Icon`, …) and theme variables so that Generral looks and behaves like Sonarr. See [10](10-ui.md). |
| Design validation | Sportarr | Year-numbered seasons (`Season 2025`, `S2025E04`) work in Plex; "multi-part" events (prelims/main card); a Sonarr v3 shim for Prowlarr; spoiler-free summaries. |

Third-party libraries (non-exhaustive, all permissive or GPL-compatible):

| Purpose | Library |
|---------|---------|
| Templating / expressions in mappings | [Scriban](https://github.com/scriban/scriban) (sandboxed, Liquid-like) |
| JSONPath (RFC 9535) | [JsonPath.Net](https://github.com/json-everything/json-everything) |
| HTML parsing / CSS selectors | [AngleSharp](https://github.com/AngleSharp/AngleSharp) |
| iCalendar | [Ical.Net](https://github.com/ical-org/ical.net) |
| RSS/Atom | `System.ServiceModel.Syndication` |
| Persistence | EF Core + SQLite (PostgreSQL optional, like Sonarr) |
| Scheduling | Built-in hosted services + a Sonarr-style command queue |
| Real-time UI updates | ASP.NET Core SignalR (Sonarr uses SignalR too) |

## Licensing

Sonarr and Sportarr are licensed **GPL-3.0-or-later**; Prowlarr is GPL-3.0 (its notices don't say "or
later"). Porting their code makes Generral a derivative work, so Generral **MUST** be released as
**GPL-3.0-or-later**, and:

- Generral follows the [REUSE](https://reuse.software) specification. `reuse lint` runs in CI, so every
  file has machine-readable copyright and licence information.
- Every **ported** source file keeps its upstream copyright as an `SPDX-FileCopyrightText` line and gets
  a `Ported from <project>@<commit> <path>` note. Sonarr's own files carry no per-file headers, so
  there is no original header text to preserve; the SPDX line is the retained notice.
- Upstream code **vendored as a whole directory** (e.g. Sonarr's generic UI components, see
  [10](10-ui.md#implementation-approach)) is attributed per directory in `REUSE.toml` instead of
  per file, together with the upstream commit it was copied from, so that the files stay
  byte-identical to upstream (or minimally patched) and are easy to re-sync.
- `COPYRIGHT.md` lists the upstream projects, their copyright holders and the revisions used.
- **Prowlarr caveat:** because Prowlarr doesn't grant "or later", code taken from it could restrict the
  combined work to GPL-3.0-only. Before porting Prowlarr files (notably Cardigann for `http-html`),
  check their provenance; prefer Sonarr's equivalent, or reimplement the documented YAML format.
- Trademarks/logos of Sonarr/Plex/etc. are not used as Generral branding. Generral has its own
  name, logo and accent colour (see [10](10-ui.md#branding)).
