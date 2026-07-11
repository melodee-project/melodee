---
title: Charts
description: Import curated album rankings, link them to library albums, and expose optional chart playlists.
permalink: /charts/
tags:
  - administration
  - charts
  - playlists
---

# Charts

Charts are ranked album lists such as publication year-end lists or a personal top 100. Melodee stores the source entry even when the album is not in your library, then tries to link each entry to a local album.

## Browse charts

Open **Charts** from the main navigation. Non-administrators see only charts marked **Visible**. The list shows each chart's source, year, item count, link completion, and generated-playlist status.

Two reports are available:

- **Ranked report** combines linked and missing entries across charts.
- **Missing report** shows entries that are not linked to a local album. It can also be opened for one chart.

## Create or import a chart

Chart management is restricted to administrators.

### Create with CSV

Select **Create Chart**, enter the chart metadata, and paste the ranked items. A new chart requires at least one valid CSV row.

Use one row per item, with no header row:

```csv
1,The Beatles,Abbey Road,1969
2,Pink Floyd,The Dark Side of the Moon,1973
3,Nirvana,Nevermind,1991
```

The columns are:

1. positive, unique rank;
2. artist name;
3. album title;
4. optional release year.

Quote fields that contain commas:

```csv
1,"Crosby, Stills, Nash & Young","Déjà Vu",1970
```

Importing CSV into an existing chart replaces all of that chart's items and runs automatic linking. It also replaces any manual item links, so review the preview before saving.

### Import JSON

Select **Import JSON** on the Charts page to upload a file or paste JSON. The importer accepts this shape:

```json
{
  "title": "Best Albums of 2025",
  "sourceName": "Example publication",
  "sourceUrl": "https://example.com/list",
  "year": 2025,
  "description": "Annual editors' list.",
  "tags": ["2025", "editors-picks"],
  "imageUrl": "https://example.com/chart-cover.jpg",
  "isVisible": true,
  "isGeneratedPlaylistEnabled": true,
  "items": [
    {
      "rank": 1,
      "artistName": "Example Artist",
      "albumTitle": "Example Album",
      "releaseYear": 2025
    }
  ]
}
```

`title` and at least one item are required. Every item needs a positive, unique `rank`, `artistName`, and `albumTitle`. The other fields are optional.

Example chart files are maintained in the repository's `design/charts/` directory.

## Linking behavior

Each entry has one of four states:

| State | Meaning |
|---|---|
| **Linked** | One local album was selected. |
| **Ambiguous** | More than one local album matched. |
| **Unlinked** | No local album matched. |
| **Ignored** | An administrator chose not to track this entry. |

Automatic linking first normalizes the artist and album names. It then tries:

1. one exact album-title match for the normalized artist;
2. one partial title match for that artist;
3. otherwise, Ambiguous or Unlinked.

Release year is retained for display but is not part of automatic matching. Administrators can search for an album and resolve an entry manually, mark an entry ignored, or choose **Relink All**. Relinking can preserve or overwrite existing manual links.

The **ChartUpdateJob** retries unlinked chart entries after new albums arrive. Its default schedule is daily at 02:00 and is controlled by `jobs.chartUpdate.cronExpression`.

## Generated playlists

When **Generated Playlist** is enabled, Melodee exposes every song from each linked album. Albums follow chart rank; songs follow disc/track order as represented by the song number. This is generated at request time and is not a user-owned editable playlist.

## Chart images

The editor accepts an optional image and stores a GIF derivative in the library whose type is **Chart**. A Chart library is not seeded by default in 2.2.0. To use chart images:

1. create a writable directory and mount it into the Melodee container;
2. add an administrator-managed library with type **Chart** and that path;
3. upload the chart image from the chart editor.

Chart metadata and items work without an image.

## Native API

The chart API is read-only; use the administrator UI for changes. It requires a native API bearer token and returns visible charts only.

```text
GET /api/v1/charts?page=1&pageSize=20
GET /api/v1/charts/{numericIdOrSlug}
GET /api/v1/charts/{numericIdOrSlug}/playlist
```

The list supports optional `tags`, `year`, and `source` query parameters. The playlist route returns an error unless generated playlists are enabled for that chart. See [Native API](/api/) for authentication and response conventions.
