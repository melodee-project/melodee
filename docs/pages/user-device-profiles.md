---
title: User Device Profiles
description: Configure per-user and per-device transcoding profiles for automatic codec and bitrate selection
tags:
  - configuration
  - transcoding
  - streaming
  - profiles
---

# User Device Profiles

User Device Profiles allow you to configure different transcoding settings for different devices and users. For example, you can automatically serve Opus at 96 kbps to mobile devices while providing lossless Direct Play to desktop clients—all without changing settings each time.

## Overview

The device profile system provides:

- **Per-Player Profiles**: Configure specific transcoding for individual devices (e.g., "My iPhone", "Living Room Desktop")
- **Per-User Defaults**: Set a default transcoding profile for a user across all their devices
- **Global Default**: Fall back to Direct Play (no transcoding) when no specific profile is set
- **Automatic Device Identification**: Devices are automatically identified from OpenSubsonic, Jellyfin, and native API requests

## Profile Precedence

When a stream request is made, Melodee applies profiles in this order (highest to lowest priority):

1. **Per-Player Override** - If a profile exists for the specific player/device
2. **Per-User Default** - If the user has set a default profile
3. **Global Default** - Direct Play (no transcoding)

This means you can set a user default of "MP3 192kbps" but override it for specific devices like "Mobile → Opus 96kbps" or "Desktop → Direct Play".

## Enabling Device Profiles

Device profiles are enabled by default. To disable them system-wide:

```bash
# Via settings or configuration
userDeviceProfile.enabled = false
```

When disabled:
- The Device Profiles UI is hidden in Blazor
- All transcoding decisions fall back to legacy behavior
- Existing profiles are preserved but not used

## Configuration Options

### Direct Play

Direct Play streams the original file without any transcoding—perfect for high-quality local playback or fast networks.

**Profile Settings:**
- **Name**: A descriptive name (e.g., "Desktop Lossless")
- **Direct Play**: `true`
- **Target Codec**: (not used)
- **Max Bitrate**: (not used)
- **Resample Rate**: (not used)

### Transcoding Profiles

Transcoding profiles convert audio to a different format and/or bitrate.

**Supported Codecs:**
- **mp3** - MP3 (MPEG Audio Layer 3)
- **opus** - Opus (modern, efficient codec)
- **aac** - AAC (Advanced Audio Coding)

**Profile Settings:**
- **Name**: A descriptive name (e.g., "Mobile - Opus 96k")
- **Direct Play**: `false`
- **Target Codec**: `mp3`, `opus`, or `aac`
- **Max Bitrate**: Bitrate in kbps (e.g., 96, 128, 192, 320)
- **Resample Rate** (optional): Resample rate in Hz (e.g., 44100, 48000)

## Device Identification

Melodee automatically identifies devices from different API clients:

### OpenSubsonic Clients

Uses the `c` parameter (client name) from the API request:
- **Ultrasonic**: `c=Ultrasonic`
- **Symfonium**: `c=Symfonium`
- **Sublime Music**: `c=Sublime%20Music`

Devices are auto-registered when first seen. Each unique client name becomes a Player that can have a profile assigned.

### Jellyfin Clients

Uses the following headers:
- **X-Emby-Client**: Client application name
- **X-Emby-Device-Id**: Stable device identifier
- **X-Emby-Device-Name**: Human-readable device name

The combination of client and device ID creates a stable player identity.

### Native Melodee API / Web Player

Uses the custom header:
- **X-Melodee-Device-Id**: Custom device identifier

If no device ID is provided, Melodee generates a stable identifier based on User-Agent and IP address.

## Example Usage

### Example 1: Mobile and Desktop Profiles

**Scenario**: You want mobile devices to save bandwidth with Opus 96kbps, but desktop to play losslessly.

**Setup:**
1. Create a user default profile: "User Default - Opus 96kbps"
2. Create a per-player override for your desktop: "Desktop - Direct Play"

**Result:**
- Your phone (no specific profile) gets Opus 96kbps
- Your desktop gets Direct Play
- Any new device defaults to Opus 96kbps

### Example 2: Quality Tiers

**Setup:**
1. User default: "MP3 192kbps"
2. Player "Office Desktop": "Direct Play"
3. Player "Car Stereo": "MP3 128kbps"
4. Player "Mobile": "Opus 96kbps"

**Result:**
- Each device gets the appropriate quality for its use case
- Unknown devices fall back to MP3 192kbps

## API Management

Device profiles can be managed via the Melodee REST API:

### List Profiles for User

```http
GET /api/v1/user-device-profiles?userId=123
```

### Create Profile

```http
POST /api/v1/user-device-profiles
Content-Type: application/json

{
  "userId": 123,
  "playerId": 456,
  "name": "Mobile - Opus 96k",
  "directPlay": false,
  "targetCodec": "opus",
  "maxBitrate": 96,
  "isDefaultProfile": false
}
```

### Update Profile

```http
PUT /api/v1/user-device-profiles/789
Content-Type: application/json

{
  "id": 789,
  "name": "Updated Name",
  "directPlay": true
}
```

### Delete Profile

```http
DELETE /api/v1/user-device-profiles/789
```

## Logging and Troubleshooting

Melodee logs transcoding decisions for each stream request:

```
[UserDeviceProfileService] Using per-player profile [Mobile - Opus 96k] for user [123], player [456]
[UserDeviceProfileService] Using user default profile [MP3 192k] for user [123]
[UserDeviceProfileService] Using global default (direct play) for user [123]
```

To see which profile is being applied, check the logs or look for the `X-Transcoding-Profile` response header (if enabled).

## Best Practices

1. **Start with a sensible user default** - Set a reasonable quality that works for most devices
2. **Override selectively** - Only create per-player profiles when needed
3. **Test with real devices** - Verify transcoding settings work as expected
4. **Monitor bandwidth** - Higher bitrates = more bandwidth usage
5. **Use Opus for mobile** - Opus provides excellent quality at low bitrates
6. **Use Direct Play for local** - No transcoding overhead for same-network playback

## Limitations

- Device identification relies on client cooperation (sending correct parameters)
- Transcoding requires CPU resources; high concurrency may impact performance
- Not all codecs are supported on all platforms
- Fallback behavior when player is unknown is deterministic but may not match expectations

## See Also

- [Configuration Reference](configuration-reference) - All configuration settings
- [OpenSubsonic API](api-opensubsonic) - OpenSubsonic compatibility
- [Jellyfin API](api-jellyfin) - Jellyfin compatibility
