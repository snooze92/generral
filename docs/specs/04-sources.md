# 04 – Sources

A **Source** fetches data from somewhere and turns it into a list of **Events** (see
[03](03-domain-model.md#event)). Sources know nothing about series or episodes; that is what
mapping does.

## Source type plugins

Each type is a plugin with a settings schema (rendered as a Sonarr-style provider form, the same
`fields[]` mechanism used for indexers/download clients), a `Test` action, and a `Fetch` action that
returns events.

| Type | MVP | Purpose |
|------|-----|---------|
| `thesportsdb` | MVP | Built-in client for TheSportsDB (league → seasons → events). Handles API key, v1/v2, rate limit, image URLs. |
| `http-json` | MVP | Any JSON (or XML-as-JSON) HTTP API. JSONPath to select items, field extractors per item. Optional pagination. |
| `http-html` | MVP | Scrape an HTML page (e.g. a Wikipedia table). CSS selectors + Cardigann-style filters. |
| `ical` | MVP | ICS/webcal feed. `VEVENT` → event (`UID` key, `SUMMARY` title, `DTSTART`/`DTEND`, `LOCATION`, `DESCRIPTION`, `CATEGORIES`). |
| `rss` | Later | RSS/Atom items → events. |
| `static` | MVP | Events typed/pasted by the user (YAML/CSV) in the UI. For one-off tournaments or fixing a source's gaps. |
| `script` | Later | Run an executable in the container that prints events JSON to stdout. Escape hatch. Off unless `GENERRAL__ALLOWSCRIPTSOURCES=true`. |
| `generral-preset-feed` | Later | Subscribe to a shared URL serving a static events list maintained by the community. |

All types share common settings:

| Setting | Default | Notes |
|---------|---------|-------|
| `refreshInterval` | `12h` | Minimum `15m`. In the API, a number of minutes (default `720`). |
| `requestDelay` | type default (`thesportsdb`: 2 s, others 1 s) | Between HTTP requests within one fetch. |
| `headers` | `{}` | Extra headers (e.g. auth). Values may reference secrets: `{{ secret.MY_TOKEN }}`. |
| `cookies` | none | Optional raw cookie string. |
| `flaresolverr` | off | Optional: route requests via a FlareSolverr instance (Prowlarr-style proxy tag). |
| `timezone` | `UTC` | Applied to date strings that lack an offset. |
| `keepGoneEvents` | `true` | Events missing from a refresh are kept and marked `gone`, not deleted. |

Implementation notes (M1a):

- A plugin (`ISourceType`) exposes `id`, `fields[]` (Sonarr shape: `name`, `label`, `type`, `value`
  = default, `helpText`, `advanced`, `selectOptions`), `Validate(settings)` → `{ propertyName,
  errorMessage }[]`, and `Fetch(settings)` → `{ events, itemErrors, responses, meta, warnings }`.
  `responses` are summaries for the preview (URL, status, content type, body truncated at 64 KiB,
  from cache). A failed fetch throws, carrying the responses seen so far.
- `requestDelay` is a number of seconds or a duration (`500ms`, `2s`), at most 1 min. `headers` is
  an object or Sonarr's key/value list. Types without requests (`static`) only have `timezone`;
  `thesportsdb` has no `timezone` (its times are UTC). `refreshInterval` and `keepGoneEvents` live on
  the source, not in the type settings. `cookies` and `flaresolverr` are not implemented yet.
- `static`: `format` (`yaml`/`json`/`csv` with a header row), `events` text (≤ 1 MiB), optional
  `items` (JSONPath; default the root list, or the root object's `events` list) and `fields`
  (extractors as in `http-json`). Without `fields`, every property is a field as is.
- `ical`: `url` (`webcal://` is fetched over https). Recurrences are not expanded; an overridden
  instance (`RECURRENCE-ID`) gets the key `UID/<recurrence-id UTC>`. Floating times and all-day
  dates use the source timezone. Fields: `uid`, `summary`, `location`, `description`, `categories`,
  `url`, `status`, `allDay`; `_raw` holds the properties. Meta: `name` (`X-WR-CALNAME`).

## Extraction model (shared by `http-json`, `http-html`, `rss`, `static`)

A fetch has three steps:

1. **Requests**: one or more URLs. URLs are templates and can loop over variables
   (e.g. years). Optional pagination.
2. **Items**: select the repeated element (JSONPath for JSON, CSS selector for HTML).
3. **Fields**: for each item, extract named fields, each with an optional filter pipeline.

```yaml
# http-json example: generic JSON API
type: http-json
requests:
  - url: "https://api.example.org/events?league=ufc&year={{ year }}"
    foreach:
      year: "{{ (date.now.year - 1)..(date.now.year + 1) }}"   # 3 requests
    pagination:                       # optional
      next: "$.links.next"            # JSONPath to next URL, or:
      # page_param: page, start: 1, until_empty: true
items: "$.data[*]"
fields:
  key:    { path: "$.id" }
  title:  { path: "$.name" }
  start:  { path: "$.starts_at", filters: [ { name: dateparse, args: "o" } ] }
  venue:  { path: "$.venue.name" }
  number: { path: "$.name", filters: [ { name: regexp, args: "^UFC (\\d+)" } ] }
  poster: { path: "$.images.poster" }
```

```yaml
# http-html example: Wikipedia "List of UFC events" scheduled-events table
type: http-html
requests:
  - url: "https://en.wikipedia.org/wiki/List_of_UFC_events"
items: "table#Scheduled_events tbody tr:has(td)"
fields:
  title:  { selector: "td:nth-child(1)" }
  date:   { selector: "td:nth-child(2)" }
  venue:  { selector: "td:nth-child(3)" }
  key:    { text: "{{ title | string.slugify }}" }        # no natural id: derive one
  start:  { text: "{{ date }}", filters: [ { name: dateparse, args: "MMM d, yyyy" } ] }
  number: { text: "{{ title }}", filters: [ { name: regexp, args: "^UFC (\\d+)" } ], optional: true }
```

### Field extractor

| Property | Meaning |
|----------|---------|
| `path` | JSONPath (JSON sources). |
| `selector` | CSS selector relative to the item (HTML). Comma-separated alternatives take the first match. `xpath:` prefix for an XPath expression instead (e.g. `xpath:preceding::h3[1]` to read the section heading above a match box). `attribute: href` reads an attribute instead of text. `remove: "sup, .reference"` strips sub-elements first (useful on Wikipedia). |
| `text` | A Scriban template over the fields extracted *so far* (fields are evaluated in order). |
| `filters` | Pipeline, applied in order. |
| `optional` | Missing value → null instead of an item error. |
| `default` | Value used if missing. |

Engine details (decided in M1):

- `requests` entries are a URL string or `{ url, foreach, pagination }`. `foreach` variables are
  templates returning a value, an array or a range; one request per combination (cartesian product).
- `pagination` sets either `next` (JSONPath to an absolute or relative URL, stops on null or an
  already-seen URL) or `page_param` (`start`, default 1; `until_empty`, default true). `max_pages`
  (default 10, max 100) caps the pages per request. One extraction is capped at 50 requests.
- A `path` that matches several nodes yields a JSON array; one match yields the scalar.
- `text` templates see the previous fields by name, `vars` (the `foreach` variables) and `request.url`.
- `filters` entries are `{ name, args }` (args a string or a list) or just the filter name.
  An unknown filter or invalid arguments is a definition error. A filter failing on a value is an
  item error (or null for `optional` fields).
- Unparseable documents, invalid URLs and the request cap fail the whole fetch. Item problems
  (missing key/title, bad date, duplicate key) are collected per item and do not fail the fetch.
- Every event keeps the item JSON under `fields._raw` (truncated to 16 KB).

### Filters

Same names and semantics as Prowlarr Cardigann where they exist:
`replace`, `re_replace`, `regexp` (first capture group), `split`, `trim`, `prepend`, `append`,
`tolower`, `toupper`, `urldecode`, `urlencode`, `htmldecode`, `dateparse` (.NET/Go layout),
`timeago`/`fuzzytime`, `querystring`, `validate`, `diacritics`, `jsonjoinarray`, plus Generral
additions `toint`, `tofloat`, `slugify`, `timezone` (re-interpret a local date in an IANA tz).

- `dateparse` takes .NET formats (one or a list, tried in order). No args, `o` or `iso` = any ISO /
  common format. Values with an offset become UTC; values without one stay local and are converted
  with the source `timezone` (or the `timezone` filter). Go layouts are not supported.
- `regexp` returns the first capture group (the whole match if there is no group), null if no match.
- `split` takes `[separator, index]`; a negative index counts from the end.
- `validate` keeps the comma-separated values that appear in the allowed list (case-insensitive).
- `jsonjoinarray` takes `[jsonpath, separator]`.
- `timeago` parses `now`, `today`, `yesterday`, `2 hours ago`, `in 3 days`, …; `fuzzytime` also
  accepts absolute dates.

### Reserved fields

After extraction, these fields fill the event's core attributes. Everything else stays in
`event.fields`.

| Field | Required | Maps to |
|-------|----------|---------|
| `key` | **yes** | `Event.key`. Items with no key are rejected (shown as errors in preview). |
| `title` | **yes** | `Event.title` |
| `start`, `end` | no | `Event.start/end` (UTC after timezone handling) |
| `status` | no | `Event.status` (values normalised: `cancelled`/`canceled`, `postponed`, `ft`/`finished` → `completed`, …) |
| `poster`, `fanart`, `thumb`, `banner`, `logo` | no | `Event.images` (relative URLs resolved against the request URL; non-HTTP(S) dropped) |

`start`/`end` accept a parsed date, an ISO-like string or a Unix timestamp (seconds or
milliseconds). Keys must be unique within a fetch; later duplicates are item errors.

## `thesportsdb` source

Purpose-built because it covers a lot of sport and its free tier has awkward limits.

| Setting | Notes |
|---------|-------|
| `apiKey` | Default `123` (free). Premium key enables v2 and larger limits. |
| `leagueId` | e.g. `4443` (UFC), `4574` (Rugby World Cup). There is a search box in the UI (`search_all_leagues` / v2 `search/league`). |
| `seasons` | `all`, `current`, or a list (`2024`, `2025`, `2024-2025`). A range `2023..current` (or `2023..2025`) selects the league's seasons whose first year is in the range. `all` and ranges read `search_all_seasons`. At most 50 seasons. |
| `includePast`/`includeFuture` | Use `eventspastleague` / `eventsnextleague` to top up the current season between full refreshes. |

Output fields: everything in the TheSportsDB event object (`idEvent`, `strEvent`, `strSeason`,
`intRound`, `dateEvent`, `strTime`, `strTimestamp`, `strVenue`, `strCity`, `strCountry`,
`strHomeTeam`, `strAwayTeam`, `strPoster`, `strThumb`, `strFanart`, `strBanner`, `strDescriptionEN`,
`strStatus`, …), plus reserved fields: `key = idEvent`, `title = strEvent`,
`start = strTimestamp` (or `dateEvent + strTime`, UTC).

Free-tier caveats (documented in the UI help text): a season schedule returns at most 15 events on
the free key, which is not enough for a full rugby world cup. The plugin warns when the returned
count equals the known cap, and suggests premium or combining with another source.

League/series images (`strPoster`, `strFanart`, `strBanner`, `strLogo`, `strBadge`) are available
to the mapping as `source.meta.*` so a series can take its artwork from the league.

v1 endpoints used: `lookupleague.php?id=` (meta; the whole league object, `strFanart` defaulting to
`strFanart1`), `eventsseason.php?id=&s=`, and `eventspastleague.php` / `eventsnextleague.php`
(`includePast`/`includeFuture`, default on). Events are de-duplicated by `idEvent`, the season
schedule winning. Images: `strPoster`, `strFanart`, `strThumb`, `strBanner`. v2 (premium) is not
implemented yet. The API key is part of the URL path and is redacted from logs.

## Fetch semantics

- **Upsert** by `(sourceId, key)`. A changed `hash` sets `lastChanged` and creates an event revision
  (kept for 30 days) so that the UI can show "title changed from *UFC 322: TBD vs TBD* to
  *UFC 322: Della Maddalena vs Makhachev*".
- **Gone** events: not returned by N=3 consecutive successful refreshes → `status = gone` (kept).
  A failed fetch (non-2xx, parse error, zero items when there were items before) **never** marks
  events gone. It records `lastError` and raises a health warning after 24 h.
- **Zero-item guard:** if a refresh returns 0 items but the previous one returned > 0, treat it as a
  failure (protects against layout changes on scraped pages).
- **Caching:** conditional GET, plus a raw-response cache (per URL, 1 h) so that editing a mapping
  and previewing doesn't refetch. *(M1a: in-memory, keyed by URL + extra headers, 2xx only, at most
  256 entries; bypassable per fetch, e.g. for scheduled refreshes. Conditional GET is not
  implemented yet.)*
- **Concurrency:** one fetch per source at a time; global max 4 concurrent source fetches.

Implementation notes (M1a):

- The hash is a SHA-256 of everything but the key (title, start, end, timezone, status, fields
  including `_raw`, images). A `gone` event that is returned again gets its status back and a
  revision. Revisions older than 30 days are deleted at the end of each successful refresh of the
  source.
- `missedRefreshes` counts consecutive successful refreshes without the event; an item that failed
  extraction but still has a `key` counts as returned. Failures (fetch errors, invalid settings,
  zero-item guard) leave events untouched and set `lastRefresh` and `lastError` (secrets removed
  with the log cleansing rules), not `lastSuccess`. The zero-item guard compares with the events
  returned by the previous successful refresh (`missedRefreshes = 0`).
- Scheduled refreshes bypass the raw-response cache; `POST /source/test` uses it.
- A source is due when it is enabled and `lastRefresh + refreshInterval` has passed (never
  refreshed = due at once). `RefreshSources` refreshes the due sources, or all enabled ones with
  `force: true`; `RefreshSource` refreshes one, due or not.

## Source editor UI

See also [10 – UI](10-ui.md#sources).

- Form (or "Advanced: YAML") on the left. On the right, a **live preview** with three tabs:
  *Raw response* (pretty-printed, with selectors/paths highlighted on hover), *Items* (table of
  extracted fields per item, errors in red), *Events* (normalised core fields).
- *Test* button = fetch + extract without saving.
- *Pick* helper for HTML: click an element in a sandboxed render of the page to get a CSS selector
  (`Later`).
