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
   - Single-episode grab + several files (e.g. `Main Card.mkv` + `Prelims.mkv`) → one file is picked:
     the only one matching the episode's rules, else the only one not excluded by `mustNot`, else the
     only one named like the episode title, else the only one named "main card", else the largest when
     ≥ 2× the next. The others are ignored and listed in the import history (`ignoredFiles`). The
     file quality is parsed from the file name when the release title has none.
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
   settings, a name or gid; Sonarr parity, Linux only). With *Delete Empty Folders*, deleting an
   episode file (not an upgrade) also deletes the season folders it leaves empty; the series folder
   stays.
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

*Implementation:* `GET /manualimport?folder=&seriesId=` lists the folder's video files (same
selection as automatic import). With `seriesId`, each file is matched against the rules of the
series' active episodes and gets the episode when exactly one matches; otherwise the rejection says
why (no match, several). Without `seriesId` (or a download), files say *Unknown Series*. With
`downloadId`, files are assigned as for automatic import. Rejections only explain a missing
assignment: once the user picks series and episodes the file is importable, except the files a
release deliberately skipped (`assignable: false`). A missing folder is a validation failure. The
folder is typed (no file browser yet); release group and languages aren't editable yet.

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
/data/media/generral/
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
- Implementation (M3b): the *Plex NFO* consumer (`/api/v1/metadata`) has Sonarr-style checkboxes
  (series metadata, episode metadata, series/season/episode images). Turning both metadata options
  off gives approach B (artwork only). Written files are tracked in `MetadataFiles` with a content
  hash (or the image URL). A series is re-synced after each import or file deletion, after each
  mapping apply or renumber, and when the series or metadata settings change. Only changed files
  are rewritten, and a Plex partial refresh follows when something changed. The settings are global
  (not per root folder or series) for now.
- **Spoiler-free mode** (UI setting, on by default): once an episode's air time has passed, the
  episode NFO leaves out `<plot>` and its thumb is hidden; the episode poster (promo art) is kept
  and used as episode image instead. The same rule applies to notifications. Titles are always kept (mapping templates keep them spoiler-free).

### Artwork

Plex local-asset file names:

| Asset | File | From |
|-------|------|------|
| Series poster | `poster.jpg` | Series images (mapping/source/upload) |
| Series background | `fanart.jpg` | |
| Series banner | `banner.jpg` | |
| Clear logo | `clearlogo.png` | |
| Season poster | `Season XX/SeasonNN.jpg` (`Season2025.jpg`; `season-specials-poster.jpg` for specials) | Mapping `season.images.poster` |
| Episode thumb | `<episode base name>.jpg` | Mapping `images.thumb`, else `images.poster` (fitted to 16:9, below) |

Images are cached in `/config/MediaCover/<seriesId>/` (Sonarr parity, served to the UI at
`/MediaCover/...`) and copied to the library on import and when they change. *M3b: no cache yet.
Images are downloaded straight into the library when their URL changes, and season posters are
named `SeasonNN.jpg` (`Season2025.jpg`).* Every sync (after import, mapping apply, series edit)
compares each file's URL with the tracked one, so a changed `images.thumb` rewrites the thumbs of
existing files too. Season poster files are only written for seasons that have a file (the season
folder must exist).

Image downloads (Generral User-Agent) go one at a time per host, at least 1 s apart. On a 429/503
a `Retry-After` of at most 60 s is waited for once; otherwise the host is skipped for the rest of
the sync (and for `Retry-After`, 60 s to 1 h, across syncs), with one warning per host
("N image(s) from <host> deferred (rate limited), retried on next sync"). Missing images are
retried by the next sync, at the latest after the next scheduled *Refresh Sources* (which applies
the mappings, then syncs their series).

Series images are set by URL in the UI (*Series → Edit Images*: poster, fanart, banner, logo).
Season posters come from the mapping's `season.images.poster` template, rendered per event (like
`season.title`, `season`/`episode` known): the season takes the first non-empty value in event
order (earliest start first), and keeps its stored poster when the template renders empty for all
of its events.

**Generated posters** (Plex NFO setting *Generate missing series/season posters*, default on):
without a series or season poster URL, a plain 1000×1500 JPEG is drawn instead (dark gradient,
series title big with the year below; for seasons the season title, `Specials`, the year or
`Season N` big with the series title below), with SkiaSharp and an embedded Noto Sans Bold.
Episodes never get one (Plex's own screenshots are better). A generated file is tracked with the
hash `generated:v<N>:…`, so it is redrawn when the title changes and replaced as soon as a real URL
appears; it never overwrites a file that exists and wasn't generated (a downloaded image whose URL
was later removed, or a hand-placed one). A failed download falls back to the generated poster only
when no file exists yet, and is retried on the next sync.

**Wide episode posters** (Plex NFO setting *Fit episode posters to 16:9 (blurred background)*,
default on): Plex shows episode images 16:9 and crops portrait posters, so when an episode image
comes from `images.poster` (not `images.thumb`), a 1920×1080 JPEG is written instead: the whole
poster centred at full height over a copy scaled to cover the frame, Gaussian-blurred (sigma 40) and
darkened by 50%. Images already landscape (width/height ≥ 1.5) and thumbs are written unchanged; an
image that can't be decoded is written as downloaded. The file is tracked with the hash
`wide-v<N>:<url>`, so raw posters written before (and fitted files, when the drawing changes) are
redrawn once on the next sync; turning the setting off writes the raw poster again.

## Rename & organise existing files

*Series → Preview Rename* (Sonarr parity) shows old → new names; *Organise* applies them. It is also
triggered after a confirmed *Renumber* (see [05](05-mapping.md#renumber)). NFO, thumbnail and
subtitle files move with their video.
