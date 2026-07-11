---
title: Hardware & Performance
description: Size and tune a Melodee host for library scans, PostgreSQL, transcoding, and concurrent streams.
permalink: /hardware/
tags:
  - hardware
  - performance
  - sizing
  - homelab
---

# Hardware & Performance

Melodee's resource use depends more on active work than raw song count. Initial
imports stress CPU and storage, PostgreSQL benefits from low-latency random I/O,
and real-time transcoding consumes CPU for every concurrent stream.

## Baseline Requirements

The container setup helper checks for at least 2 GB of RAM and 5 GB of free
space before media is added. Treat those values as an installation floor, not a
capacity plan.

The published image supports Linux AMD64 and ARM64. A dual-core system with
2-4 GB of RAM can evaluate Melodee and serve a small, mostly direct-play
library. Increase resources for concurrent users, background imports, or
transcoding.

## Starting Profiles

These are conservative starting points to validate with your own collection:

| Workload | CPU | RAM | Storage guidance |
|----------|-----|-----|------------------|
| Evaluation or small personal library | 2 cores | 2-4 GB | SSD for PostgreSQL; media on local disk |
| Regular homelab use and moderate imports | 4 cores | 8 GB | SSD for database and working directories |
| Large imports or several concurrent users | 6+ modern cores | 16 GB+ | NVMe database; separate media capacity tier |

Library size alone does not establish a hard boundary. Measure scan duration,
query latency, memory pressure, and transcoding CPU before resizing.

## Storage

Prioritize storage in this order:

1. **PostgreSQL**: low latency, durable SSD or NVMe storage.
2. **Inbound and Staging**: enough local working space for simultaneous imports
   and converted output.
3. **Storage library**: capacity and reliable sequential throughput; a NAS is
   acceptable if directory enumeration and metadata operations are responsive.
4. **Backups**: a separate failure domain with verified restore procedures.

RAID improves availability but is not a backup. Preserve PostgreSQL and media
with the coordinated procedure in [Backup & Recovery](/backup/).

When using NFS or SMB:

- Mount the share on the host and bind mount it into the container.
- Keep PostgreSQL on local block storage.
- Use a wired network for predictable streaming and scans.
- Test permissions as the container user before importing media.
- Avoid network mounts that silently reconnect read-only.

## CPU and Transcoding

Direct play is inexpensive. AAC, MP3, and Opus transcoding launches `ffmpeg` and
can become the dominant CPU load. The standard transcoding commands use software
encoding; hardware-accelerated transcoding is not configured automatically.

Estimate capacity by running the formats and bitrates your clients actually
request while monitoring CPU. Leave headroom for the web application,
PostgreSQL, and scheduled jobs.

## Memory and Container Limits

The supplied Compose file limits `melodee.blazor` to one CPU and 2 GB of memory.
For larger workloads, override those limits rather than editing the tracked
Compose file:

```yaml
services:
  melodee.blazor:
    deploy:
      resources:
        limits:
          cpus: "4.0"
          memory: 8g
```

Watch for container restarts or OOM kills during imports. Do not compensate for
chronic memory pressure with heavy swap on flash-based single-board computers;
reduce concurrency or add RAM where possible.

## PostgreSQL Connections

The default Compose connection pool uses a minimum of 10 and maximum of 50
connections. More connections do not automatically improve performance and can
increase memory use in PostgreSQL. Change `DB_MIN_POOL_SIZE` and
`DB_MAX_POOL_SIZE` only after observing pool saturation and database capacity.

## Single-Board Computers

ARM64 boards can run the published image, but sustained imports and transcoding
require adequate cooling and fast storage. Avoid using a low-endurance microSD
card for PostgreSQL. Prefer a USB 3 or NVMe SSD, active cooling, and direct play
for clients.

## Measure the System

```bash
docker stats
docker compose ps
docker compose logs --tail=200 melodee.blazor
df -h
```

On the host, use tools such as `iostat`, `vmstat`, and your platform's thermal
sensors. Useful application-level observations include:

- Time to process an Inbound batch
- Staging revalidation and Storage insert duration
- PostgreSQL query latency during browsing and search
- CPU per active transcode
- Storage latency and free space
- Container restarts, OOM events, and log growth

Change one variable at a time and repeat a representative workload. Performance
advice based only on collection size or synthetic disk throughput is rarely
reliable.
