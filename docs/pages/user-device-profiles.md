---
title: User Device Profiles (Preview)
description: Understand the current management-only status of per-user transcoding profiles in Melodee 2.2.0.
permalink: /user-device-profiles/
tags:
  - transcoding
  - preview
  - profiles
---

# User Device Profiles (Preview)

User Device Profiles are a partially implemented transcoding-profile model in
Melodee 2.2.0. Users can create and manage profile records in the web UI, and a
service can resolve a per-player profile or user default. The streaming paths do
not yet consume that resolved profile, so these records do not automatically
change the codec, bitrate, or sample rate delivered to a client.

Do not use this feature as evidence that bandwidth or format limits are being
enforced.

## What Is Available

Open **Account > Profile > Device Profiles**. A signed-in user can create, edit,
set as default, and delete their own profile records.

Each record stores:

| Field | Meaning |
|-------|---------|
| Name | User-facing description |
| Direct Play | Intended to preserve the source stream |
| Target Codec | Intended transcode format |
| Maximum Bitrate | Intended limit in kbps |
| Resample Rate | Intended output sample rate in Hz |
| Priority | Reserved profile-selection value |
| Default | Marks the user's fallback profile |
| Player | Optional database association with a known client/player |

The UI offers MP3, Opus, AAC, FLAC, and WAV. It accepts bitrates from 1 to 9999
kbps, resample rates from 1,000 to 192,000 Hz, and priorities from 0 to 100.
Those UI choices describe stored intent; actual encoder and client support is
not validated by profile creation.

The service enforces these consistency rules:

- a Direct Play profile must not specify a target codec or maximum bitrate;
- a non-Direct-Play profile must specify a target codec;
- selecting a new user default clears the previous default flag.

## Intended Precedence

The implemented resolver returns profiles in this order:

1. the profile assigned to the supplied user and player;
2. the user's default profile;
3. an in-memory Direct Play fallback named `Global Default - Direct Play`.

The 2.2.0 profile dialog does not expose a Player selector, so profiles created
there are user-level records. Although player records are created elsewhere for
client tracking, the UI does not currently bind a profile to one.

## Current Boundaries

The following claims from earlier documentation are not true for 2.2.0:

- OpenSubsonic, Jellyfin, and native streaming do not call
  `GetEffectiveProfileAsync` when selecting their output;
- clients are not automatically mapped to these profiles for transcoding;
- no `/api/v1/user-device-profiles` REST controller is registered;
- no `X-Transcoding-Profile` response header is emitted;
- no fallback based on User-Agent and IP is created by this feature;
- changing `userDeviceProfile.enabled` does not currently hide the profile tab
  or connect/disconnect profile resolution from streaming.

`userDeviceProfile.enabled` is seeded to `true`, but no 2.2.0 consumer reads it.
Existing profile records are preserved if the setting is changed.

## Evaluation and Troubleshooting

Profile records are useful for UI and service-development testing, but an
administrator should continue configuring client-requested transcoding and the
existing streaming settings for production behavior. Confirm delivered content
from response headers and the media stream itself rather than from the profile
row.

If profile creation fails, ensure Direct Play fields are consistent, a
transcoding codec is selected, and the values fit the UI ranges. If a default
does not appear first, reload the Profile page; the service invalidates its
default cache after changes.

Treat the database schema and service as preview implementation detail until a
release explicitly connects them to streaming controllers and publishes API
contracts. See [Configuration Reference](/configuration-reference/),
[OpenSubsonic API](/api-opensubsonic/), and [Jellyfin Compatibility
API](/api-jellyfin/) for currently effective streaming interfaces.
