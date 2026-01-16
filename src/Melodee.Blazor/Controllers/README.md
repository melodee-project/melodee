# Controllers Architecture Guidelines

## Core Rule

**None of the controllers in the `Melodee.Blazor.Controllers` namespace should *EVER* return any Data models.**

All endpoints must return DTOs (Data Transfer Objects) or ViewModels, never Entity Framework (EF) entities directly.

---

## What Are "Data Models"?

Data models are Entity Framework entities from the `Melodee.Common.Data.Models` namespace. These include:

- `Melodee.Common.Data.Models.Song`
- `Melodee.Common.Data.Models.Artist`
- `Melodee.Common.Data.Models.Album`
- `Melodee.Common.Data.Models.User`
- `Melodee.Common.Data.Models.PodcastChannel`
- `Melodee.Common.Data.Models.PartySession`
- `Melodee.Common.Data.Models.PartyPlaybackState`
- `Melodee.Common.Data.Models.PartySessionEndpoint`
- `Melodee.Common.Data.Models.PartySessionParticipant`
- `Melodee.Common.Data.Models.Request`
- `Melodee.Common.Data.Models.RequestComment`
- And any other EF entities

---

## What Should Controllers Return?

Controllers must return DTOs from these namespaces:

1. **Controller-specific DTOs:** `Melodee.Blazor.Controllers.{Subfolder}.Models`
2. **Common DTOs:** `Melodee.Common.Models` or `Melodee.Common.Models.Collection`
3. **Custom response types:** Defined in the controller's `Models` folder

### Common Response Patterns

```csharp
// ✅ CORRECT - Return DTO
[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(Song), StatusCodes.Status200OK)]
public async Task<IActionResult> SongById(Guid id)
{
    var song = await songService.GetByApiKeyAsync(id);
    return Ok(song.Data.ToSongModel(...)); // Maps EF entity to DTO
}

// ❌ WRONG - Return EF entity
[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(Common.Data.Models.Song), StatusCodes.Status200OK)]
public async Task<IActionResult> SongById(Guid id)
{
    var song = await songService.GetByApiKeyAsync(id);
    return Ok(song.Data); // Returns EF entity directly - VIOLATION!
}
```

---

## Why This Rule Exists

### 1. **Decoupling**
- EF models represent database schema
- DTOs represent API contracts
- These should evolve independently

### 2. **Security**
- EF models may contain sensitive properties (internal IDs, audit fields, relationships)
- DTOs expose only what clients need
- Prevents accidental data leakage

### 3. **Performance**
- EF models can have large navigation properties
- DTOs are lightweight and selective
- Reduces JSON payload size

### 4. **Stability**
- Changes to database schema shouldn't break API clients
- Can rename EF model properties without breaking APIs
- Can version DTOs separately from database

### 5. **Serialization Safety**
- EF models can cause circular reference errors with navigation properties
- DTOs are designed for JSON serialization
- No risk of lazy loading issues

---

## Examples

### ✅ Correct: Using DTOs

```csharp
// File: src/Melodee.Blazor/Controllers/Melodee/AlbumsController.cs
using Melodee.Blazor.Controllers.Melodee.Models;

[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(Models.Album), StatusCodes.Status200OK)]
public async Task<IActionResult> AlbumById(Guid id)
{
    var albumResult = await albumService.GetByApiKeyAsync(id, cancellationToken);
    
    // Map EF entity to DTO
    return Ok(albumResult.Data.ToAlbumDataInfo().ToAlbumModel(baseUrl, user));
}
```

### ✅ Correct: Using Common DTOs

```csharp
// File: src/Melodee.Blazor/Controllers/Melodee/PodcastsController.cs
using Melodee.Common.Models;

[HttpGet("channels")]
[ProducesResponseType(typeof(PagedResult<PodcastChannelDataInfo>), StatusCodes.Status200OK)]
public async Task<IActionResult> ListChannelsAsync(...)
{
    var result = await podcastService.ListChannelsAsync(pagedRequest, user.Id, cancellationToken);
    return Ok(result); // Returns common DTO, not EF entity
}
```

### ✅ Correct: Using Custom DTOs

```csharp
// File: src/Melodee.Blazor/Controllers/Melodee/PartySessionEndpointRegistryController.cs

public record EndpointDto(
    Guid ApiKey,
    string Name,
    string Type,
    bool IsShared,
    string? Room,
    string? LastSeenAt,
    string? CapabilitiesJson,
    bool IsOwner);

[HttpGet]
[ProducesResponseType(typeof(IEnumerable<EndpointDto>), StatusCodes.Status200OK)]
public async Task<IActionResult> GetEndpoints(CancellationToken cancellationToken)
{
    var result = await endpointRegistryService.GetEndpointsForUserAsync(userId, cancellationToken);
    
    // Map EF entities to DTOs
    var dtos = result.Data.Select(x => new EndpointDto(
        x.ApiKey,
        x.Name,
        x.Type.ToString(),
        x.IsShared,
        x.Room,
        x.LastSeenAt?.ToString(...),
        x.CapabilitiesJson,
        x.OwnerUserId == userId));
    
    return Ok(dtos);
}
```

### ❌ Wrong: Returning EF Entity

```csharp
// DON'T DO THIS
using Melodee.Common.Data.Models;

[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(PodcastChannel), StatusCodes.Status200OK)] // EF type!
public async Task<IActionResult> GetChannel(int id)
{
    var channel = await podcastService.GetChannelAsync(id);
    return Ok(channel.Data); // Returns EF entity - VIOLATION!
}
```

### ⚠️  Acceptable: Using EF Models Internally (Private Methods)

```csharp
// Using EF models in private helper methods is OK
// as long as public endpoints return DTOs

private async Task<PagedResult<SongDataInfo>> SearchSongsWithMqlAsync(...)
{
    await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
    
    // Using EF entities for query building is fine
    var rawSongs = await scopedContext.Songs
        .Include(s => s.Album)
        .ThenInclude(a => a.Artist)
        .Where(predicate)
        .ToArrayAsync(cancellationToken);
    
    // But return DTOs, not EF entities
    var songs = rawSongs.Select(s => new SongDataInfo(...)).ToArray();
    
    return new PagedResult<SongDataInfo> { Data = songs };
}
```

---

## Mapping Patterns

### 1. Extension Method Pattern (Preferred)

```csharp
// In EF model extension file (e.g., Melodee.Common/Data/Models/Extensions/SongExtensions.cs)
public static class SongExtensions
{
    public static SongDataInfo ToSongDataInfo(this Song entity, UserSong? userSong = null)
    {
        return new SongDataInfo(
            entity.Id,
            entity.ApiKey,
            entity.IsLocked,
            entity.Title,
            // ... map properties
        );
    }
}

// In controller
return Ok(song.Data.ToSongDataInfo().ToSongModel(...));
```

### 2. Constructor Pattern

```csharp
public record SongDto(
    Guid ApiKey,
    string Title,
    int Duration,
    // ... only needed properties
);

public static SongDto FromEntity(Song entity)
{
    return new SongDto(
        entity.ApiKey,
        entity.Title,
        entity.Duration);
}

// In controller
return Ok(SongDto.FromEntity(song.Data));
```

### 3. LINQ Projection (For Collections)

```csharp
var dtos = await context.Albums
    .AsNoTracking()
    .Where(a => a.ArtistId == artistId)
    .Select(a => new AlbumDto(
        a.ApiKey,
        a.Name,
        a.ReleaseDate))
    .ToListAsync(cancellationToken);

return Ok(dtos);
```

---

## Audit Status

📋 **Latest Audit:** 2026-01-16

**Issues Found:** 4 controllers with 13 endpoints violating this rule

- [ ] `PodcastsController` - Returns `PodcastChannel`, `PodcastEpisodeBookmark`, `UserPodcastEpisodePlayHistory`
- [ ] `PartyPlaybackController` - Returns `PartyPlaybackState`
- [ ] `PartyEndpointsController` - Returns `PartySessionEndpoint`, `PartyPlaybackState`
- [ ] `PlaybackBackendController` - Returns `PartySessionEndpoint`

**See:** `design/reviews/api-model-concerns.md` for full details and remediation plan.

---

## Checklist for New Endpoints

When adding or modifying endpoints:

- [ ] Does the endpoint return a type from `Melodee.Blazor.Controllers.*.Models` or `Melodee.Common.Models`?
- [ ] Does `ProducesResponseType` use a DTO type, not an EF entity?
- [ ] Are all return values mapped from EF entities to DTOs before returning?
- [ ] Are sensitive/internal properties excluded from the DTO?
- [ ] Is the DTO in the appropriate namespace (controller's Models folder or Common.Models)?

---

## Enforcement

### Build-Time Checks

1. **Code Review:** All PRs adding/modifying controllers must verify no EF entities are returned

### Code Patterns to Flag

```bash
# grep patterns that indicate violations (use in pre-commit hooks)
grep -rn "ProducesResponseType(typeof(Melodee\.Common\.Data\.Models" src/Melodee.Blazor/Controllers/
grep -rn "return Ok(result.Data)" src/Melodee.Blazor/Controllers/Melodee/PodcastsController.cs
```

---

## Related Documentation

- [ASP.NET REST API Guidelines](../../../.github/instructions/aspnet-rest-apis.instructions.md)
- [C# Coding Standards](../../../.github/instructions/csharp.instructions.md)
- [Architecture Best Practices](../../../.github/instructions/dotnet-architecture-good-practices.instructions.md)

---

**Last Updated:** 2026-01-16
**Maintained By:** Development Team
