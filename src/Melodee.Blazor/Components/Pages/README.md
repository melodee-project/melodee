# Blazor Pages Structure

This document explains the organization of Blazor pages in Melodee and when to use each section.

## Directory Overview

```
Pages/
├── Admin/          # Administrative tools and configuration
├── Data/           # Database records (processed library data)
├── Media/          # Staging/inbound files (melodee.json processing)
├── Account/        # User account management
└── [root files]    # Dashboard, Jukebox, Party Mode, etc.
```

## /Data - Database Records

**Purpose**: Display and manage records that exist in the database (processed, imported music library).

**When to use**: When working with data that has been fully processed and stored in the database.

**Key characteristics**:
- Data comes from `Melodee.Common.Data.Models` (EF Core entities)
- Uses services like `AlbumService`, `ArtistService`, `SongService`
- Routes follow pattern: `/data/{entity}/{apiKey}`
- Users interact with their actual music library here

**Examples**:
- `/data/album/{ApiKey}` - View album details from database
- `/data/artist/{ApiKey}` - View artist details from database
- `/data/songs` - Browse all songs in library
- `/data/playlists` - Manage user playlists

**Entity types** (from `Melodee.Common.Data.Models`):
- `Album`, `Artist`, `Song` - Core music entities
- `Playlist`, `UserSong`, `UserAlbum` - User-specific data
- `Library`, `Contributor` - Library organization

## /Media - Staging & Inbound Processing

**Purpose**: Process and validate inbound music files before they are imported into the database.

**When to use**: When working with `melodee.json` files and raw music files that are being staged for import.

**Key characteristics**:
- Data comes from `Melodee.Common.Models` (file-based models, NOT database entities)
- Uses services like `AlbumDiscoveryService`, `MediaEditService`
- Routes follow pattern: `/media/{action}/{libraryApiKey}/{itemApiKey}`
- This is where music is validated, tagged, and prepared for library import

**Examples**:
- `/media/library/{LibraryApiKey}` - View staging library contents
- `/media/album/{LibraryApiKey}/{ApiKey}` - Edit staging album (melodee.json)
- `/media/album/{LibraryApiKey}/{ApiKey}/edit` - Edit album metadata before import

**Model types** (from `Melodee.Common.Models`):
- `Album`, `Song` - File-based representations (NOT database entities)
- `FileSystemDirectoryInfo`, `FileSystemFileInfo` - File system references
- `ImageInfo`, `MetaTag` - Metadata from audio files

## Critical Distinction

| Aspect | /Data                              | /Media |
|--------|------------------------------------|--------|
| **Data Source** | DecentDB/PostgreSQL database | File system (`melodee.json` files) |
| **Namespace** | `Melodee.Common.Data.Models`       | `Melodee.Common.Models` |
| **Purpose** | Browse & play music library        | Stage & prepare inbound music |
| **User Action** | Listen, rate, create playlists     | Edit tags, validate, import |
| **Persistence** | Database records                   | JSON files on disk |

## Common Mistakes to Avoid

1. **Don't add library playback features to /Media pages**
   - Jukebox queue, ratings, favorites belong in /Data
   - /Media is for processing, not consumption

2. **Don't confuse the two `Album` types**
   - `Melodee.Common.Data.Models.Album` = database entity (use in /Data)
   - `Melodee.Common.Models.Album` = file-based model (use in /Media)

3. **Don't use database services in /Media pages**
   - Use `AlbumDiscoveryService` not `AlbumService`
   - Use `MediaEditService` for editing melodee.json files

4. **Don't use file-based services in /Data pages**
   - Use `AlbumService.GetByApiKeyAsync()` not file system operations
   - Data is already in the database

## Adding New Features

### To add a feature for library content (user-facing):
→ Add to `/Data` pages

### To add a feature for inbound processing (admin/editor):
→ Add to `/Media` pages

### Examples:
- "Add to Jukebox queue" → `/Data/AlbumDetail.razor` ✓
- "Re-tag audio files" → `/Media/AlbumDetail.razor` ✓
- "Rate this song" → `/Data/AlbumDetail.razor` ✓
- "Validate album metadata" → `/Media/AlbumDetail.razor` ✓
