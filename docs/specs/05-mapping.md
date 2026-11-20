# 05 – Mapping

Mapping is what makes Generral different from Sonarr. A **Mapping** takes the events from one source,
chooses which ones to keep, and says how each kept event (or each *part* of an event) becomes an
**Episode** of a **Series**: season number, episode number, title, dates, artwork, and how to search
for it.

## Shape

```yaml
mapping:
  name: UFC Numbered Events
  series: UFC                         # target series (by title or id)
  source: TheSportsDB – UFC           # source (by name or id)
  priority: 0

  filter:                             # which events to keep (all must pass)
    - '{{ event.title | regex.match "^UFC \\d+" }}'
    - '{{ event.status != "cancelled" }}'

  vars:                               # computed once per event, available as `vars.*`
    number: '{{ (event.title | regex.match "^UFC (\\d+)")[1] | string.to_int }}'
    headline: '{{ event.title | string.split ":" | array.last | string.strip }}'

  parts:                              # optional: expand one event into several episodes
    - { key: early-prelims, title: Early Prelims, monitored: false }
    - { key: prelims,       title: Prelims,       monitored: false }
    - { key: main-card,     title: Main Card,     monitored: true  }

  season:
    strategy: year                    # see "Numbering strategies"
  episode:
    strategy: sequential              # by `order` within season
    order: '{{ event.start }}'
    # number: '{{ vars.number }}'     # alternative: strategy: field

  absolute: '{{ vars.number }}'       # optional absoluteEpisodeNumber
  title: 'UFC {{ vars.number }}: {{ vars.headline }}{{ if part }} - {{ part.title }}{{ end }}'
  overview: '{{ event.fields.strVenue }}, {{ event.fields.strCity }}'
  airDate: '{{ event.start }}'
  runtime: '{{ if part }}90{{ else }}300{{ end }}'
  images:
    thumb: '{{ event.images.thumb ?? event.images.poster }}'

  search:                             # see 06 – Indexers & search
    queries:
      - 'UFC {{ vars.number }}'
    must:
      - '\bUFC[ ._-]?{{ vars.number }}\b'
    mustNot:
      - '\bUFC[ ._-]?{{ vars.number }}\d'           # UFC 31 must not match UFC 314
      - 'fight[ ._-]?night'
    parts:
      early-prelims: { must: ['early[ ._-]?prelim'] }
      prelims:       { must: ['(?<!early[ ._-]?)prelim'] }
      main-card:     { mustNot: ['prelim'] }
```

All string values are **Scriban templates**. The template context:

| Variable | Contents |
|----------|----------|
| `event` | `key`, `title`, `start`, `end`, `status`, `images`, `fields` (all extracted fields), `source` (name, type) |
| `source.meta` | Source-level metadata (e.g. TheSportsDB league info) |
| `vars` | Values computed in `vars:` (evaluated in order; later vars can reference earlier ones) |
| `part` | Current part (`key`, `title`, `index`, plus any custom keys) or `null` |
| `series` | Target series (`title`, `year`, …) |
| `season` | Resolved season number (available to `title`, `overview`, `search`) |
| `episode` | Resolved episode number (same) |

Built-in Scriban functions are available (including `regex.replace`, `regex.escape`,
`array.compact`, `array.join`), plus Generral helpers: `regex.match` (returns the groups array, or
null), `date.add <duration>` (`-2h`, `3d`; null-safe), `date.year`, `date.tz(tz)`,
`string.slugify`, `string.romanize` (`XXV` → 25),
`string.normalize_team` (strips punctuation, `&`→`and`), `sport.country_code` (`New Zealand` → `NZL`,
`NZ`).

## Numbering strategies

### Season

| Strategy | Season number | Typical use |
|----------|---------------|-------------|
| `year` | Year of `airDate` (series timezone) | UFC, most sport. Plex shows `Season 2025`. |
| `field` | `season.value` template, must be an int | e.g. tournament edition, or `{{ event.fields.strSeason \| string.split "-" \| array.last }}` for `2024-2025` → `2025`. |
| `fixed` | `season.value` (constant) | One-off tournament as its own series, or "everything in Season 1". |
| `sequential` | Order of distinct `season.group` values, from 1 | Grouping by an arbitrary key (e.g. tournament name) without caring about the number. |

`season.title` (optional template) sets a display name (`2024-25`, `France 2023`), written to
`tvshow.nfo` `<namedseason>`.

### Episode

| Strategy | Episode number | Notes |
|----------|----------------|-------|
| `sequential` (default) | Position by `order` within the season, from 1 | Combined with [numbering stability](#numbering-stability). With parts, each part takes the next number in `parts` order. |
| `field` | `episode.value` template (int) | e.g. official match number, round number. Mapping errors on collisions. |
| `date` | No number; series becomes `seriesType: daily` | File named `Series - 2025-03-08 - Title`. Plex date-based TV. Good for "one event per day" content. |

### Parts

`parts` turns one event into several episodes. Each part:

- has a stable `key` (used in `EpisodeLink.partKey`); the list order sets episode order within the
  event;
- may have `when:` (template → bool) to include it only for some events (e.g. only events where
  `event.fields.hasEarlyPrelims`);
- may override `title`, `airDate` (e.g. `{{ event.start | date.add_hours -2 }}` for prelims),
  `runtime`, `monitored` (the default monitored state for *new* episodes of that part), and `search`.

Parts can also come from the data instead of a fixed list: `parts.from: '{{ event.fields.stages }}'`
plus `parts.key`/`parts.title` templates per element. This is used for Sevens tournaments, where stages
come from the source.

> Why parts are episodes rather than Plex "pt1/pt2" stacking: Plex treats split parts as *one
> episode*. Prelims and main cards are released separately, sometimes in different qualities, and
> often only one of them is wanted. They need their own monitored flags, searches and files, so they
> have to be separate episodes.

## Numbering stability

Episode numbers end up in file names and Plex watch history. If they change, Plex shows the wrong
episode or loses watch state. Rules:

1. **Links are sticky.** Once an `EpisodeLink (mapping, event, part) → episode` exists, re-running
   the mapping updates that episode's title/dates/images but **does not change its season/episode
   number**, unless the user explicitly asks for a *Renumber* (below).
2. **New events append.** With `sequential`, a newly appearing event whose `order` puts it *between*
   existing episodes still gets a new number. The default policy is `onInsert: append`: it takes
   `max+1`, and the UI flags the season as "out of order". Alternatives:
   - `onInsert: renumber-unlocked`: renumber freely among episodes that have **no file and no
     grab history** (`numberLocked = false`); locked ones keep their numbers. This is the right default
     for fixtures lists that are published all at once and then refined.
   - `onInsert: renumber-all`: always reflect `order`. Existing files are renamed on disk (and Plex
     refreshed) as part of the same command. Never automatic: a preview + confirmation is required,
     or the mapping must explicitly opt in (`autoRenumber: true`).
3. **Locking.** An episode's number becomes `numberLocked` when it gets a file, is grabbed, or
   when its air date has passed. The user can lock/unlock manually.
4. **Gone/filtered events.** If an event disappears or stops passing the filter, its episode becomes
   `orphaned` (see [03](03-domain-model.md#episode)). Its number is **not reused** by the default
   policies.
5. **Collisions.** If a `field`-strategy number collides with an existing different episode, that
   event is not applied. It shows up as a mapping error in the preview and as a health warning.
6. **Multiple mappings → one series.** Allowed (e.g. a TheSportsDB mapping plus a `static` source
   that patches missing events). Slots are reserved per mapping in `priority` order. A `dedupe`
   template (e.g. `{{ event.start | date.to_string "%F" }}-{{ vars.number }}`) makes a
   lower-priority mapping skip events already covered by a higher-priority one.

### Renumber

The *Series → Renumber…* action computes the "ideal" numbering from the current mapping, shows a
diff (old → new number, file renames), and applies it in one command.

## Dates and timezones

- `airDate` templates must produce an ISO-8601 datetime. If the result has no offset, it is
  interpreted in the source's `timezone` setting.
- `Episode.airDateUtc` is stored in UTC. `Episode.airDate` (local date) uses the **series timezone**
  (default container `TZ`). This matters for year seasons (a UFC event on 31 Dec 22:00 ET belongs to
  the previous year in ET but the next in UTC). `season.strategy: year` uses the local date.

## Spoilers

Sport is spoiler-sensitive. Defaults:

- `overview` templates in shipped presets never include results/winners.
- Source fields that are known to carry results (`intHomeScore`, `strResult`, `strDescriptionEN`
  for completed events in TheSportsDB) are listed in the UI with a ⚠ badge in the field picker.
- A per-series setting `spoilerFree: true` (default) also strips the `event.fields.*` values tagged
  as spoilers from the template context for events whose `status = completed`.

## Preview & dry-run

The mapping editor has a **live preview** (re-rendered on each edit, debounced):

| Column | |
|--------|-|
| Event | key, title, start, status |
| Kept? | ✔ / ✘ (with the filter that rejected it) |
| Part | |
| → S/E | Proposed season/episode. Rows where this would differ from the current number are highlighted (*would renumber*, *new*, *orphaned*). |
| Title | Rendered episode title |
| Path | Rendered file name (using the current naming format) |
| Search | Rendered queries + must/mustNot regexes |
| Errors | Template errors, collisions |

Plus a summary bar: *N events · K kept · E episodes · +new · ~changed · −orphaned · ⚠ errors*.
Saving a mapping triggers `ApplyMappings` for that mapping only.

## Presets

A **preset** is a YAML file bundling `source`, `series` defaults (title, overview, images, monitor
mode, quality profile name, tags), and `mapping`. Presets:

- ship in the image under `/app/presets/` (read-only, e.g. `ufc-numbered`, `ufc-numbered-parts`,
  `ufc-fight-night`, `rugby-world-cup`, `six-nations`, `svns`, `formula-1-sessions`);
- can be imported from a file or URL and exported from any existing series ("*Export as preset*");
- carry a `schema: generral/preset@1` header and `requires: { generral: ">=0.3" }`;
- never include secrets (API keys are replaced by `{{ secret.NAME }}` placeholders, and the user is
  prompted on import).

The *Add Series* flow (see [10](10-ui.md#add-series)) starts with *From preset* or *From scratch*.

## Import/export

Every Source, Series and Mapping has *Export YAML* / *Import YAML* actions. The YAML form is the
same document the "Advanced" editor tab shows, so there is a single declarative format and the UI is
a view over it.

## Worked examples

- [`examples/ufc-numbered.yaml`](examples/ufc-numbered.yaml): UC1/UC2
- [`examples/rugby-world-cup.yaml`](examples/rugby-world-cup.yaml): UC3
- [`examples/rugby-sevens.yaml`](examples/rugby-sevens.yaml): UC4
