# 07 – Download clients

## Abstraction

`IDownloadClient` (Sonarr-derived): `Test()`, `Download(release, episodes) → downloadId`,
`GetItems() → DownloadClientItem[]`, `RemoveItem(id, deleteData)`, `GetStatus()` (output root
folders for remote-path-mapping hints), `MarkItemAsImported(item)` (optional post-import category).

`DownloadClientItem`: `downloadId` (infohash for torrents), `title`, `category`, `totalSize`,
`remainingSize`, `remainingTime`, `status` (`queued`, `paused`, `downloading`, `completed`,
`failed`, `warning`), `message`, `outputPath`, `canMoveFiles`, `canBeRemoved`, `seedRatio`,
`seedTime`.

| Client | MVP | Notes |
|--------|-----|-------|
| **qBittorrent** (WebUI API v2, qBittorrent ≥ 4.1, tested on 5.x) | MVP | Port of Sonarr `QBittorrentProxyV2`. |
| Transmission, Deluge, rTorrent | Later | Port from Sonarr as needed. |
| Torrent Blackhole | Later | Writes `.torrent`/`.magnet` to a folder, watches another for completed files. |
| SABnzbd, NZBGet | Later (with Usenet) | |

## qBittorrent

### Settings (same names/defaults as Sonarr)

| Setting | Default | Notes |
|---------|---------|-------|
| Host / Port / Use SSL / URL Base | `localhost` / `8080` / off / empty | |
| Username / Password | | Login: `POST /api/v2/auth/login` (form), with a `Referer` header equal to the base URL. Reuse **whatever cookie name the server sets** (`SID`, or `QBT_SID_<port>` on newer builds). Re-login on 403. |
| Category | `generral` | Created if missing (`/api/v2/torrents/createCategory`). |
| Post-Import Category | empty | If set, the torrent is moved to this category after import (e.g. `generral-imported`). |
| Recent / Older Priority | Last / Last | Older = air date > 14 days ago. Uses queue priority if queueing is enabled. |
| Initial State | Start | `Start`, `Force Start`, `Stop` (pause). |
| Sequential Order / First and Last First | off / off | |
| Content Layout | Default | `Default`, `Original`, `Subfolder`, `NoSubfolder`. |
| Tags | — | Optional extra qBittorrent tags. Generral doesn't rely on tags for tracking; it uses the infohash. |

### Operations

| Purpose | Endpoint(s) |
|---------|-------------|
| Version check | `GET /api/v2/app/version`, `GET /api/v2/app/webapiVersion` |
| Preferences (save path, ratio limits, queueing) | `GET /api/v2/app/preferences` |
| Add | `POST /api/v2/torrents/add` (multipart: `urls` for magnet, or `torrents` file upload; `category`, `paused`/`stopped`, `sequentialDownload`, `firstLastPiecePrio`, `contentLayout`, `ratioLimit`, `seedingTimeLimit`, `tags`) |
| Find added torrent | Infohash comes from the magnet/torrent (computed locally for `.torrent`). If a magnet has no hash in its URL, poll `torrents/info?category=` for a new item. |
| Poll | `GET /api/v2/torrents/info?category=<cat>` (and `/sync/maindata` incremental, `Later`) |
| Files (for import) | `GET /api/v2/torrents/files?hash=` |
| Set share limits | `POST /api/v2/torrents/setShareLimits` (from indexer seed criteria) |
| Set category post-import | `POST /api/v2/torrents/setCategory` |
| Remove | `POST /api/v2/torrents/delete` (`deleteFiles=true/false`) |

`.torrent` vs magnet: Generral downloads the `.torrent` file itself (through the indexer's download
link, so cookies/API keys go to the indexer and not to qBittorrent) and uploads it. If the link
redirects to a `magnet:`, it passes the magnet instead (Sonarr behaviour).

### Status mapping

| qBittorrent `state` | Generral status |
|---------------------|-----------------|
| `error`, `missingFiles` | `warning` (message: qBittorrent error) |
| `pausedDL`/`stoppedDL` | `paused` |
| `queuedDL`, `checkingDL`, `checkingUP`, `checkingResumeData`, `allocating`, `moving` | `queued` |
| `metaDL`, `forcedMetaDL` | `queued` (message: "Downloading metadata") |
| `downloading`, `forcedDL`, `stalledDL` | `downloading` (`stalledDL` → `warning` with message after 1 h with no progress) |
| `uploading`, `stalledUP`, `queuedUP`, `forcedUP`, `pausedUP`/`stoppedUP` | `completed` |

`canBeRemoved` = seeding goals reached (ratio/time from torrent or global prefs) **and** the torrent
is paused/stopped, or the max-ratio action is "remove". Same rules as Sonarr, so private-tracker
seeding requirements are respected.

## Tracked downloads & queue

- Every grab creates a **TrackedDownload** keyed by `(downloadClientId, downloadId)`, linked to the
  episode ids and the grabbed release (indexer, title, size, quality, protocol).
- Items found in the Generral category that aren't tracked (added by hand) show in the queue as
  *Unknown* with a "Manual import" action (Sonarr parity).
- **Queue** (*Activity → Queue*): series, episode(s), quality, progress, ETA, status, client,
  indexer, actions: *Remove* (options: remove from client, blocklist, skip redownload), *Manual
  import*, *Grab again*.
- **Failed download handling:** client reports `failed`/`error`, or the import fails because no
  video files are found → mark failed → blocklist the release → (if *Redownload failed* is on)
  automatically search again.
- **Stalled handling** (optional, off by default): torrent stalled with 0 % progress for > X hours →
  treat as failed.
