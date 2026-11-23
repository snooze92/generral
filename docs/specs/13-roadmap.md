# 13 – Roadmap

## Milestones

### M0 – Skeleton
- Repo layout, .NET host serving a Vite SPA, SQLite + EF migrations, auth (Forms + API key),
  config.yaml/env, logging, `/ping`, Dockerfile + multi-arch CI publishing `develop`.
- Vendored Sonarr UI shell (sidebar, header, themes, tables, forms, modals).

### M1 – Catalogue (no downloading)
- Sources: `thesportsdb`, `http-json`, `http-html`, `ical`, `static`, with preview.
- Mapping engine: filter, vars, parts, season/episode strategies, numbering stability
  (`append`, `renumber-unlocked`), preview, versions.
- Series/Season/Episode UI (index, details, monitoring), Calendar + iCal feed.
- Presets: format, gallery, import/export YAML. None ship (tests use fictional presets).
- **Exit criteria:** UFC and Rugby World Cup series look right in the UI with correct numbering, and
  survive source refreshes.

### M2 – Grab
- Torznab indexers, Sonarr v3 indexer shim, **Prowlarr sync verified** against current Prowlarr.
- Quality parser/definitions/profiles, release profiles.
- Decision engine with Generral matching (must/mustNot/any, parts, packs, date window), interactive
  search with reasons, automatic search, on-air search schedule, RSS sync.
- qBittorrent client, queue, history, blocklist, failed download handling.
- **Exit criteria:** UC1 grabs automatically from a Prowlarr-synced indexer into qBittorrent.

### M3 – Import & Plex (**MVP / v0.1**, released as v0.1.0)
- Completed download handling, remote path mappings, hardlink/copy, rename, multi-episode files,
  manual import, recycle bin.
- Plex NFO metadata + local artwork, Plex Connect (partial refresh), Webhook, Discord.
- Health checks, backups, System pages.
- **Exit criteria:** the MVP success criteria in [00](00-overview.md#success-criteria-for-mvp).

### M4 – Polish (v0.2–v0.x)
- Community/fictional preset examples (fictional or licence-free content only; nothing ships by
  default).
- Renumber with on-disk rename, *Preview Rename* / organise existing files, moving files when a
  series path changes, Library Import (adopt existing files).
- Mapping editor for existing series (versions, revert, *Renumber…* preview in the UI; the API
  exists), a richer mapping preview (part, path and search columns, change counts).
- Release packs (`search.pack`), release profile *Preferred words* scores, "already imported"
  check, Plex health checks (library covering the root folder, agent hint).
- Manual import: release group and language editing, file browser; queue: unknown items, *Grab
  again*, indexer column.
- Header search and shortcuts, SignalR live updates, global source fetch settings, TheSportsDB
  league search, `{{ secret.NAME }}` in source headers, secrets encrypted at rest, per-entity YAML
  export, event revision view, manual episodes, `date.tz` and `sport.*` template helpers, per-series
  spoiler fields.
- `/api/v3/release/push` (autobrr), read-only Sonarr series/calendar/queue shim for dashboards.
- More notification providers, Jellyfin/Emby refresh.

### Later
- Plex Custom Metadata Provider (`/plex`), with Generral as the agent.
- Custom formats (Sonarr v4 port), delay profiles.
- Usenet (Newznab + SABnzbd/NZBGet).
- More download clients (Transmission, Deluge, rTorrent, Blackhole).
- `rss`, `script`, and community preset-feed sources. Visual CSS selector picker.
- Native Prowlarr app type (upstream PR).
- PostgreSQL.

## Open questions

| # | Question | Current leaning |
|---|----------|-----------------|
| Q1 | Year seasons (`Season 2025`) vs. per-event seasons for small series (e.g. one season per Rugby World Cup edition, already the same thing) vs. one season per tournament leg for Sevens? | Year by default; presets choose. Sevens preset: season = series year, episodes = legs × stages. |
| Q2 | Should prelims/main card default to separate episodes (parts) or a single episode with the main card only? | Both work: a mapping with or without `parts` (no presets ship; users choose). |
| Q3 | Is TheSportsDB free tier enough for the MVP use-cases? | **Probably not.** The free key caps a season schedule at 15 events. UFC runs ≈ 40 events/yr (all types), and filtering to numbered events happens on our side, after the cap. The Rugby World Cup has 48 matches. Plan: `eventsnextleague`/`eventspastleague` top-ups to stay current, a Wikipedia `http-html` source as the main or fallback source in presets, and premium documented as the easy path. Needs a spike in M1. |
| Q4 | Kodi-NFO `<season>` with year values (`2025`): verify the Plex NFO agent accepts 4-digit seasons. | **Closed:** it does. `Season 2025/… S2025E01 …` with `<season>2025</season>` loads with the *Plex NFO Series* agent (PMS ≥ 1.43.1); the file name decides the numbers and the NFO must carry the same ones, which Generral writes. No season offset needed. |
| Q5 | Expose a Sonarr-like `tvdbId` (synthetic) in the shim? | Only if a real consumer needs it. |
| Q6 | Allow a single series to be fed by *events from several sources merged by key* (e.g. TheSportsDB for dates + Wikipedia for titles)? | Not in MVP. Multiple mappings with `priority` + `dedupe` cover the common case. Revisit "event enrichment" later. |
| Q7 | Project name spelling | **Generral** (double r, as given). |
