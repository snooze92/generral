# Changelog

All notable changes to Generral are listed here. Versions follow [Semantic Versioning](https://semver.org);
before 1.0, minor versions may include breaking changes.

## [0.1.0] – 2026-11-23

First release: the MVP of the [roadmap](docs/specs/13-roadmap.md) (milestones M0–M3).

### Catalogue
- **Sources**: TheSportsDB (v1, with next/past top-ups past the free tier's 15-event cap),
  `http-json` (JSONPath extraction, pagination, details requests), `http-html` (CSS selectors),
  `ical` and `static`, with a test preview. Events keep their revision history and are marked gone
  after missed refreshes.
- **Mappings**: events → series/seasons/episodes with filters, variables, Scriban templates, parts
  (e.g. *Prelims* / *Main Card*), year/field/fixed/sequential seasons, sequential/field/date episodes,
  numbering that stays put (`append`, `renumber-unlocked`, locks once aired, grabbed or imported),
  `dedupe` across mappings, versions, and a live preview in *Add Series*.
- Series index, series details (seasons, episodes, monitoring, locks), *Edit* and *Edit Images*,
  Calendar and iCal feed, presets (import from YAML or URL; none ship).

### Grab
- Torznab indexers, synced from **Prowlarr** through a Sonarr v3 indexer API (add Generral in
  Prowlarr as a *Sonarr* app). Indexer back-off, tags and priorities.
- Sonarr's qualities, quality definitions and profiles, release profiles (required/ignored terms),
  languages.
- Decision engine with Generral's release matching (`must` / `mustNot` / `any` rules, parts, date
  window), automatic and interactive search with reasons, on-air search, RSS sync.
- **qBittorrent**, queue, history, blocklist, failed download handling.

### Import & Plex
- Completed download handling with remote path mappings, hardlink/copy, Sonarr-style renaming,
  multi-episode files, multi-file releases, subtitles, recycle bin, permissions (chmod/chown), empty
  folder cleanup.
- **Manual import** from the queue and *Wanted → Manual Import*: assign series, episodes and quality
  per file.
- **Plex NFO** metadata (`tvshow.nfo`, episode NFOs) and local artwork, enabled by default:
  downloaded posters/fanart, generated title posters when there are none, episode posters fitted
  to 16:9, spoiler-free mode.
- Connections: **Plex Media Server** (partial library refresh), **Webhook** (Sonarr payloads),
  **Discord**.

### System
- Forms or external authentication, API key, `urlBase`, `config.yaml` + `GENERRAL__*` environment
  variables.
- Health checks, scheduled tasks, backups and restore, logs, events, status.
- Docker image for `linux/amd64` and `linux/arm64` (linuxserver.io-style `PUID`/`PGID`/`UMASK`/`TZ`).

### Not yet
Planned for later versions (see the [roadmap](docs/specs/13-roadmap.md)): renaming existing files
and renumbering with on-disk renames, Library Import, release packs, release profile scores, custom
formats, `/release/push` (autobrr), Usenet, more download clients, Jellyfin/Emby, more notification
providers, `rss`/`script` sources, editing a series' mapping after creation from the UI (the API
supports it), real-time UI updates (SignalR), the header search box.

[0.1.0]: https://github.com/snooze92/generral/releases/tag/v0.1.0
