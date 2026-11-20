# Generral

> A generic, Sonarr-style PVR for things that aren't TV shows.

Generral watches **arbitrary sources** (JSON APIs, web pages, iCal calendars, RSS feeds, …) for
**events**, lets you **map** those events into a *virtual* TV series (series → seasons → episodes),
then does what Sonarr does: searches your indexers (synced from **Prowlarr**), sends the best release
to **qBittorrent**, and imports / renames the result into a **Plex**-friendly library.

The original use-case is sport:

| Real-world thing                | Becomes in Plex                                                        |
|---------------------------------|------------------------------------------------------------------------|
| UFC numbered events             | `UFC/Season 2025/UFC - S2025E04 - UFC 314 - Main Card.mkv`              |
| Rugby World Cup                 | `Rugby World Cup/Season 2023/Rugby World Cup - S2023E48 - Final - South Africa vs New Zealand.mkv` |
| World Rugby Sevens Series       | `HSBC SVNS/Season 2025/HSBC SVNS - S2025E01 - Dubai - Cup Final.mkv`  |

…but nothing in Generral is sport-specific. Anything that can be scraped into a list of dated,
identifiable events can be turned into a series.

## Status

**Early development (M0 – skeleton).** The specs are the source of truth; start reading at
[`docs/specs/README.md`](docs/specs/README.md). Progress is tracked against the
[roadmap](docs/specs/13-roadmap.md).

## Development

Requirements: the [.NET SDK](https://dotnet.microsoft.com/download) pinned in `global.json`
(10.0.x).

```sh
dotnet test Generral.slnx                      # unit + integration tests
dotnet run --project src/Generral.Host         # http://localhost:8990
```

`dotnet run` uses `src/Generral.Host/Properties/launchSettings.json`, which keeps runtime data in
`./config/` (git-ignored). On first start Generral writes `config/config.yaml` with a new API key and
logs the password of the initial `generral` user. Health check: `GET /ping`.

Before pushing, run `dotnet format Generral.slnx` and, if available, `reuse lint`
([REUSE](https://reuse.software)); CI checks both. After changing entities, add a migration with
`dotnet tool restore && dotnet ef migrations add <Name> --project src/Generral.Core --output-dir Datastore/Migrations`.

## Specs at a glance

| #  | Document                                                             | What it covers                                   |
|----|----------------------------------------------------------------------|--------------------------------------------------|
| 00 | [Overview](docs/specs/00-overview.md)                                | Vision, goals, non-goals, glossary, use-cases    |
| 01 | [Prior art & reuse](docs/specs/01-prior-art.md)                      | What exists, what we reuse, what we don't        |
| 02 | [Architecture](docs/specs/02-architecture.md)                        | Components, tech stack, data flow                |
| 03 | [Domain model](docs/specs/03-domain-model.md)                        | Entities and their relationships                 |
| 04 | [Sources](docs/specs/04-sources.md)                                  | Pluggable event sources (API, HTML, iCal, …)     |
| 05 | [Mapping](docs/specs/05-mapping.md)                                  | Events → Series / Season / Episode               |
| 06 | [Indexers & release matching](docs/specs/06-indexers-and-search.md)  | Prowlarr, Torznab, decision engine, quality      |
| 07 | [Download clients](docs/specs/07-download-clients.md)                | qBittorrent (first), client abstraction          |
| 08 | [Import & organisation](docs/specs/08-import-and-organisation.md)    | Completed download handling, naming, NFO, art    |
| 09 | [Media servers & notifications](docs/specs/09-media-servers-and-notifications.md) | Plex, Jellyfin/Emby later, Connect |
| 10 | [User interface](docs/specs/10-ui.md)                                | Sonarr-consistent UI, page by page               |
| 11 | [HTTP API](docs/specs/11-api.md)                                     | Native API + Sonarr-compat shim for Prowlarr     |
| 12 | [Deployment](docs/specs/12-deployment.md)                            | Docker image, volumes, compose, CI               |
| 13 | [Roadmap](docs/specs/13-roadmap.md)                                  | Milestones, MVP scope, open questions            |

Worked mapping examples live in [`docs/specs/examples/`](docs/specs/examples/).

## License

Generral is free software, licensed under the **GNU General Public License v3.0 or later**
([`LICENSE`](LICENSE)). It reuses code from Sonarr (GPL-3.0-or-later) with attribution; see
[`COPYRIGHT.md`](COPYRIGHT.md) and [01 – Prior art § Licensing](docs/specs/01-prior-art.md#licensing).
