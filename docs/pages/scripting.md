---
title: Event Scripting
permalink: /scripting/
---

# Event Scripting

Melodee supports **admin-authored JavaScript** scripts that can allow/deny specific operations at well-defined hook points
(called "events"). Scripts are evaluated in-process using the Jint engine and must return either:

- A **boolean**: `true` to allow, `false` to deny.
- An **object** with `result` (boolean) and optional `message` (string) properties.

Example return values:

```javascript
// Simple boolean
return true;

// Object with message (message is displayed to users when denied)
return { result: false, message: "Registration is currently disabled for maintenance." };
```

If a script is missing/disabled, fails to compile, throws, times out, exceeds statement limits, or returns a non-boolean/non-object,
Melodee defaults to **allow** (`true`).

## Where to manage scripts

In the Melodee web UI:

1. Go to **Admin → Scripts**.
2. Select an event (dropdown) and choose **Create**, or edit an existing script row.
3. Use the built-in editor to update script text, enable/disable the config, and run a "Test" using mock JSON.

Scripts are stored in the database `Settings` table under keys like:

- `script.<eventName>` (example: `script.directoryProcessingStart`)

## Script contract

Melodee calls your script as:

```javascript
function check(ctx, scriptConfig) {
  // Return boolean or object with result/message
  return true;
}
```

You may also provide a single expression; Melodee wraps it into `check(...)` automatically:

```javascript
ctx.userNameLength >= 3 && ctx.emailDomain === "example.com"
```

### Return values

Scripts can return:

| Return Type | Example | Behavior |
|------------|---------|----------|
| `true` | `return true;` | Allow the operation |
| `false` | `return false;` | Deny the operation |
| Object with `result` | `return { result: false, message: "Not allowed" };` | Deny with message displayed to user |

The `message` property is particularly useful for UI events (login, registration, profile, etc.) where the message
is displayed to the user explaining why the action was denied.

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
| `onDeny` | string | Host action when result is `false` (`skip` or `quarantine` for ingestion events) |
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
      "onDeny": "skip",
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

### Directory actions and dry-run

Inbound ingestion does not physically delete release directories through event scripting. Directory scripts can skip a
candidate directory, and quarantine handlers are still subject to dry-run mode:

- `script.dryRun.enabled = true` prevents quarantine actions from modifying the filesystem.

## Event reference

This section lists the supported events and the `ctx` fields available to scripts.

### `directoryProcessingStart`

Runs before processing each candidate directory. If it returns `false`, Melodee applies `onDeny`. For ingestion safety,
`delete` is treated as `skip`.

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

Legacy event name retained for existing settings compatibility. Inbound ingestion no longer evaluates this event and does
not delete release directories through event scripting.

Context: `DirectoryProcessingContext` (same as above).

### `userRegistrationStart`

Runs when a user views the registration page. If the script returns `false`, registration is disabled and the
`message` property (if provided) is displayed to the user.

Context: `UserRegistrationContext`

| Field | Type |
|------|------|
| `userNameLength` | number |
| `emailDomain` | string |
| `clientIp` | string |
| `userAgent` | string |
| `now` | string |

### `userLoginStart`

Runs when a user views the login page. If the script returns `false`, authentication is disabled and the
`message` property (if provided) is displayed to the user.

Context: `UserLoginContext`

| Field | Type |
|------|------|
| `userId` | number\|null |
| `roles` | string[] |
| `clientIp` | string |
| `userAgent` | string |
| `now` | string |

### `userProfileUpdateStart`

Runs when a user views their profile page. If the script returns `false`, the profile becomes read-only and
the `message` property (if provided) is displayed to the user.

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

Runs when viewing the playlists page. If the script returns `false`, the "Import Playlist" buttons are disabled
and the `message` property (if provided) is shown as a tooltip.

Context: `PlaylistCreateContext`

| Field | Type |
|------|------|
| `userId` | number |
| `nameLength` | number |
| `initialSongCount` | number |
| `now` | string |

### `podcastChannelAddStart`

Runs when viewing the podcasts page. If the script returns `false`, the "Add Podcast Channel" button is disabled
and the `message` property (if provided) is shown as a tooltip.

Context: `PodcastChannelAddContext`

| Field | Type |
|------|------|
| `userId` | number |
| `feedUrl` | string |
| `isNewSubscription` | boolean |
| `now` | string |

### `requestCreateStart`

Runs when viewing the requests page. If the script returns `false`, the "New Request" button is disabled
and the `message` property (if provided) is shown as a tooltip.

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
      "onDeny": "skip",
      "body": "function check(ctx, scriptConfig) { return ctx.mediaFilesCount >= 3; }"
    }
  ]
}
```

### Example: skip directories with track number gaps

Event: `directoryProcessingStart`

```javascript
function check(ctx, scriptConfig) {
  return ctx.hasTrackNumberGaps === false;
}
```

### Example: block registration with custom message

Event: `userRegistrationStart`

```javascript
function check(ctx, scriptConfig) {
  const allowed = ["example.com", "example.org"];
  if (!allowed.includes((ctx.emailDomain || "").toLowerCase())) {
    return {
      result: false,
      message: "Registration is only available for example.com and example.org email addresses."
    };
  }
  return true;
}
```

### Example: disable login during maintenance

Event: `userLoginStart`

```javascript
function check(ctx, scriptConfig) {
  // Maintenance window: deny all logins with a message
  return {
    result: false,
    message: "System is under maintenance. Please try again in 30 minutes."
  };
}
```

### Example: restrict playlist creation

Event: `playlistCreateStart`

```javascript
function check(ctx, scriptConfig) {
  // Require playlist names between 3 and 80 characters.
  if (ctx.nameLength < 3 || ctx.nameLength > 80) {
    return {
      result: false,
      message: "Playlist names must be between 3 and 80 characters."
    };
  }
  return true;
}
```

### Example: reject insecure podcast feeds

Event: `podcastChannelAddStart`

```javascript
function check(ctx, scriptConfig) {
  // Allow only HTTPS feeds.
  const url = (ctx.feedUrl || "").toLowerCase();
  if (!url.startsWith("https://")) {
    return {
      result: false,
      message: "Only HTTPS podcast feeds are allowed for security reasons."
    };
  }
  return true;
}
```

### Example: limit daily requests

Event: `requestCreateStart`

```javascript
function check(ctx, scriptConfig) {
  const maxDailyRequests = 5;
  if (ctx.dailyRequestCount >= maxDailyRequests) {
    return {
      result: false,
      message: "You have reached the maximum of " + maxDailyRequests + " requests per day."
    };
  }
  return true;
}
```
