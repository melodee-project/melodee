<div align="center">
  <img src="graphics/melodee_logo.png" alt="Melodee Logo" height="120px" />

  # Melodee

  **A music system designed to manage and stream music libraries with tens of millions of songs**

  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://github.com/melodee-project/melodee/actions/workflows/dotnet.yml/badge.svg)](https://github.com/melodee-project/melodee/actions/workflows/dotnet.yml)
  [![CodeQL](https://github.com/melodee-project/melodee/actions/workflows/codeql.yml/badge.svg)](https://github.com/melodee-project/melodee/actions/workflows/codeql.yml)
  [![Discord](https://img.shields.io/discord/1337921126210211943)](https://discord.gg/bfMnEUrvbp)

  [🌐 Try Demo](https://demo.melodee.org) • [Features](#features) • [Quick Start](#quick-start) • [Documentation](#documentation) • [Contributing](#contributing) • [Support](#support)
</div>

---

## 🎵 Overview

Melodee is a self-hosted music management and streaming system designed for very large libraries.
It ingests and normalizes audio through an inbound → staging → storage pipeline, then serves a curated library via a Blazor web UI plus OpenSubsonic, Jellyfin, and native REST APIs.
Built on .NET 10 with PostgreSQL and first-class container support, it's a great fit for homelabs (from Raspberry Pi-class hardware to full servers).

### Key Capabilities

- **🧙 Onboarding Wizard**: Guided 8-step setup wizard for new installations with system checks
- **📁 Smart Media Processing**: Automatically converts, cleans, and validates inbound media
- **🎛️ Staging Workflow**: Optional manual editing before adding to production libraries
- **⚡ Automatic Ingestion**: Drop files → play music (validated albums flow through automatically)
- **🔄 Automated Jobs**: Cron-based scheduling with intelligent job chaining
- **📝 User Requests**: Submit and track requests for missing albums/songs, with automatic completion when matches are detected
- **🎙️ Podcast Support**: Subscribe to podcasts, auto-download episodes, playback tracking with resume positions
- **🔐 Fine-Grained Library Access Control**: User group-based permissions for multi-library setups with flexible access policies
- **🎵 OpenSubsonic API**: Compatible with popular Subsonic and OpenSubsonic clients
- **🎬 Jellyfin API**: Compatible with Jellyfin music clients (Finamp, Feishin, Streamyfin)
- **🌐 Melodee API**: Fast RESTful API for custom integrations
- **🌐 Modern Web UI**: Blazor Server interface with Radzen UI components
- **🎛️ Jukebox**: Server-side playback with queue/control support (OpenSubsonic jukeboxControl; MPV/MPD backends)
- **🎉 Party Mode**: Shared listening sessions with a collaborative queue and DJ/Listener roles
- **🎧 User Device Profiles**: Per-user and per-device transcoding profiles for automatic codec/bitrate selection
- **📜 Event Scripting Hooks**: JavaScript-based event scripting for customizing user authentication, media processing, and feature access
- **🐳 Pre-built Containers**: Multi-arch Docker images (linux/amd64, linux/arm64) published to GHCR — pull and run with `docker pull ghcr.io/melodee-project/melodee:latest`

## 🌐 Try the Demo

Experience Melodee before installing! Our official demo server is available at:

**🎧 [https://demo.melodee.org](https://demo.melodee.org)**

### Quick Start

- **Login**: Username `demo` / Password `Mel0deeR0cks!`
- **Or Register**: Create a free non-admin account (no email verification required)
- **Reset Cycle**: All user data is purged every 24 hours at midnight UTC

### What You Can Test

- ✅ **Browse & Stream**: Pre-loaded sample music (permissively licensed)
- ✅ **Create Playlists**: Build and share custom playlists
- ✅ **Search**: Test full-text search across artists, albums, and songs
- ✅ **Multiple Clients**: Compatible with Subsonic, OpenSubsonic, and Jellyfin clients
- ✅ **API Explorer**: Interactive API documentation at `/scalar/v1`
- ✅ **User Requests**: Submit requests for missing albums or songs

### Demo Limitations

- ❌ **No File Uploads**: Upload functionality is disabled for security
- ❌ **No Admin Access**: Admin features are not available to demo users
- ⚠️ **Limited Concurrent Users**: Maximum 100 simultaneous connections
- 🔄 **24-Hour Reset**: All user accounts and data are deleted daily

> **Note**: The demo server is for testing only. For production use, please [install Melodee](https://melodee.org/installing/) on your own infrastructure.

## 🎶 Music Ingestion Pipeline

Melodee features a fully automated music ingestion pipeline. Simply drop your music files into the inbound folder and they'll be processed, validated, and made available for streaming—typically within 15-20 minutes.

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│     INBOUND     │     │     STAGING     │     │     STORAGE     │     │    DATABASE     │
│   (Drop zone)   │────▶│    (Review)     │────▶│   (Published)   │────▶│   (Playable)    │
│                 │     │                 │     │                 │     │                 │
│  Drop files     │     │  Validated &    │     │  Final home     │     │  Indexed &      │
│  here           │     │  ready albums   │     │  for music      │     │  streamable     │
└─────────────────┘     └─────────────────┘     └─────────────────┘     └─────────────────┘
        │                       │                       │                       │
        ▼                       ▼                       ▼                       ▼
  LibraryInbound          StagingAuto             LibraryInsert            API Clients
    ProcessJob             MoveJob                   Job                   can stream
   (every 10 min)        (every 15 min)          (chains auto)
```

### How It Works

| Step | What Happens | Automatic? |
|------|--------------|------------|
| **1. Drop** | Place music files (MP3, FLAC, etc.) in the inbound folder | You do this |
| **2. Process** | Files are scanned, metadata extracted, validated, and moved to staging | ✅ Automatic |
| **3. Review** | Albums marked "Ok" are automatically promoted; others await manual review | ✅ Automatic for valid albums |
| **4. Move** | Validated albums move from staging to storage library | ✅ Automatic |
| **5. Index** | Albums in storage are indexed into the database | ✅ Automatic |
| **6. Stream** | Music is available via OpenSubsonic API and web player | Ready to play! |

### Automatic vs Manual Mode

**Automatic Mode (Default)**: Jobs chain together—when one completes successfully, it triggers the next. Well-tagged music flows from inbound to playable without intervention.

**Manual Mode**: Trigger jobs individually from the admin UI for troubleshooting or when you want to review albums before promotion. Manual triggers don't chain, giving you full control.

### When Manual Review is Needed

Albums that don't pass validation (missing tags, artwork issues, etc.) stay in staging for manual review. You can:
- Edit metadata and artwork in the web UI
- Mark albums as "Ok" when ready
- Use the "Move Ok" button to promote them
- Delete albums that shouldn't be imported

## 🚀 Quick Start

### 🌐 Try the Demo First!

Before installing, test drive Melodee on our demo server:

👉 **[https://demo.melodee.org](https://demo.melodee.org)** (Username: `demo`, Password: `Mel0deeR0cks!`)

### Automated Setup (Recommended)

```bash
git clone https://github.com/melodee-project/melodee.git
cd melodee
python3 scripts/run-container-setup.py --start
```

The script handles preflight checks, runtime detection, secure configuration, building, and startup.

### Pre-built Container Images

Melodee publishes **pre-built, multi-arch images** (linux/amd64, linux/arm64) to GitHub Container Registry on every release. No build step required:

```bash
# Pull the latest pre-built image
docker pull ghcr.io/melodee-project/melodee:latest

# Or pin to a specific version
docker pull ghcr.io/melodee-project/melodee:2.0.1
```

To use pre-built images with compose, set the `MELODEE_IMAGE` environment variable before running `compose up`:

```bash
export MELODEE_IMAGE=ghcr.io/melodee-project/melodee:latest
docker compose up -d
```

📖 **Full installation guide**: [melodee.org/installing](https://melodee.org/installing/)

### 📦 Updating Melodee

```bash
cd melodee
git pull
python3 scripts/run-container-setup.py --update
```

Your data (database, music library, playlists) is preserved during updates. Database migrations run automatically.

📖 **Full upgrade guide**: [melodee.org/upgrade](https://melodee.org/upgrade/)

## 🏠 Homelab Deployment

Melodee is designed to run in homelab environments with support for various hardware configurations:

- **Single Board Computers**: Raspberry Pi 4/5, Rock 5B, Odroid N2+
- **Home Servers**: Intel NUC, custom builds, used desktops
- **NAS Integration**: Mount external storage for large music collections
- **Container Orchestration**: Docker Compose, Podman Compose, or Docker Swarm

📖 **Deployment guides**: [melodee.org/installing](https://melodee.org/installing/) | [melodee.org/upgrade](https://melodee.org/upgrade/)

### 🗂️ Volume Management

Melodee uses several persistent volumes for data storage:

| Volume | Purpose | Description |
|--------|---------|-------------|
| `melodee_data_protection_keys` | Security | ASP.NET Core data protection keys |
| `melodee_db_data` | Database | PostgreSQL data |
| `melodee_inbound` | Incoming Media | New media files to be processed |
| `melodee_logs` | Logs | Application log files |
| `melodee_playlists` | Playlists | Admin-defined (JSON-based) dynamic playlists |
| `melodee_podcasts` | Podcasts | Downloaded podcast episodes |
| `melodee_staging` | Staging Area | Media ready for manual review |
| `melodee_storage` | Music Library | Processed and organized music files |
| `melodee_templates` | Templates | Email and notification templates |
| `melodee_themes` | Themes | Custom theme packs |
| `melodee_user_images` | User Content | User-uploaded avatars |

To backup your data:
```bash
# Backup volumes
podman volume export melodee_storage > melodee_storage_backup.tar
podman volume export melodee_db_data > melodee_db_backup.tar

# Restore volumes
podman volume import melodee_storage melodee_storage_backup.tar
podman volume import melodee_db_data melodee_db_backup.tar
```

## ✨ Features

### 🧙 Onboarding Wizard

New installations are guided through an 8-step setup wizard that ensures your server is properly configured:

| Step | Description |
|------|-------------|
| **1. Welcome** | Introduction and overview of the setup process |
| **2. Paths** | Configure library paths (inbound, staging, storage, etc.) |
| **3. Database** | Verify database connectivity and run migrations |
| **4. Search Engines** | Set up MusicBrainz and artist search databases |
| **5. Settings** | Configure essential server settings |
| **6. Admin Account** | Create or verify the administrator account |
| **7. Pre-flight Checks** | Run system diagnostics and verify configuration |
| **8. Verify & Complete** | Final verification and setup completion |

The wizard automatically detects existing configurations and can be re-run from `/onboarding` if needed. Once setup is complete, the `system.onboardingCompletedAt` setting is updated and the wizard is bypassed for subsequent logins.

### 🎛️ Media Processing Pipeline

1. **Inbound Processing**
   - Converts media to standard formats
   - Applies regex-based metadata cleanup rules
   - Validates file integrity and metadata completeness
   - Extracts and validates album artwork

2. **Staging Management**
   - Automatic promotion of validated ("Ok") albums
   - Manual editing of metadata for albums needing review
   - Album art management and replacement
   - Quality control workflow with status tracking

3. **Production Libraries**
   - Automated scanning and indexing
   - Multiple storage library support
   - Real-time database updates
   - Artist, album, and song relationship management

### 🔄 Background Jobs

Melodee includes a comprehensive job scheduling system powered by Quartz.NET:

| Job | Purpose | Default Schedule |
|-----|---------|------------------|
| **ArtistHousekeepingJob** | Cleans up artist data and relationships | Daily at midnight |
| **ArtistSearchEngineHousekeepingJob** | Updates artist search index | Daily at midnight |
| **ChartUpdateJob** | Links chart entries to albums | Daily at 2 AM |
| **LibraryInboundProcessJob** | Scans inbound, processes files to staging | Every 10 minutes |
| **LibraryInsertJob** | Indexes storage albums into database | Daily at midnight (+ chained) |
| **MusicBrainzUpdateDatabaseJob** | Updates local MusicBrainz cache | Monthly |
| **NowPlayingCleanupJob** | Cleans stale now-playing entries | Every 5 minutes |
| **PodcastCleanupJob** | Enforces retention policies on downloaded episodes | Daily at 3 AM |
| **PodcastDownloadJob** | Downloads queued podcast episodes | Every 5 minutes |
| **PodcastRecoveryJob** | Resets stuck downloads and cleans orphaned files | Every hour |
| **PodcastRefreshJob** | Fetches new episodes from subscribed podcasts | Every 30 minutes |
| **StagingAutoMoveJob** | Moves "Ok" albums from staging to storage | Every 15 minutes (+ chained) |

Jobs can be manually triggered, paused, or monitored from the admin UI at `/admin/jobs`.

### 🔍 Melodee Query Language (MQL)

MQL is a powerful query language for advanced music library searches. Access it via the **Advanced** button in the Search page.

**Syntax highlights:**
- **Field-specific search**: `artist:"Pink Floyd" album:"The Wall"`
- **Numeric comparisons**: `year:>=2000 rating:>3 duration:<300`
- **Date ranges**: `added:last-week lastPlayedAt:-30d`
- **Boolean logic**: `(rock OR metal) AND NOT live`
- **Regex patterns**: `title:/.*remix.*/i`

**Example queries:**
```
# Find highly-rated jazz you haven't heard recently
genre:Jazz rating:>=4 lastPlayedAt:<-90d

# Pink Floyd albums from the 70s
artist:"Pink Floyd" year:1970-1979

# Recently added music you haven't played yet
added:-7d plays:0
```

See the full [MQL Documentation](https://melodee.org/mql/) for complete field reference and examples.

### 🔌 Plugin Architecture

- **Media Format Support**: AAC, AC3, M4A, FLAC, OGG, APE, MP3, WAV, WMA, and more
- **Metadata Sources**: iTunes, Last.FM, MusicBrainz, Spotify, Brave Search
- **File Parsers**: NFO, M3U, SFV, CUE sheet metadata files

### 🌍 Multi-Language Support

Melodee features comprehensive localization with support for 10 languages:

| Language | Code | Status | RTL |
|----------|------|--------|-----|
| English (US) | en-US | ✅ 100% | - |
| Arabic | ar-SA | 🔄 94% | ✅ |
| Chinese (Simplified) | zh-CN | 🔄 94% | - |
| French | fr-FR | 🔄 94% | - |
| German | de-DE | 🔄 94% | - |
| Italian | it-IT | 🔄 34% | - |
| Japanese | ja-JP | 🔄 94% | - |
| Portuguese (Brazil) | pt-BR | 🔄 94% | - |
| Russian | ru-RU | 🔄 94% | - |
| Spanish | es-ES | 🔄 94% | - |

- **Language Selector**: Available in the header for quick switching
- **User Preference**: Language choice is saved to your user profile and persists across sessions
- **RTL Support**: Full right-to-left layout support for Arabic

#### 🌐 Help Translate Melodee!

We welcome translation contributions from the community! Strings marked with `[NEEDS TRANSLATION]` in the resource files need native speaker review.

**How to contribute translations:**

1. Find your language file in `src/Melodee.Blazor/Resources/SharedResources.<code>.resx`
2. Search for `[NEEDS TRANSLATION]` entries
3. Replace the placeholder with your native translation
4. Submit a pull request

Resource files are standard .NET `.resx` XML format. You can edit them with any text editor or Visual Studio's resource editor.

**Current translation needs:** Italian needs the most work (~1,321 strings). Other languages need ~105 strings each (out of 2,025 total). See [Contributing Guide](CONTRIBUTING.md) for details.

### 🌐 OpenSubsonic API

Full compatibility with Subsonic 1.16.1 and OpenSubsonic specifications:

- Real-time transcoding (including OGG and Opus)
- Playlist management
- User authentication and permissions
- Album art and metadata serving
- Scrobbling support (Last.fm)

#### OpenSubsonic Compatibility Matrix

See the [OpenSubsonic Compatibility Matrix](https://melodee.org/opensubsonic-matrix) for detailed endpoint support, client compatibility, and known limitations.

#### Tested OpenSubsonic Clients

- [MeloAmp (desktop)](https://github.com/melodee-project/meloamp)
- [Melodee Player (Android Auto)](https://github.com/melodee-project/melodee-player)
- [Airsonic (refix)](https://github.com/tamland/airsonic-refix)
- [Dsub](https://github.com/DataBiosphere/dsub)
- [Feishin](https://github.com/jeffvli/feishin)
- [Symphonium](https://symfonium.app/)
- [Sublime Music](https://github.com/sublime-music/sublime-music)
- [Supersonic](https://github.com/dweymouth/supersonic)
- [Ultrasonic](https://gitlab.com/ultrasonic/ultrasonic)

### 🎬 Jellyfin API

Melodee provides a Jellyfin-compatible API that allows popular Jellyfin music clients to connect:

- Full media browsing (artists, albums, songs)
- Streaming with transcoding support
- Playlist management
- Favorites and play state tracking
- Session and playback reporting
- Instant mix generation

#### Tested Jellyfin Clients

- [Finamp](https://github.com/jmshrv/finamp) - iOS, Android, Desktop
- [Feishin](https://github.com/jeffvli/feishin) - Desktop (Jellyfin mode)
- [Streamyfin](https://github.com/streamyfin/streamyfin) - iOS, Android
- [Gelli](https://github.com/dkanada/gelli) - Android

### 📝 Custom Blocks

Add custom Markdown content to pages for branding, announcements, or policies:

- **Flexible Placement**: Top and bottom slots on login, register, forgot-password, and reset-password pages
- **Markdown Support**: Write content in Markdown with automatic HTML rendering
- **Security First**: Strict HTML sanitization prevents XSS attacks and injection
- **Auto-Caching**: File-based caching with automatic invalidation on changes
- **Zero Configuration**: Drop `.md` files in `${MELODEE_DATA_DIR}/custom-blocks/{page}/{slot}.md` and they appear instantly

Perfect for terms of service links, support contact information, or custom branding messages.

### 🔐 Authentication

Melodee supports multiple authentication methods:

- **Username/Password**: Traditional authentication with JWT tokens
- **Google Sign-In**: OAuth 2.0 authentication via Google (configurable)

#### API Authentication

All API endpoints (except public endpoints) require a Bearer token:

```bash
curl -H "Authorization: Bearer <JWT_TOKEN>" https://your-server/api/v1/albums
```

#### Token Refresh

Melodee implements secure token rotation:
- **Access tokens**: Short-lived (15 minutes default)
- **Refresh tokens**: Long-lived (30 days default) with automatic rotation

#### Google Sign-In Configuration

To enable Google Sign-In, configure the following in `appsettings.json`:

```json
{
  "Auth": {
    "Google": {
      "Enabled": true,
      "ClientId": "your-google-client-id.apps.googleusercontent.com",
      "AllowedHostedDomains": [],
      "AutoLinkEnabled": false
    },
    "SelfRegistrationEnabled": true
  }
}
```

For API documentation including authentication endpoints, access `/scalar/v1` on a running instance. Download the OpenAPI specification at `/openapi/v1.json`. For OpenSubsonic API, refer to the [OpenSubsonic specification](https://opensubsonic.netlify.app/).

## 🏗️ Architecture

### Components

| Component | Description | Technology |
|-----------|-------------|------------|
| **Melodee.Blazor** | Web UI and OpenSubsonic API server | Blazor Server, Radzen UI |
| **Melodee.Cli** | Command-line interface | .NET Console App |
| **Melodee.Common** | Shared libraries and services | .NET Class Library |

### System Requirements

- **.NET 10.0** or later
- **PostgreSQL 17** (included in container deployment)
- **2GB RAM** minimum (4GB recommended)
- **Storage**: Varies based on music library size

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

### Development Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/melodee-project/melodee.git
   cd melodee
   ```

2. **Install .NET 10 SDK**
   ```bash
   # Follow instructions at https://dotnet.microsoft.com/download
   ```

3. **Run locally**
   ```bash
   dotnet run --project src/Melodee.Blazor
   ```

### Code of Conduct

This project adheres to the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 💬 Support

- **Melodee Music System**: [Home](https://melodee.org)
- **Discord**: [Join our community](https://discord.gg/bfMnEUrvbp)
- **Issues**: [GitHub Issues](https://github.com/melodee-project/melodee/issues)
- **Discussions**: [GitHub Discussions](https://github.com/melodee-project/melodee/discussions)

## 📚 Documentation

For comprehensive documentation, including installation guides, configuration options, homelab deployment strategies, and API references, visit [https://www.melodee.org](https://www.melodee.org).

## 🙏 Acknowledgments

- Built with [.NET 10](https://dotnet.microsoft.com/)
- UI powered by [Radzen Blazor Components](https://blazor.radzen.com/)
- Job scheduling by [Quartz.NET](https://www.quartz-scheduler.net/)
- Compatible with [OpenSubsonic](https://opensubsonic.netlify.app/) specification
- Music metadata from [MusicBrainz](https://musicbrainz.org/), [Last.FM](https://last.fm/), [Spotify](https://spotify.com/), and [Brave Search](https://brave.com/search/api/)

---

<div align="center">
  Made with ❤️ by the Melodee community
</div>
