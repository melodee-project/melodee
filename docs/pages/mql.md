---
title: Melodee Query Language (MQL)
description: Search songs, albums, artists, and podcast episodes with Melodee's field-aware query language.
permalink: /mql/
tags:
  - search
  - mql
  - api
---

# Melodee Query Language (MQL)

MQL adds field filters, numeric comparisons, ranges, and Boolean logic to the
Search page. Select **Advanced**, choose an entity type, enter a query, and run
the search. Podcast search appears when podcasts are enabled.

```text
artist:"Pink Floyd" AND year:>=1970
genre:Jazz rating:>=4
duration:180-300 AND plays:0
(artist:Beatles OR artist:"Rolling Stones") NOT title:live
```

The selected entity controls which fields are valid. **All** runs the same query
against songs, albums, artists, and enabled podcast episodes; a field that exists
for only one type can be invalid for the others.

## Syntax

| Form | Meaning | Example |
|------|---------|---------|
| `word` | Free-text term | `Beatles` |
| `field:value` | Field filter | `artist:Beatles` |
| `field:"value with spaces"` | Exact normalized string | `artist:"Pink Floyd"` |
| `field:>value` | Comparison | `year:>=2000` |
| `field:min-max` | Inclusive numeric range | `year:1970-1979` |
| `field:contains value` | Explicit substring operation | `title:contains live` |
| `field:startsWith value` | Prefix operation | `title:startsWith The` |
| `field:endsWith value` | Suffix operation | `title:endsWith Mix` |
| `field:wildcard value` | `*`/`?` wildcard operation | `title:wildcard *remix*` |

For registered text fields, an unquoted `field:value` normally uses substring
matching. A quoted field value is exact after Melodee's text normalization.
Standalone quoted phrases are not a supported free-text form; qualify them with
a field.

Whitespace between terms is an implicit `AND`. Keywords are case-insensitive:

- `AND` requires both sides;
- `OR` accepts either side;
- `NOT` negates the next term or group;
- parentheses group expressions.

Precedence is parentheses, `NOT`, `AND`, then `OR`.

```text
artist:Beatles year:>=1965
(genre:Rock OR genre:Metal) AND NOT title:live
NOT (channel:Music OR channel:Sports)
```

## Comparison Operators

| Operator | Meaning | Example |
|----------|---------|---------|
| `:` | Field's default match/equality behavior | `starred:true` |
| `:=` | Numeric/date equality token | `year:=1969` |
| `:!=` | Not equal | `year:!=1969` |
| `:>` | Greater than | `plays:>10` |
| `:>=` | Greater than or equal | `rating:>=4` |
| `:<` | Less than | `duration:<180` |
| `:<=` | Less than or equal | `bpm:<=100` |

Ranges are inclusive. Durations are entered in seconds even though their
database representation uses milliseconds.

## Song Fields

| Field | Type | Notes |
|-------|------|-------|
| `title` | text | Song title |
| `artist` | text | Album artist name |
| `album` | text | Album name |
| `genre`, `mood` | text array | Tag membership |
| `year` | number | Album release year |
| `duration` | number | Seconds |
| `bpm` | number | Beats per minute |
| `rating` | number | Current user's rating |
| `plays` | number | Current user's play count |
| `starred` | boolean | Current user's starred state |
| `starredAt`, `lastPlayedAt` | date | Current-user dates |
| `added` | date | Library creation date |
| `composer` | text | Normalized composer |
| `discNumber` (`disc`) | number | Disc number |
| `trackNumber` (`track`) | number | Track sort order |
| `comment` | text | Song comment |
| `imageCount` (`images`) | number | Image count |

## Album Fields

| Field | Type | Notes |
|-------|------|-------|
| `album` (`name`) | text | Album name |
| `artist` | text | Artist name |
| `year` | number | Release year |
| `originalYear` (`origyear`) | number | Original album year |
| `duration` | number | Total seconds |
| `songCount` (`trackcount`) | number | Song count |
| `genre`, `mood` | text array | Tags |
| `rating`, `plays`, `starred` | user-scoped | Current user's values |
| `starredAt`, `lastPlayedAt` | date | Current-user dates |
| `added` | date | Library creation date |

## Artist Fields

| Field | Type | Notes |
|-------|------|-------|
| `artist` (`name`) | text | Artist name |
| `rating`, `starred`, `starredAt` | user-scoped | Current user's values |
| `plays` | number | Total play count |
| `songCount` | number | Song count |
| `albumCount` | number | Album count |
| `added` | date | Library creation date |

## Podcast Episode Fields

Podcast results are limited to the signed-in user's non-deleted channels.

| Field | Type | Notes |
|-------|------|-------|
| `channel` | text | Channel title |
| `title` | text | Episode title |
| `published` (`date`) | date | Publish date |
| `downloaded` | boolean | Download status |
| `duration` | number | Seconds |

```text
channel:Science AND duration:<1800
title:Interview AND downloaded:true
```

## Current Date and Regex Boundaries

The tokenizer and validation API recognize ISO dates (`2026-01-06`), `today`,
`yesterday`, `last-week`, `last-month`, `last-year`, and relative values such as
`-7d`, `-3w`, and `-12h`. In 2.2.0, execution of registered date fields through
the database compilers has known type-conversion limitations and can return a
compilation error. Do not depend on date filters for production automation in
this release.

The parser also recognizes `field:/pattern/i`, but regex execution is disabled
in the Search service's default compiler options. Use `contains`, `startsWith`,
`endsWith`, or `wildcard` for executable 2.2.0 searches.

## Practical Queries

```text
plays:0
starred:true AND artist:"Pink Floyd"
genre:Jazz AND duration:<300
bpm:>140 AND (genre:Electronic OR genre:Dance)
year:1970-1979 AND genre:Rock
composer:Morricone
disc:2 AND track:>=5
songCount:>12
albumCount:>=10 AND plays:>50
```

If a query fails, check the selected entity, field spelling, quoting, operator,
and balanced parentheses. The UI reports parser/validator or compilation errors
and can suggest nearby field names.

## Smart Playlist Preview

The native smart-playlist endpoints under `/api/v1/playlists/smart` can create,
read, update, and delete stored MQL definitions for `songs`, `albums`, or
`artists`. In 2.2.0, the evaluate endpoint records the evaluation time but
always returns an empty result set. Treat this API as schema preview only; it
does not yet produce a playable playlist. Regular and file-defined playlists
are covered in [Playlists](/playlists/).

## Parse and Suggest API

The MQL HTTP controller parses and suggests queries; it does not return library
search results. It is intentionally omitted from the generated OpenAPI document.

```http
POST /api/v1/query/parse
Content-Type: application/json

{
  "entity": "songs",
  "query": "artist:Beatles AND year:>=1970"
}
```

```http
POST /api/v1/query/suggest
Content-Type: application/json

{
  "entity": "songs",
  "query": "art",
  "cursorPosition": 3
}
```

Valid API entities are `songs`, `albums`, `artists`, and `podcasts`. Parse
requests are limited to 500 characters and 10 requests per minute per detected
client address. See [Native API](/api/) for general HTTP security and deployment
guidance.
