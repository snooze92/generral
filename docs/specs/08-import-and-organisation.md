# 08 – Import & organisation

The goal is for a Plex **TV Shows** library pointed at the root folder to show every Generral series
with the right series, season and episode titles, air dates and artwork, **without installing any
Plex agent or plugin**.

## Completed download handling

Sonarr parity, with matching driven by the tracked download rather than by parsing:

1. `CheckForFinishedDownloads` (every minute) polls clients. Items that are `completed` and tracked
   become *ImportPending*.
2. **Path translation** via *Remote Path Mappings* (host, remote path → local path), for when
   qBittorrent and Generral see different mount points. Health check: "Download client reports
   files in `/downloads/...` but this path doesn't exist in Generral."
3. **File selection:** list video files in the output path (`.mkv .mp4 .avi .m4v .ts .mov .wmv
   .webm`). Exclude `sample` files (< 5 % of the largest file, or named `sample`) and extras.
   Archives (`.rar`) are not unpacked (use Unpackerr, as with Sonarr).
4. **File → episode assignment:**
   - Single-episode grab + one video file → that episode.
   - Pack grab (several parts) + several files → each file's name is matched against each covered
     episode's *part* rules (`must`/`mustNot`). Unmatched files are left for manual import.
   - Pack grab + one file → **multi-episode file** covering all its episodes.
   - Ambiguous → *Manual import required* (queue shows ⚠ with a button).
5. **Specifications** (Sonarr parity): not a sample, enough free space, is an upgrade (if a file
   exists), not already imported, not still unpacking.
6. **Transfer:** hardlink if same filesystem and *Use Hardlinks instead of Copy* is on (default
   **on**), else copy (torrent must keep seeding). Never move torrent payloads unless the client
   reports `canMoveFiles`.
7. **Rename** using the naming format (below). Old file (on upgrade) goes to the recycle bin if one
   is configured, else it is deleted.
8. Extras: subtitles (`.srt .ass .ssa .sub/.idx .vtt`) with the same base name get carried over as
   `<episode name>.<lang>.<ext>` (Sonarr parity).
9. Write **metadata** (NFO/art, below) and set permissions (`chmod` folder/file, `chown` group from
   settings; Sonarr parity, Linux only).
10. History *Imported*, `DownloadFolderImported` event → Connect (Plex refresh, notifications).
11. Mark the item imported in the client (post-import category), and remove it once seeding goals
    are met if *Remove Completed* is on.

Implementation notes (M3a): a file smaller than 5 % of the largest *or* with `sample` as a word in its
name is a sample; Sonarr's extras folders/files (`Featurettes/`, `-trailer.mkv`, …) and hidden
folders are skipped. Pack assignment uses each episode's resolved `must`/`mustNot`/`any` rules on the
file name; a file matching several episodes, or several files matching one, is left for manual import.
Specifications: not unpacking (`_UNPACK_`), not already imported (history of the download id), is an
upgrade of every existing file, free space (`minimumFreeSpaceWhenImporting`, default 100 MB). The
series folder default is `{Series TitleYear}` (renders the plain title without a year). Imported
downloads leave the queue; `removeCompletedDownloads` removes them from qBittorrent once seeding goals
are met (`canBeRemoved`). Recycle bin cleanup (`CleanUpRecycleBin`) runs daily.

**Manual import** (*Wanted → Manual Import* and the queue action): browse a folder, list videos,
choose series/season/episode(s), quality and release group for each file (with suggestions from
part rules and the source filename), then import with *Move* or *Copy/Hardlink*.

**Library import** (existing files, `Later`): scan a root folder for existing
`Series/Season X/…SxxEyy…` files and link them to episodes, so an existing hand-curated sports
library can be adopted.

## Naming

*Settings → Media Management*, same tokens and UI as Sonarr (with a live example), plus a few
Generral tokens.

### Defaults

| Setting | Default |
|---------|---------|
| Rename Episodes | **on** (Sonarr default is off; Generral needs it because source file names are unhelpful) |
| Replace Illegal Characters | on |
| Colon Replacement | `Smart` (` - ` between words; `UFC 314: Volkanovski` → `UFC 314 - Volkanovski`) |
| Standard Episode Format | `{Series Title} - S{season:00}E{episode:00} - {Episode Title} [{Quality Full}]` |
| Daily Episode Format | `{Series Title} - {Air-Date} - {Episode Title} [{Quality Full}]` |
| Multi-Episode Style | `Prefixed Range` → `S2026E11-E13` |
| Series Folder Format | `{Series Title}` (or `{Series Title} ({Series Year})` if the series has a year) |
| Season Folder Format | `Season {season}` (no zero-padding, so a year season becomes `Season 2025`) |
| Specials Folder Format | `Specials` |

### Tokens

Sonarr tokens: `{Series Title}`, `{Series CleanTitle}`, `{Series TitleYear}`, `{Series Year}`,
`{season:0}`/`{season:00}`, `{episode:0}`/`{episode:00}`, `{absolute:000}`, `{Episode Title}`,
`{Episode CleanTitle}`, `{Air-Date}`, `{Air Date}`, `{Quality Full}`, `{Quality Title}`,
`{MediaInfo Simple}` (`Later`), `{Release Group}`, `{Original Title}`, `{Original Filename}`,
`{Preferred Words}`, `{Custom Formats}` (`Later`).

Generral tokens: `{Part Title}`, `{Part Key}`, `{Event Key}`, `{Generral Id}` (renders
`generral-ep-<id>`, a stable id that survives renumbering, for future custom-provider matching),
and `{Event:<field>}` for any mapped event field (e.g. `{Event:venue}`).

### Resulting layout (UC1/UC2/UC3)

```
/data/media/sports/
├── UFC/
│   ├── tvshow.nfo
│   ├── poster.jpg  fanart.jpg  banner.jpg  clearlogo.png
│   ├── Season 2025/
│   │   ├── Season2025.jpg
│   │   ├── UFC - S2025E04 - UFC 314 - Volkanovski vs. Lopes - Main Card [WEBDL-1080p].mkv
│   │   ├── UFC - S2025E04 - UFC 314 - Volkanovski vs. Lopes - Main Card [WEBDL-1080p].nfo
│   │   └── UFC - S2025E04 - UFC 314 - Volkanovski vs. Lopes - Main Card [WEBDL-1080p].jpg
│   └── Season 2026/ …
└── Rugby World Cup/
    ├── tvshow.nfo  poster.jpg  fanart.jpg
    └── Season 2027/
        ├── Rugby World Cup - S2027E01 - Pool A - Australia vs Hong Kong China [HDTV-1080p].mkv
        └── …
```

Year seasons (`S2025E04`) are matched correctly by Plex's TV scanner. Sportarr uses this convention
in production.

## Plex-correct metadata

Generral supports three approaches. The user chooses per root folder or per series (*Settings →
Metadata*, Sonarr's "Metadata" page, with a new consumer **Plex NFO**).

| Approach | What the user does in Plex | What Generral writes | MVP |
|----------|---------------------------|----------------------|-----|
| **A. Plex NFO agent** (recommended) | TV library → *Advanced* → Agent **Plex NFO Series** (PMS ≥ 1.43.1). Optionally a custom agent with *Plex NFO Series* first and *Plex Local Media* second. | `tvshow.nfo`, per-episode `.nfo`, local artwork | **MVP** |
| **B. Personal Media Shows / local assets** | TV library with agent *Personal Media Shows*, "Use local assets" on. | Local artwork only. Titles come from file names (so the episode title must be in the file name, which it is with the default format). | MVP (same files as A, minus NFO) |
| **C. Generral as a Plex Custom Metadata Provider** | *Settings → Metadata Agents → + Add Provider* → `http://generral:8990/plex`. | Nothing extra on disk; Plex queries Generral. | Later |

### A. NFO files (Kodi format, Plex NFO agent subset)

`tvshow.nfo` in the series folder:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<tvshow>
  <title>UFC</title>
  <plot>Numbered UFC pay-per-view events.</plot>
  <premiered>1993-11-12</premiered>
  <status>Continuing</status>
  <studio>UFC</studio>
  <genre>Sport</genre>
  <genre>MMA</genre>
  <uniqueid type="generral" default="true">series-8f1c…</uniqueid>
  <namedseason number="2025">2025</namedseason>
</tvshow>
```

Per-episode `<file base name>.nfo`:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<episodedetails>
  <title>UFC 314: Volkanovski vs. Lopes - Main Card</title>
  <season>2025</season>
  <episode>4</episode>
  <plot>Kaseya Center, Miami.</plot>
  <aired>2025-04-12</aired>
  <runtime>180</runtime>
  <uniqueid type="generral" default="true">ep-1234</uniqueid>
</episodedetails>
```

- Multi-episode files: one `<episodedetails>` block per episode in the same `.nfo` (Plex NFO agent
  supports this).
- `<uniqueid type="generral">` gives Plex a stable GUID (`tv.plex.agents.nfo…/generral_ep-1234`), so
  watch state survives renames.
- NFO is rewritten when episode metadata changes (title update at the source). Then a Plex
  partial refresh of that path is triggered.
- `season.nfo` is not written (Plex ignores it).

### Artwork

Plex local-asset file names:

| Asset | File | From |
|-------|------|------|
| Series poster | `poster.jpg` | Series images (mapping/source/upload) |
| Series background | `fanart.jpg` | |
| Series banner | `banner.jpg` | |
| Clear logo | `clearlogo.png` | |
| Season poster | `Season XX/SeasonNN.jpg` (`Season2025.jpg`; `season-specials-poster.jpg` for specials) | Mapping `season.images.poster` |
| Episode thumb | `<episode base name>.jpg` | Mapping `images.thumb` |

Images are cached in `/config/MediaCover/<seriesId>/` (Sonarr parity, served to the UI at
`/MediaCover/...`) and copied to the library on import and when they change.

## Rename & organise existing files

*Series → Preview Rename* (Sonarr parity) shows old → new names; *Organise* applies them. It is also
triggered after a confirmed *Renumber* (see [05](05-mapping.md#renumber)). NFO, thumbnail and
subtitle files move with their video.
