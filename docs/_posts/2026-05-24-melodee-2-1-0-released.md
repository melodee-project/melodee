---
title: "Melodee 2.1.0 Released"
description: Historical release notes for Melodee 2.1.0 performance, image-processing, migration, and security changes.
date: 2026-05-24
tags:
  - release
  - 2.1.0
badges:
  - type: info
    tag: release
---

Melodee 2.1.0 focused on image processing, page and bulk-operation performance, migration maintenance, and security.

<!--more-->

## Highlights

### SkiaSharp image processing

Image decoding, resizing, encoding, format detection, and hashing moved behind the `IImageProcessor` abstraction and SkiaSharp.

### Faster web and bulk operations

- Dashboard loading moved noncritical work after the initial render.
- Doctor's attention check gained a lightweight fast path.
- Bulk artist, album, and song deletion replaced per-item database lookups with bounded batch queries.
- Party Mode's Blazor service stopped making loopback HTTP calls and invoked domain services directly.

### Developer and migration maintenance

- EF Core history was consolidated into an InitialBaseline migration.
- Quartz scheduling moved into a reusable registration helper.
- CI and CodeQL scope were reduced where the removed language analysis was not used.
- Package updates and analyzer cleanup improved repeatability.

### Security

Production password-reset responses stopped returning reset tokens and use the configured email delivery path.

## Upgrade

Follow the [Upgrade Guide](/upgrade/). Existing PostgreSQL databases at the expected 2.0 migration state treat the consolidated baseline as already satisfied.

## Documentation

- [Installation](/installing/)
- [Upgrade](/upgrade/)
- [Configuration](/configuration/)
- [Event Scripting](/scripting/)

Questions or feedback are welcome on [GitHub](https://github.com/melodee-project/melodee/issues) and [Discord](https://discord.gg/bfMnEUrvbp).
