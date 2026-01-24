---
title: Event Scripting
permalink: /scripting/
---

# Event Scripting

Melodee supports **admin-authored JavaScript** scripts that can allow/deny specific operations at well-defined hook points
(called “events”). Scripts are evaluated in-process using the Jint engine and must return a single boolean:

- `true`: allow the operation to proceed.
- `false`: deny the operation (and optionally apply an `onDeny` action such as `skip`, `delete`, or `quarantine`).

If a script is missing/disabled, fails to compile, throws, times out, exceeds statement limits, or returns a non-boolean,
Melodee defaults to **allow** (`true`).

## Where to manage scripts

In the Melodee web UI:

1. Go to **Admin → Scripts**.
2. Select an event (dropdown) and choose **Create**, or edit an existing script row.
3. Use the built-in editor to update script text, enable/disable the config, and run a “Test” using mock JSON.

Scripts are stored in the database `Settings` table under keys like:

- `script.<eventName>` (example: `script.directoryProcessingStart`)

## Script contract

Melodee calls your script as:

```javascript
function check(ctx, scriptConfig) {
  return true;
}
```

You may also provide a single expression; Melodee wraps it into `check(...)` automatically:

```javascript
ctx.userNameLength >= 3 && ctx.emailDomain === "example.com"
```

### Inputs

- `ctx`: event-specific context object (see event reference below).
- `scriptConfig`: metadata about the current evaluation.

`scriptConfig` fields available in scripts:

| Field | Type | Notes |
|------|------|-------|
| `eventName` | string | The current event name |
| `settingKey` | string | The settings key, e.g. `script.userLoginStart` |
| `timeoutMs` | number | The configured timeout in milliseconds |
| `maxStatements` | number | The configured statement limit |
| `onDeny` | string | Host action when result is `false` (`skip`, `delete`, `quarantine`) |
| `isOverride` | boolean | Whether an override (library/path) matched |
| `libraryId` | number\|null | The matched override library ID (directory events only) |
| `pathPrefix` | string\|null | The matched override path prefix (directory events only) |

### Naming and casing

Melodee exposes `ctx` and `scriptConfig` using **camelCase** keys, even if the underlying .NET models are PascalCase.
For example: `ctx.LibraryId` becomes `ctx.libraryId`.

## Configuration model (stored as JSON)

Each `script.<eventName>` setting value is a JSON document. The conceptual schema is:

```json
{
  "enabled": true,
  "engine": "jint",
  "timeoutMs": 50,
  "maxStatements": 10000,
  "default": {
    "enabled": true,
    "onDeny": "skip",
    "body": "function check(ctx, scriptConfig) { return true; }"
  },
  "overrides": [
    {
      "enabled": true,
      "libraryId": 1,
      "pathPrefix": "Incoming/",
      "onDeny": "delete",
      "body": "function check(ctx, scriptConfig) { return ctx.mediaFilesCount >= 3; }"
    }
  ]
}
```

Notes:

- `enabled: false` disables scripting for the event and always allows.
- `default.onDeny` is used when the default script denies.
- `overrides` apply only where `libraryId` and/or `pathPrefix` matches the current directory event.

### Override selection rules

For directory events, Melodee chooses at most one override:

1. Consider only `overrides` with `enabled: true`.
2. `libraryId` must match exactly if the override specifies one.
3. `pathPrefix` must be a prefix match of the normalized relative path if specified.
4. The most specific match wins:
   - Prefer overrides with a `libraryId` over those without.
   - Prefer the longest `pathPrefix`.
   - If still tied, the earliest entry in the list wins.

## Safety and guardrails

Melodee treats scripts as untrusted code (defense in depth):

- Scripts do not receive live .NET objects or direct filesystem/network APIs.
- Execution limits are enforced:
  - Time limit (`timeoutMs`)
  - Statement limit (`maxStatements`)
- Failures default to allow and are logged using the settings key and script hash (not the full script body).

### Directory deletion and dry-run

Directory deletion is constrained to safe roots. You can also enable dry-run mode:

- `script.dryRun.enabled = true` prevents deletion/quarantine from actually modifying the filesystem.

## Event reference

This section lists the supported events and the `ctx` fields available to scripts.

### `directoryProcessingStart`

Runs before processing each candidate directory. If it returns `false`, Melodee applies `onDeny` (`skip`, `delete`, or
`quarantine`).

Context: `DirectoryProcessingContext`

| Field | Type | Notes |
|------|------|-------|
| `libraryId` | number | Library ID |
| `relativePath` | string | Path relative to library root |
| `directoryName` | string | Directory name only |
| `totalFilesCount` | number | Total files in directory |
| `totalSizeMegabytes` | number | Total size (MB) |
| `mostRecentModified` | string | ISO-8601 timestamp |
| `mediaFilesCount` | number | Recognized media files |
| `totalDurationMinutes` | number | Aggregate duration |
| `trackNumbers` | number[] | Extracted track numbers |
| `hasTrackNumberGaps` | boolean | Whether track numbering has gaps |

### `directoryProcessingDelete`

Runs only when `directoryProcessingStart` denies and `onDeny` is `delete`. If it returns `true`, deletion is attempted.

Context: `DirectoryProcessingContext` (same as above).

### `userRegistrationStart`

Runs during user registration.

Context: `UserRegistrationContext`

| Field | Type |
|------|------|
| `userNameLength` | number |
| `emailDomain` | string |
| `clientIp` | string |
| `userAgent` | string |
| `now` | string |

### `userLoginStart`

Runs during login, before issuing an authenticated session.

Context: `UserLoginContext`

| Field | Type |
|------|------|
| `userId` | number\|null |
| `roles` | string[] |
| `clientIp` | string |
| `userAgent` | string |
| `now` | string |

### `userProfileUpdateStart`

Runs before persisting a profile update.

Context: `UserProfileUpdateContext`

| Field | Type |
|------|------|
| `userId` | number |
| `emailDomain` | string |
| `profileChangesCount` | number |
| `clientIp` | string |
| `userAgent` | string |
| `now` | string |

### `playlistCreateStart`

Runs before creating a playlist.

Context: `PlaylistCreateContext`

| Field | Type |
|------|------|
| `userId` | number |
| `nameLength` | number |
| `initialSongCount` | number |
| `now` | string |

### `podcastChannelAddStart`

Runs before adding a podcast channel.

Context: `PodcastChannelAddContext`

| Field | Type |
|------|------|
| `userId` | number |
| `feedUrl` | string |
| `isNewSubscription` | boolean |
| `now` | string |

### `shareCreateStart`

Runs before creating a share.

Context: `ShareCreateContext`

| Field | Type |
|------|------|
| `userId` | number |
| `shareType` | string |
| `itemCount` | number |
| `expirationDays` | number\|null |
| `now` | string |

### `requestCreateStart`

Runs before creating a request.

Context: `RequestCreateContext`

| Field | Type |
|------|------|
| `userId` | number |
| `requestType` | string |
| `isFirstRequestToday` | boolean |
| `dailyRequestCount` | number |
| `now` | string |

## Examples

These examples are written as `check(ctx, scriptConfig)` functions, but you can also use expression-only scripts when
they are simple.

### Example: require minimum media files to process a directory

Event: `directoryProcessingStart`

```javascript
function check(ctx, scriptConfig) {
  // Require at least 3 media files; otherwise deny.
  return ctx.mediaFilesCount >= 3;
}
```

Recommended override config example:

```json
{
  "enabled": true,
  "timeoutMs": 50,
  "maxStatements": 10000,
  "default": {
    "enabled": true,
    "onDeny": "skip",
    "body": "function check(ctx, scriptConfig) { return ctx.mediaFilesCount >= 3; }"
  },
  "overrides": [
    {
      "enabled": true,
      "libraryId": 1,
      "pathPrefix": "Incoming/",
      "onDeny": "delete",
      "body": "function check(ctx, scriptConfig) { return ctx.mediaFilesCount >= 3; }"
    }
  ]
}
```

### Example: add an extra safety check before deletion

Event: `directoryProcessingDelete`

```javascript
function check(ctx, scriptConfig) {
  // Only allow deletion if there are no track number gaps.
  return ctx.hasTrackNumberGaps === false;
}
```

### Example: block registration from unknown email domains

Event: `userRegistrationStart`

```javascript
function check(ctx, scriptConfig) {
  const allowed = ["example.com", "example.org"];
  return allowed.includes((ctx.emailDomain || "").toLowerCase());
}
```

### Example: restrict playlist names

Event: `playlistCreateStart`

```javascript
function check(ctx, scriptConfig) {
  // Require playlist names between 3 and 80 characters.
  return ctx.nameLength >= 3 && ctx.nameLength <= 80;
}
```

### Example: reject insecure podcast feeds

Event: `podcastChannelAddStart`

```javascript
function check(ctx, scriptConfig) {
  // Allow only HTTPS feeds.
  const url = (ctx.feedUrl || "").toLowerCase();
  return url.startsWith("https://");
}
```

### Example: require share expiration

Event: `shareCreateStart`

```javascript
function check(ctx, scriptConfig) {
  // Deny shares that do not expire.
  return ctx.expirationDays != null && ctx.expirationDays > 0;
}
```

