## [Unreleased]

---
title: Changelog
permalink: /changelog/
---

# Changelog

All notable changes to Melodee will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Types of Changes

- **Added** — New features, endpoints, UI pages, or configuration options.
- **Changed** — Modifications to existing functionality or behavior.
- **Deprecated** — Features that will be removed in a future release.
- **Removed** — Features that were removed in this release.
- **Fixed** — Bug fixes and error corrections.
- **Security** — Vulnerability patches and security hardening.

---

## [2.0.1] - 2026-05-01

### Added

- Dashboard now loads progressively with skeleton placeholders; each data section renders independently as its query completes instead of blocking the entire page.
- Inline setting editor on the Onboarding verification step — failed configuration checks (e.g., `system.baseUrl`) now show a text input and Save button so admins can fix settings without navigating away.
- `Disabled` parameter on the `ThemeSelector` component for use in read‑only profile forms.

### Fixed

- Dashboard `OnInitializedAsync` no longer runs twice during prerender/interactive transition, eliminating duplicate database queries on page load.
- Profile page crash caused by missing `Disabled` parameter on `ThemeSelector`.

---

## [2.0.0] - 2026-05-01

> **v2.0.0** marks the current major release line of Melodee, built on .NET 10 with Blazor Server UI, OpenSubsonic-compatible API, and a native Melodee REST API.

### Added

- Blazor Server administrative UI with Radzen component library.
- OpenSubsonic-compatible API for third-party client support.
- Native Melodee REST API with versioned endpoints (`/api/v1/`).
- Party Mode for collaborative queue management.
- Jukebox playback mode for server-side audio playback.
- Podcast discovery, subscription, and playback.
- Event scripting engine for custom automation.
- MQL (Melodee Query Language) for advanced search and filtering.
- Scrobbling support (Last.fm and compatible services).
- User sharing and playlist management.
- Custom theming with Radzen theme support and custom CSS overrides.
- Multi-library support (Inbound, Staging, Storage).
- Background job scheduling with Quartz.NET.
- Doctor diagnostics for server health checks.
- Onboarding wizard for first-time setup.
- Request system for user-submitted metadata corrections.
- Radio station management.
- Chart generation and display.
- User device profiles.
- Multi-language localization (en-US, de-DE, es-ES, fr-FR, it-IT, ja-JP, pt-BR, ru-RU, zh-CN, ar-SA).
- Docker multi-arch images (linux/amd64, linux/arm64) via GitHub Container Registry.
- Scalar OpenAPI documentation UI.
- Rate limiting for API and authentication endpoints.
- JWT and cookie-based authentication.
- CORS policy configuration.
- ETag and response compression support.
- Custom block system for page customization via Markdown/HTML.

### Changed

- Migrated to .NET 10 runtime.
- Migrated from SQLite to [DecentDB](https://github.com/sphildreth/decentdb)
- Centralized configuration via `IMelodeeConfigurationFactory` with environment variable overrides.

### Fixed

- Various stability and performance improvements across scan pipeline and API endpoints.

### Security

- SSRF validation for podcast and external URL fetching.
- Secret redaction in configuration exports and logs.
- CSRF protection via antiforgery tokens.
- HSTS and security headers middleware.
