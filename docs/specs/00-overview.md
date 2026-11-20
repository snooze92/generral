# 00 – Overview

## Problem

The *arr ecosystem (Sonarr, Radarr, Lidarr, …) automates downloads very well, but only for content
that has an entry in a metadata database: TheTVDB for Sonarr, TMDB for Radarr, MusicBrainz for Lidarr.
A lot of content you'd want to automate isn't in those databases, or isn't organised there in a useful
way:

- **Sport**: UFC cards, rugby tournaments, F1 sessions, cycling stages. TheTVDB coverage is
  patchy and the numbering often doesn't match how releases are named.
- **Recurring shows that aren't really "TV"**: conference talks, e-sports events, a podcast's video
  feed, festival sets, a broadcaster's catch-up catalogue.
- **Your own idea of a series**: "every Champions League final", "every Six Nations match France plays".

People get by today with hand-written FlexGet configs, qBittorrent RSS rules, manual renaming, or
Sportarr (sport only, fixed catalogue). None of these give you **Sonarr's workflow** (monitored
episodes, a missing list, upgrades, a queue, history, import, rename) over **a catalogue you define
yourself**.

## Vision

> **Generral = Sonarr's workflow + a user-defined catalogue.**

The user tells Generral:

1. **Where events come from**: a *Source*, e.g. TheSportsDB's UFC league, a Wikipedia table, an
   ICS calendar, a JSON endpoint, an HTML page.
2. **How events become episodes**: a *Mapping*, e.g. "season = event year, episode = order
   within the year, title = event name; only keep events whose name matches `^UFC \d+`".
3. **How to find releases for an episode**: search query templates and match rules, usually
   provided by the mapping preset.

From then on Generral behaves like Sonarr: it builds a virtual series with seasons and episodes, marks
future and past episodes as monitored or not, searches indexers, grabs, tracks the download, imports,
renames, writes Plex metadata, and refreshes Plex.

## Goals

| ID  | Goal |
|-----|------|
| G1  | **Generic sources.** Any HTTP JSON/XML API, HTML page, iCal feed or RSS feed can be a source, configured without writing code. |
| G2  | **Configurable mapping.** Users control how events turn into series/season/episode numbers, titles, dates and metadata. A live preview shows the result before anything is saved. |
| G3  | **Stable numbering.** Once an episode number has been given out (and especially once a file is imported), it doesn't change by itself. |
| G4  | **Prowlarr integration.** Indexers are synced from Prowlarr with no manual Torznab setup, by adding Generral in Prowlarr as a Sonarr-type app. |
| G5  | **qBittorrent integration** first, behind a download-client abstraction so others can be added. |
| G6  | **Plex-correct output.** Folder and file naming, NFO files and local artwork that a Plex *TV Shows* library displays correctly with no extra agent. |
| G7  | **Familiar UI.** Layout, terminology, settings and behaviour consistent with Sonarr. |
| G8  | **Docker-first.** One container, linuxserver.io-style conventions (`PUID`/`PGID`/`TZ`, `/config`, `/data`). |
| G9  | **Shareable presets.** Source + mapping configs export/import as YAML so the community can share "UFC numbered events", "Rugby World Cup", etc. |

## Non-goals (for now)

- **Usenet.** Design the abstractions so it fits later (Newznab, SABnzbd), but MVP is torrent-only.
- **Replacing Sonarr/Radarr.** If something is properly in TheTVDB/TMDB, use those apps.
- **Being a metadata database.** Generral doesn't host a central catalogue like sportarr.net. Every
  install builds its catalogue from the sources it's given. Community *presets* are files, not a service.
- **Live recording / IPTV DVR.**
- **Multi-user / RBAC.** One admin user, same as Sonarr.
- **Transcoding, splitting or joining media files.**

## Glossary

| Term | Meaning |
|------|---------|
| **Source** | A configured, pluggable fetcher producing a list of *Events*. See [04](04-sources.md). |
| **Event** | One raw item from a source (a fight card, a match, a talk), held as a JSON document plus normalised core fields (`key`, `title`, `start`). |
| **Series** | A virtual TV show Generral manages. Equivalent to a Sonarr series. |
| **Season / Episode** | Children of a Series, same meaning as in Sonarr/Plex. Episodes are *derived* from events by mappings. |
| **Mapping** | Rules binding a Source (filtered) to a Series, saying how events become seasons/episodes and how to search for them. See [05](05-mapping.md). |
| **Part** | An optional sub-division of an event (e.g. *Early Prelims / Prelims / Main Card*). A mapping can expand parts into separate episodes. |
| **Preset** | An exportable YAML bundle of Source + Mapping (+ optional Series defaults). |
| **Release** | One indexer search result (a torrent). |
| **Grab** | Sending a release to a download client for a specific episode. |
| **Import** | Moving/hardlinking the downloaded file into the library under its final name. |

## Primary use-cases

### UC1 – UFC numbered events

> "I want every numbered UFC event (UFC 300, UFC 301, …), main card only, 1080p, from now on. Older
> ones only if I click search."

- Source: TheSportsDB league `4443` (UFC), seasons by year; *or* Wikipedia "List of UFC events".
- Mapping: filter `title =~ ^UFC \d+`; season = year; episode = order within year; title = event name
  (`UFC 314: Volkanovski vs. Lopes`); search query `UFC {{ event.number }}`; release must match
  `\bUFC[ ._-]?{{ number }}\b` and must not match `prelim|fight[ ._-]?night`.
- Monitoring: *future episodes* only.
- Example: [`examples/ufc-numbered.yaml`](examples/ufc-numbered.yaml).

### UC2 – UFC with card parts

Same as UC1, but each event is expanded into 3 episodes (*Early Prelims*, *Prelims*, *Main Card*).
Each one has its own monitored flag and its own release-matching keywords. (The example file
implements UC2. Removing its `parts:` block gives UC1.)

### UC3 – Rugby World Cup

> "The whole 2027 Rugby World Cup, every match, as one season."

- Source: an HTML fixtures table, ICS calendar, or TheSportsDB league.
- Mapping: season = tournament year; episode = match number ordered by kick-off; title =
  `{{ stage }} - {{ home }} vs {{ away }}`; search query
  `Rugby World Cup {{ year }} {{ home }} {{ away }}`.
- Example: [`examples/rugby-world-cup.yaml`](examples/rugby-world-cup.yaml).

### UC4 – World Rugby Sevens Series

> "Each tournament leg (Dubai, Cape Town, …) of the SVNS season; I only want Cup finals and semis."

- Season = series season (`2024-25` mapped to season number `2025`); episode = sequence by date;
  part expansion per stage, with only `Cup Final` and `Cup Semi-Final` monitored by default.
- Example: [`examples/rugby-sevens.yaml`](examples/rugby-sevens.yaml).

### UC5 – Non-sport

A conference's talk list (JSON API) → series "PyCon US", season = year, episode = talk order, search
via a public tracker. This proves the design isn't secretly sport-specific.

## Success criteria for MVP

1. A user can go from an empty install to UFC numbered events showing in Plex, with correct titles and
   posters, **using only the UI**, in under 15 minutes, given a running Prowlarr + qBittorrent + Plex.
2. Adding Generral in Prowlarr as a "Sonarr" application syncs indexers with no errors.
3. A new UFC event announced at the source shows up as a new monitored episode within one source
   refresh interval, is grabbed automatically once a matching release appears (RSS sync), and appears
   in Plex after completion with no manual action.
