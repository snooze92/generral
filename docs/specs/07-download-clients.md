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

## Implementation notes (M2d)

- Download clients: `GET/POST/PUT/DELETE /downloadclient`, `/downloadclient/schema`, `/downloadclient/test` (Sonarr
  provider shape, like indexers; `password` is masked as `********`). Saving an enabled client runs the test unless
  `?forceSave=true`. Implementation `QBittorrent`, `configContract: QBittorrentSettings`; field names are Sonarr's
  (`host`, `port`, `useSsl`, `urlBase`, `username`, `password`, `tvCategory`, `tvImportedCategory`, `recentTvPriority`,
  `olderTvPriority`, `initialState`, `sequentialOrder`, `firstAndLast`, `contentLayout`) plus `torrentTags`. No API-key
  auth, no `addSeriesTags`/`seedTimeType` yet. Requests go through the SSRF guard (LAN hosts allowed, link-local blocked).
- Client choice: the indexer's `downloadClientId`, else the enabled torrent client with the lowest priority number
  (tag rules as Sonarr, no round-robin). No client → the grab fails with "No torrent download client is available".
- Grab: Generral fetches the indexer's `.torrent` link (no automatic redirects; a `magnet:` redirect is passed on as a
  magnet), computes the infohash, and uploads the file; it falls back to the magnet link when the fetch fails. Seed
  ratio/time come from the indexer's `seedCriteria`. The category is created if missing. One `grabbed` history row is
  written per episode (`downloadId` = upper-case infohash, Sonarr's `data` keys in camelCase).
- Tracked downloads are in memory and rebuilt from the client listing (`torrents/info?category=`) and the grab history.
  `RefreshMonitoredDownloads` runs every minute (scheduler check interval is now 1 min) when an enabled client exists.
  States: `downloading`; `importPending` for completed grabs (import is M3); `importBlocked` for completed unknown
  items; `failed` after failed download handling; `ignored` after *Remove* without client removal or blocklist.
  qBittorrent `error`/`missingFiles` are warnings (Sonarr parity, not failures). `stalledDL` warns after 1 h.
- Failed download handling: a client `failed` status, or (if `stalledDownloadTimeout` > 0 hours) a torrent stalled at
  0 % for longer → history `downloadFailed` + blocklist (hash, title, indexer) → removed from the client if
  *Remove Failed* and the item can be removed → `EpisodeSearch { episodeIds }` if `autoRedownloadFailed` (and, for
  interactive grabs, `autoRedownloadFailedFromInteractiveSearch`). `GET/PUT /settings/downloadclient` holds these three
  settings (defaults true/true/0).
- Queue API: `GET /queue?page&pageSize&includeUnknownSeriesItems` (Sonarr paging shape, one record per download with
  `episodeIds`), `DELETE /queue/{id}?removeFromClient=true&blocklist=false&skipRedownload=false`. History:
  `GET /history?page&pageSize&sortDirection&episodeId&seriesId&downloadId&eventType`. Blocklist: `GET /blocklist?page&pageSize&seriesId`,
  `DELETE /blocklist/{id}`.
- Not yet: post-import category, share-limit updates after add, bulk queue/blocklist endpoints, *Grab again*,
  *Manual import*, `/sync/maindata`, download client health checks.
