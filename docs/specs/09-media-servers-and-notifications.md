# 09 – Media servers & notifications ("Connect")

*Settings → Connect*, identical in concept and UI to Sonarr: a list of connections, each with
triggers, tags, and provider-specific fields.

## Triggers (Sonarr parity)

`On Grab`, `On Import` (`On Download`), `On Upgrade`, `On Rename`, `On Series Add`,
`On Series Delete`, `On Episode File Delete`, `On Episode File Delete For Upgrade`,
`On Health Issue` (+ *Include Health Warnings*), `On Health Restored`, `On Manual Interaction
Required`, `On Application Update` (not used in Docker).

Generral additions: `On New Episode` (a mapping created a monitored episode, e.g. "UFC 323
announced"), `On Source Failure`.

## Providers

| Provider | MVP | Notes |
|----------|-----|-------|
| **Plex Media Server** | MVP | Port of Sonarr's. Plex.tv OAuth sign-in to obtain a token (or paste a token); host/port/SSL. *Update Library* on import/rename/delete: **partial scan** of the series folder via `GET /library/sections/{id}/refresh?path=<url-encoded folder>`. The section id is discovered by matching section `Location` paths against the series path (with a path mapping setting for when Plex sees different paths). |
| Webhook | MVP | Sonarr-shaped JSON payloads (`eventType`, `series`, `episodes`, `episodeFile`, `release`, …) so that existing Sonarr webhook consumers mostly work. |
| Discord | MVP | |
| ntfy, Apprise, Gotify, Pushover, Telegram, Email | Later | Port from Sonarr as needed. |
| Jellyfin / Emby | Later | Library refresh. Jellyfin reads the same NFO files (Kodi format), so approach A in [08](08-import-and-organisation.md) works there too. |
| Kodi | Later | |
| Custom script | Later | Environment variables `generral_*`, mirroring Sonarr's `sonarr_*`. |

## Plex specifics

- **Health check:** for each series root folder, check that a Plex TV library section contains it.
  If not, warn: "Plex has no TV library covering `/data/media/sports`".
- **Agent hint:** if the section's agent is the default *Plex TV Series* (`tv.plex.agents.series`),
  show a one-time informational health message suggesting *Plex NFO Series* or Personal Media Shows,
  since the default agent will try to match "UFC" against TMDB/TVDB and may mis-match.
- **Later – Custom Metadata Provider:** serve `GET /plex` → `MediaProvider` manifest with identifier
  `tv.plex.agents.custom.generral` and types 2/3/4, and `match` and `metadata` features (including
  `/children` for seasons/episodes), following
  [plexinc/tmdb-example-provider](https://github.com/plexinc/tmdb-example-provider) docs. The ratingKey
  scheme is `generral-series-<id>`, `generral-season-<seriesId>-<n>`, `generral-ep-<id>`. When a
  filename contains a `generral-ep-<id>` token, matching uses that token instead of season/episode
  numbers, so renumbering can't confuse it.
