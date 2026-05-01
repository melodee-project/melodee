# Melodee API

The Melodee API is a RESTful API designed for Melodee applications, providing a modern, performant alternative to the OpenSubsonic API.

## Design Principles

- **Performance First**: Optimized queries, efficient serialization, and minimal payload sizes
- **Pagination**: All list operations support pagination with consistent metadata
- **Semantic Naming**: Clear, descriptive method names and response objects
- **Lightweight Responses**: Return only necessary data to reduce bandwidth
- **Standardized Pagination**: All paginated responses include a `meta` property with pagination data
- **Structured Errors**: Consistent error responses with codes, messages, and correlation IDs
- **DTOs Only**: Controllers return DTOs, never EF entities (see [../README.md](../README.md))

## API Versioning

All versioned API endpoints follow the pattern: `/api/v{version}/[controller]`

Current version: `v1`

Example: `GET /api/v1/songs` or `GET /api/v2/songs` (when v2 exists)

## Authentication

Most API endpoints require authentication. Melodee supports multiple authentication methods:

### 1. JWT Bearer Tokens (Primary)

**Standard for most endpoints**

Obtain a bearer token by calling:
```
POST /api/v1/auth/authenticate
```

Then include it in the `Authorization` header:
```
Authorization: Bearer {token}
```

### 2. Cookie-Based Authentication

**For web applications with session management**

```
POST /api/v1/auth/cookie/sign-in
```

Session cookies are automatically sent with subsequent requests.

### 3. OAuth (Google)

**For social login integration**

```
POST /api/v1/auth/google
POST /api/v1/auth/cookie/google
```

### 4. Public Endpoints

Some endpoints don't require authentication:

| Endpoint | Purpose |
|----------|---------|
| `GET /api/v1/system/info` | Public system information |
| `GET /api/v1/system/throw` | Error testing endpoint |
| `GET /api/v1/shares/public/{shareUniqueId}` | Public shared playlist access |
| `GET /song/stream/{songApiKey}/{userApiKey}/{authToken}` | Song streaming (HMAC auth, see below) |

### 5. Token Refresh

Refresh expired JWT tokens without re-authentication:

```
POST /api/v1/auth/refresh-token
```

## Song Streaming (Out of Band)

The song streaming endpoint is intentionally **non-versioned** and uses HMAC authentication instead of Bearer tokens.

**Route:** `GET /song/stream/{songApiKey}/{userApiKey}/{authToken}`

### Why This Design?

1. **Client Compatibility**: Many JavaScript/React audio controls don't handle Bearer tokens well for streaming
2. **Enhanced Security**: HMAC token binds to user, song, and client IP for additional security
3. **Independent Versioning**: Separate versioning allows proxy/caching strategies distinct from main API
4. **Performance**: Simplifies streaming pipeline and reduces token validation overhead

## Rate Limiting

All API endpoints are rate-limited to prevent abuse. Rate limits are configured per endpoint:

- **Standard endpoints**: `melodee-api` rate limiter
- **Authentication endpoints**: `melodee-auth` rate limiter

Exceeding rate limits returns:
```json
{
  "code": "TOO_MANY_REQUESTS",
  "message": "Rate limit exceeded",
  "correlationId": "request-trace-id"
}
```

## API Routes

### Core Library Endpoints

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/albums` | Album management | List, get by ID, recently added, songs, star, rate |
| `/api/v1/artists` | Artist management | List, get by ID, recently added, albums, songs, star, rate |
| `/api/v1/songs` | Song management | List, get by ID, recently added, random, stream, lyrics, star, rate |

### Search & Discovery

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/search` | Library search | Full-text search across songs, albums, artists |
| `/api/v1/charts` | Music charts | List charts, get chart details, get chart playlist |
| `/api/v1/recommendations` | Music recommendations | Get personalized recommendations |
| `/api/v1/artist-lookup` | Artist metadata lookup | Lookup artist by name across providers |

### User & Library Management

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/playlists` | User playlists | CRUD playlists, add/remove songs, reorder |
| `/api/v1/playlists/smart` | Smart playlists | CRUD smart playlists with rules |
| `/api/v1/queue` | User playback queue | Get queue, add items, clear, current song |
| `/api/v1/scrobble` | Music scrobbling | Submit scrobbles, link Last.fm |
| `/api/v1/user` | Current user profile | Get profile, update settings |
| `/api/v1/user/stats` | User statistics | Listening stats, plays per day, top genres |
| `/api/v1/users` | User management | Get users, user details |

### Audio Features

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/audio/features/{id}` | Get audio features | BPM, key, danceability, energy, etc. |
| `/api/v1/audio/bpm` | BPM detection | Estimate BPM for songs |
| `/api/v1/playback/settings` | Playback settings | Get/set playback settings |
| `/api/v1/playback-backend` | Backend management | Register playback backend, health check, status |

### Podcasts

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/podcasts/channels` | Podcast channels | List, create, update, delete, refresh channels |
| `/api/v1/podcasts/channels/{id}/episodes` | Channel episodes | List episodes |
| `/api/v1/podcasts/episodes/{id}` | Episode actions | Download, delete, update playback progress (now playing) |
| `/api/v1/podcasts/episodes/{id}/bookmark` | Episode bookmarks | Get, save, delete resume position |
| `/api/v1/podcasts/episodes/{id}/history` | Episode history | Get play history |
| `/api/v1/podcasts/episodes/search` | Episode search | Search episodes across channels |
| `/api/v1/podcasts/opml/export` | OPML export | Export subscriptions to OPML |
| `/api/v1/podcasts/opml/import` | OPML import | Import subscriptions from OPML |
| `/api/v1/podcasts/discover/search` | Podcast discovery | Search podcast directories (iTunes) |
| `/api/v1/podcasts/discover/trending` | Trending podcasts | Get trending podcasts |
| `/api/v1/podcasts/discover/lookup/{itunesId}` | Podcast lookup | Get podcast details by iTunes ID |

### Requests

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/requests` | Music requests | List, create, update, delete requests |
| `/api/v1/requests/{id}` | Request details | Get request details |
| `/api/v1/requests/{id}/comments` | Request comments | List, create comments |
| `/api/v1/requests/{id}/seen` | Mark as seen | Mark request as seen (activity tracking) |
| `/api/v1/requests/activity` | Activity tracking | Check for unread activity, get unread requests |

### Shares

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/shares` | Shared playlists | Create, list, delete shares |
| `/api/v1/shares/public/{shareUniqueId}` | Public access | Access shared playlist (public endpoint) |

### Genres

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/genres` | Genre management | List genres, get genre details |
| `/api/v1/genres/{id}/songs` | Genre songs | List songs in genre |
| `/api/v1/genres/starred` | Starred genres | List starred genres |

### Equalizer Presets

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/equalizer/presets` | EQ presets | List, create, delete presets |
| `/api/v1/equalizer/presets/{id}` | Preset details | Update preset |

### Party Mode

#### Party Sessions

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/party-sessions` | Party sessions | Create, list, join, leave sessions |
| `/api/v1/party-sessions/{id}` | Session details | Get session details, end session |
| `/api/v1/party-sessions/{id}/participants` | Participants | List participants, ban, kick, change role |
| `/api/v1/party-sessions/{id}/playback` | Playback control | Play, pause, stop, seek, next, get state |
| `/api/v1/party-sessions/{id}/queue` | Queue management | Get queue, add items, clear, reorder, evaluate |

#### Party Endpoints

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/party-endpoints` | Endpoint registry | Register, update, delete endpoints, heartbeat |
| `/api/v1/endpoints` | Session endpoints | Get endpoints, get endpoints for session, attach/detach |

#### Party Moderation

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/party-moderation` | Moderation tools | Queue lock control, ban management |

### Analytics

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/analytics/listening` | Listening analytics | Get listening statistics |
| `/api/v1/analytics/top/{period}` | Top content | Get top songs/albums/artists by period |

### System

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/system/info` | System information | Get server info, version, capabilities (public) |
| `/api/v1/system/throw` | Error testing | Test error handling (public) |
| `/api/v1/system/shutdown` | System control | Gracefully shutdown the server |

### Authentication

| Route | Description | Key Operations |
|-------|-------------|----------------|
| `/api/v1/auth/authenticate` | JWT login | Authenticate with email/password |
| `/api/v1/auth/cookie/sign-in` | Cookie login | Sign in with email/password (session cookie) |
| `/api/v1/auth/cookie/sign-out` | Cookie logout | Sign out (clear session) |
| `/api/v1/auth/google` | Google OAuth | Authenticate with Google |
| `/api/v1/auth/cookie/google` | Google cookie login | Sign in with Google (session cookie) |
| `/api/v1/auth/refresh-token` | Refresh JWT | Get new JWT from refresh token |
| `/api/v1/auth/refresh` | Refresh cookie | Refresh session cookie |
| `/api/v1/auth/logout` | JWT logout | Logout (revoke token) |
| `/api/v1/auth/revoke` | Revoke refresh | Revoke refresh tokens |
| `/api/v1/auth/password-reset/request` | Reset request | Request password reset email |
| `/api/v1/auth/password-reset/validate/{token}` | Validate reset | Validate password reset token |
| `/api/v1/auth/password-reset/confirm` | Confirm reset | Set new password |
| `/api/v1/auth/lastfm/auth-url` | Last.fm auth | Get Last.fm authorization URL |
| `/api/v1/auth/lastfm/session` | Last.fm session | Complete Last.fm authentication |
| `/api/v1/auth/lastfm/disconnect` | Last.fm disconnect | Disconnect Last.fm account |
| `/api/v1/auth/me/linked-providers` | Linked providers | Get linked OAuth providers |
| `/api/v1/auth/me/link/google` | Link Google | Link Google account to user |

## Error Responses

All error responses follow a standardized format:

```json
{
  "code": "ERROR_CODE",
  "message": "Human-readable error message",
  "correlationId": "request-trace-id"
}
```

### Standard Error Codes

| Code | HTTP Status | Description |
|-------|-------------|-------------|
| `UNAUTHORIZED` | 401 | Authentication required or invalid |
| `FORBIDDEN` | 403 | Access denied (insufficient permissions) |
| `NOT_FOUND` | 404 | Resource not found |
| `BAD_REQUEST` | 400 | Invalid request data |
| `VALIDATION_ERROR` | 400 | Request validation failed |
| `TOO_MANY_REQUESTS` | 429 | Rate limit exceeded |
| `BLACKLISTED` | 403 | Email/IP is blacklisted |
| `USER_LOCKED` | 403 | User account is locked |
| `INTERNAL_ERROR` | 500 | Server error |

### OAuth & Authentication Error Codes

| Code | HTTP Status | Description |
|-------|-------------|-------------|
| `invalid_google_token` | 400 | Google token is invalid |
| `expired_google_token` | 400 | Google token has expired |
| `google_account_not_linked` | 400 | No Google account linked |
| `google_already_linked` | 400 | Google account already linked |
| `signup_disabled` | 403 | New user registration disabled |
| `forbidden_tenant` | 403 | Tenant/domain not allowed |
| `account_disabled` | 403 | User account is disabled |
| `refresh_token_replayed` | 401 | Refresh token already used (replay attack) |
| `refresh_token_invalid` | 401 | Invalid or expired refresh token |

## Pagination

All list operations support pagination with consistent metadata.

### Query Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `page` | int | 1 | Page number (1-indexed) |
| `pageSize` | int | 50 | Number of items per page |
| `orderBy` | string | varies | Field to sort by |
| `orderDirection` | string | ASC | Sort direction (ASC or DESC) |
| `q` | string | null | Search query (where supported) |

### Paginated Response Format

```json
{
  "meta": {
    "totalCount": 1234,
    "pageSize": 50,
    "currentPage": 1,
    "totalPages": 25
  },
  "data": [...]
}
```

### Pagination Best Practices

- Use the smallest `pageSize` that meets your needs
- Avoid requesting page numbers beyond `totalPages`
- Use `orderBy` consistently to ensure stable ordering across pages
- Check `totalCount` to determine if more data exists

## Response Objects

All responses use DTOs (Data Transfer Objects), never EF entities. See [Controller Architecture Guidelines](../README.md) for details.

### Common DTO Types

- **Song**: Song information with artist/album references
- **Album**: Album information with artist reference
- **Artist**: Artist information
- **Playlist**: Playlist with track list
- **User**: User profile information
- **PaginationMetadata**: Pagination data in list responses
- **ApiError**: Standardized error response

## CORS

The API supports CORS for web applications. Configure allowed origins in server settings.

## SDKs & Clients

Official SDKs are available for:

- **JavaScript/TypeScript**: `@melodee/api-client` (npm)
- **Python**: `melodee-api` (pip)
- **.NET**: `Melodee.Client` (NuGet)

See [SDK Documentation](../../../../../docs/sdks/) for details.

## OpenAPI/Swagger

Interactive API documentation is available at:

```
https://your-server.com/swagger
```

Swagger UI provides:
- Complete API reference
- Request/response schemas
- Try-it-out functionality
- Authentication examples

## Rate Limiting Details

| Endpoint Group | Rate Limiter | Default Limit |
|----------------|---------------|----------------|
| Standard API | `melodee-api` | 1000 requests per 15 minutes |
| Authentication | `melodee-auth` | 10 requests per 15 minutes |
| Song Streaming | `melodee-stream` | 1000 requests per 15 minutes |

Rate limits are configurable per deployment.

## Testing

Use the system test endpoint for error handling verification:

```
GET /api/v1/system/throw
```

This endpoint always returns an error (useful for testing error response handling).

## Support & Documentation

- **Main Documentation**: [Melodee Documentation](https://melodee.org)
- **API Reference**: [OpenAPI Spec](/swagger/v1/swagger.json)
- **Issue Tracker**: [GitHub Issues](https://github.com/your-repo/issues)

---

**Last Updated:** 2026-01-16
**API Version:** v1
**Status:** Active Development
