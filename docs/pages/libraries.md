---
title: Libraries
permalink: /libraries/
---

# Libraries

Libraries are the backbone of how Melodee organizes media through its lifecycle. Understanding each library type helps optimize ingestion speed, metadata quality, and storage efficiency.

## Lifecycle Overview

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   INBOUND   │ ──▶ │   STAGING   │ ──▶ │   STORAGE   │ ──▶ │  DATABASE   │
│  (Drop zone)│     │  (Review)   │     │ (Published) │     │ (Playable)  │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
       │                   │                   │                   │
 LibraryInbound      StagingAuto         LibraryInsert        API Clients
   ProcessJob         MoveJob                Job              can stream
```

### Automated Job Chain

When jobs are triggered by the scheduler (not manually), they automatically chain:

1. **LibraryInboundProcessJob** → scans inbound, creates melodee.json, moves to staging
2. **StagingAutoMoveJob** → moves "Ok" validated albums from staging to storage
3. **LibraryInsertJob** → reads melodee.json from storage, inserts into database

This means: drop files into inbound → within ~20 minutes they can be playable (if fully validated).

### Manual Curation Mode

Manual job triggers do NOT chain, allowing troubleshooting and review:

- Run LibraryInboundProcessJob alone to process new files
- Review albums in staging, edit metadata, mark as "Ok"
- Run "Move Ok" button or StagingAutoMoveJob to move approved albums
- Run LibraryInsertJob to index newly moved albums

### Inbound Library

Drop raw, unprocessed audio files here (rips, downloads, untagged collections). The ingestion job scans this path periodically.

Key actions:

- Format conversion (if necessary)
- Loudness / technical validation (future expansion)
- Tag normalization & regex cleanup
- Duplicate detection heuristics (planned)

### Staging Library

Files that passed basic validation but may need human curation. While in staging they are not visible to end user playback APIs.

You can:

- Manually edit or merge metadata.
- Attach or replace artwork.
- Resolve duplicates or conflicting releases.

Albums with "Ok" status are automatically moved to storage by the StagingAutoMoveJob.
Manual promotion is also available via the "Move Ok" button in the library UI.

### Storage Libraries

Published, canonical library roots. You can have multiple storage libraries (e.g., by genre, decade, physical drive, or performance tier).

Benefits of multiple storage libraries:

- Distribute capacity across NAS mounts / network paths.
- Keep high‑resolution masters separate from compressed collections.
- Apply different backup or snapshot policies.
- **Control user access** with fine-grained permissions per library.

## Library Access Control

Melodee supports fine-grained access control for storage libraries using user groups. This enables multi-tenant deployments, family accounts with parental controls, or specialized collections restricted to specific users.

### Access Control Policy

- **Unrestricted Libraries**: If a library has no access controls configured, it is accessible to all authenticated users.
- **Restricted Libraries**: If a library has one or more user groups assigned, only members of those groups can access it.
- **Authorization Enforcement**: Access checks are enforced across all APIs (OpenSubsonic, Jellyfin, native REST) and UI endpoints.

### User Groups

User groups are collections of users that simplify access management:

- Create groups based on your organizational needs (e.g., "Family", "Kids", "Premium Users", "Admins")
- Assign users to one or more groups
- Grant groups access to specific libraries

### Setting Up Access Control

**Admin UI Workflow:**

1. **Create User Groups**: Navigate to User Management → Groups, create groups like "Adults" or "Children"
2. **Assign Users to Groups**: Edit each user and select their group memberships
3. **Configure Library Access**: 
   - Edit a library (Admin → Libraries)
   - Choose "Access: Everyone" (no restrictions) or "Restricted to groups"
   - If restricted, select which groups can access the library
4. **Changes Take Effect Immediately**: Access permissions are cached and invalidated on changes

**Example Use Cases:**

- **Family Library Segregation**: Create "Kids Music" library accessible only to "Children" group, "Adult Music" accessible to "Adults" group
- **Multi-Tenant Deployments**: Separate libraries per tenant with access restricted to tenant-specific groups
- **Partial Access**: User in both "Kids" and "Adults" groups can access both libraries

### Security Considerations

- **404 on Unauthorized Access**: API endpoints return 404 (not 403) for unauthorized content to prevent enumeration attacks
- **Filtered Browsing**: Search, browse, and listing endpoints automatically filter results to only show accessible content
- **Immediate Revocation**: Removing a user from a group immediately revokes access to associated libraries
- **Playlist Handling**: Playlists automatically hide or mark unavailable tracks from inaccessible libraries

### API Behavior

All API clients (OpenSubsonic, Jellyfin, native REST) respect library access controls:

- **Search Results**: Only return artists/albums/songs from accessible libraries
- **Browse Endpoints**: Filter directory listings and folder views
- **Streaming**: Verify library access before streaming audio
- **Random/Recent/Starred Queries**: Only consider accessible libraries

This ensures that Subsonic clients (DSub, Symfonium, etc.) and Jellyfin clients (Finamp, Feishin) automatically respect access controls without client-side changes.

### Library Indexing

When items enter storage, a metadata index is updated (songs, albums, artists, relationships). This powers fast queries for both OpenSubsonic & native APIs.

### Artwork & Ancillary Assets

Artwork is cached alongside storage libraries for fast retrieval; user avatars and playlist definitions live in their own dedicated volumes.

### Naming & Organization

Default naming rules aim for consistency: Artist/Year - Album/TrackNumber - Title.ext (subject to configuration). Future versions will allow advanced templating.

### Space Management Tips

- Run periodic cleanup jobs (planned) to purge orphaned staging artifacts.
- Consider a quota or monitoring on inbound to avoid runaway disk usage.
- Deduplicate via external tooling before dropping huge batches into inbound when possible.

### Homelab Storage Strategies

**For Limited Storage:**
- Use lossy formats for storage (MP3, OGG) with transcoding for quality preferences
- Implement a "hot" library (frequently played) and "cold" archive strategy
- Consider external storage (NAS) mounted to storage volumes

**For Large Collections:**
- Use multiple storage volumes across different drives
- Implement RAID for redundancy and performance
- Consider separate volumes for different quality formats (lossless vs lossy)

**For SBC Deployments:**
- Use fast USB 3.0+ SSD for database and frequently accessed media
- Consider external spinning drives for large music collections
- Monitor storage temperature and performance

### Backups

Prioritize: storage libraries > database > staging (optional) > inbound (usually ephemeral). Always back up artwork if you’ve invested manual curation time.

### Future Enhancements

- Cross‑library move & rebalancing tool.
- Duplicate cluster detection & resolution UI.
- Smart differential re‑scan (skip unchanged directory trees).

Have feedback or a library management pain point? Open a discussion so we can refine the roadmap.

