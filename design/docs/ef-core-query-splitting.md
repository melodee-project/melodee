# EF Core Query Splitting Strategy

This is internal engineering guidance for Melodee contributors.

## Default

Use the DbContext's configured query-splitting behavior. Split queries help avoid cartesian explosion when a query loads several collection navigations.

For read-only operations, prefer projections and `AsNoTracking()` over materializing a large entity graph.

## Choose per query

Prefer split queries when:

- multiple `Include` and `ThenInclude` paths load collections;
- a joined graph would duplicate substantial row data;
- the operation is read-heavy and does not require one SQL statement.

Consider `AsSingleQuery()` when:

- the join is small and simple;
- a projection returns a bounded flat result;
- consistency between multiple SQL statements matters and the transaction semantics are explicit.

Always inspect the generated SQL and measure with representative data before overriding the configured default.

## Examples

```csharp
var songs = await context.Songs
    .Include(song => song.Album)
    .ThenInclude(album => album.Artist)
    .AsSplitQuery()
    .AsNoTracking()
    .ToArrayAsync(cancellationToken);
```

```csharp
var songs = await context.Songs
    .AsNoTracking()
    .Select(song => new
    {
        song.Id,
        song.Title,
        Artist = song.Album.Artist.Name
    })
    .ToArrayAsync(cancellationToken);
```

