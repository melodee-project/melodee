---
post_title: "Event Scripting Requirements"
author1: "steven"
post_slug: "event-scripting-requirements"
microsoft_alias: "n/a"
featured_image: "n/a"
categories:
  - "internal"
tags:
  - "requirements"
  - "event-scripting"
  - "directory-processing"
  - "jint"
ai_note: "AI-assisted"
summary: "Requirements for configuration-driven, boolean-only scripts that gate event execution (e.g., directory processing) and can trigger skip/delete actions."
post_date: "2026-01-24"
---

## Overview

Melodee needs a configuration-driven scripting capability for selected events.
Scripts are **admin-authored**, stored in the Settings table under keys like
`script.<eventName>`, and evaluated at runtime to return a single boolean:

- `true`: proceed with the event's normal behavior.
- `false`: stop/skip and apply an event-specific configured action (e.g., delete
  the directory and move to the next item).
- If the script is missing/disabled, or script execution fails for any reason,
  the host must behave as if `true` was returned (proceed).

This document focuses on the first targeted event: gating directory processing
in `DirectoryProcessorToStagingService` before a directory is processed.

## Goals

- Provide a consistent mechanism to run scripts for named events.
- Keep scripts **pure and read-only**: scripts return only `true|false` and
  cannot directly modify Melodee state, filesystem, network, or .NET runtime.
- Support **per-library** and **per-path prefix** script overrides.
- Ensure predictable behavior under failures (timeouts, exceptions, invalid
  return types) with a default “proceed” policy.
- Make “delete on deny” safe: deletion must be constrained to expected roots,
  audited, and easy to disable.

## Non-goals

- Allowing scripts to mutate objects, write to disk, call APIs, or access .NET.
- A full workflow engine (multi-step, stateful, retries, side effects).
- End-user scripting (scripts are admin-only).

## Terminology

- **Event**: a named hook point in the application (e.g.,
  `directoryProcessingStart`).
- **Script**: executable code (JavaScript via Jint) that returns `true|false`.
- **Context (`ctx`)**: a plain data object containing event inputs.
- **OnDeny Action**: host-defined behavior when a script returns `false`.

## Event model

### Supported events (initial)

Scripts are configured via `script.<eventName>` keys.
All events follow the same contract: the host provides a `ctx` object, the script returns `true|false`, and failures default to “proceed” (`true`).

1. `directoryProcessingStart`
   - Runs once per candidate directory, before any processing is performed.
   - If the script returns `false`, the directory is skipped and `directoryProcessingDelete` is evaluated.

2. `directoryProcessingDelete`
   - Runs only after `directoryProcessingStart` returns `false`.
   - If this script returns `true`, the directory is deleted.

3. `userRegistrationStart`
   - Runs before a new user is created (registration flow).

4. `userLoginStart`
   - Runs during the Blazor `/login` flow, before issuing an authenticated session/cookie.

5. `userProfileUpdateStart`
   - Runs before persisting a user profile update.

6. `playlistCreateStart`
   - Runs before creating a playlist.

7. `podcastChannelAddStart`
   - Runs before adding a podcast channel.

8. `shareCreateStart`
   - Runs before creating a share link.

9. `requestCreateStart`
   - Runs before creating a request.

Future events may be added, but must follow the same “boolean-only predicate”
contract.

### Invocation requirements

- The host must provide a `ctx` object and a `scriptConfig` object.
- The script must return a boolean.
- The host must apply the configured behavior based on the boolean result.

## Configuration requirements (Settings table)

### Settings key convention

- Base key: `script.<eventName>` (example: `script.directoryProcessingStart`).
- Value format: JSON document to allow multiple overrides and policy controls.

### Suggested JSON schema (conceptual)

The stored JSON must support:

- Global defaults for the event (body, enablement, guardrails, actions).
- Overrides by `libraryId` and `pathPrefix`.
- A deterministic selection algorithm (most specific wins).

Example shape:

```json
{
  "enabled": true,
  "engine": "jint",
  "timeoutMs": 50,
  "maxStatements": 10000,
  "default": {
    "body": "function check(ctx){ return true; }"
  },
  "overrides": [
    {
      "enabled": true,
      "libraryId": 1,
      "pathPrefix": "Incoming/",
      "onDeny": "delete",
      "body": "function check(ctx){ if(ctx.mediaFilesCount < 3) return false; return true; }"
    }
  ]
}
```

Notes:

- Script execution failures must behave as if the script was not present and the
  host received `true` (proceed). This is the default behavior for all script
  events.
- `onDeny` is an action applied by the host; the script does not perform the
  deletion.

### Override selection rules

For a given event invocation with `(libraryId, relativePath)`:

1. Filter `overrides` to those with `enabled: true`.
2. Match `libraryId` if specified (exact match).
3. Match `pathPrefix` if specified (prefix match on normalized relative path).
4. Choose the most specific match:
   - Prefer entries with a `libraryId` over those without.
   - Prefer the longest `pathPrefix`.
   - If still tied, prefer the first in list order (or an explicit `order`).
5. If no override matches, fall back to `default`.

## Script execution requirements (Jint)

### Engine choice

Initial implementation targets JavaScript executed by Jint because it is
in-process, widely understood, and easy to configure.

### Security requirements

Even though scripts are admin-authored, scripts must be treated as untrusted
code from a defense-in-depth perspective.

Minimum requirements:

- **No CLR/.NET access** from JavaScript.
- Only expose plain data objects (DTOs) to JavaScript. Do not pass
  `DirectoryInfo`, `FileInfo`, or other live .NET objects.
- Enforce execution guardrails:
  - Timeout (`timeoutMs`)
  - Statement limit (`maxStatements`) or equivalent
  - Cancellation support where practical
- The script environment must not expose filesystem/network APIs.

### Host interface contract

Scripts must be evaluated via a single entry point, one of:

- A `check(ctx, scriptConfig)` function, or
- A single expression that evaluates to boolean.

Recommended standard:

```javascript
function check(ctx, scriptConfig) {
  return true;
}
```

Return value requirements:

- `true` means “continue”.
- `false` means “deny”.
- Any non-boolean return is treated as an error and must default to “proceed”.

### Error handling and defaults

For all script events, “proceed” is the default behavior.

The host must treat the script as absent and behave as if it returned `true`
when any of the following occurs:

- The Settings key does not exist.
- Script evaluation is disabled (`enabled: false`) at the event level or for the selected override.
- The script body is empty or missing.
- The script fails to parse/compile.
- The script throws an exception.
- The script times out or exceeds execution guardrails.
- The script returns a non-boolean value.

Operational requirements:

- Log failures with enough detail to diagnose (event name, libraryId, relativePath, selected override id/prefix), without dumping full script bodies by default.
- Failures must not stop directory processing (unless the underlying operation fails independently).

### Determinism and performance

- Scripts must be expected to run in milliseconds and must not scan files
  themselves.
- The host should precompute derived values and provide them in `ctx`.
- The host should cache parsed/compiled scripts until Settings change.

## Directory processing gating requirements

### Directory processing event flow

For each candidate directory:

1. Evaluate `directoryProcessingStart`.
2. If `directoryProcessingStart` returns `true`, proceed with normal processing.
3. If `directoryProcessingStart` returns `false`, skip processing and evaluate `directoryProcessingDelete`.
4. If `directoryProcessingDelete` returns `true`, delete the directory; otherwise leave it in place and continue with the next directory.

### Context contract for `directoryProcessingStart`

The host must provide `ctx` with at least:

| Field | Type | Description |
| --- | --- | --- |
| `libraryId` | number | Library being processed. |
| `relativePath` | string | Directory path relative to the library root. |
| `directoryName` | string | Friendly directory name. |
| `totalFilesCount` | number | Total files in the directory (all extensions). |
| `totalSizeMegabytes` | number | Total size of files in the directory (MB). |
| `mostRecentModifiedUtc` | string | Most recent file modified timestamp in ISO-8601 UTC (e.g., `2026-01-24T12:34:56Z`). |
| `mediaFilesCount` | number | Number of media files considered for staging. |
| `totalDurationMinutes` | number | Sum duration of media files (minutes). |
| `trackNumbers` | number[] | Parsed positive track numbers (unique, sorted or unsorted). |
| `hasTrackNumberGaps` | boolean | Whether track numbers violate the sequential rule. |

The host may include additional fields, but must keep them primitive/JSON-like.

Notes:

- Units must be stable and documented (e.g., MB as base-10 megabytes).
- Timestamps should be UTC and serialized as ISO-8601 strings to avoid timezone ambiguity.

### Context contract for `directoryProcessingDelete`

The host must provide the same `ctx` shape as `directoryProcessingStart` (at minimum), so that deletion decisions can be based on the same aggregates.

Recommended additional fields:

| Field | Type | Description |
| --- | --- | --- |
| `startEventResult` | boolean | The result from `directoryProcessingStart` (always `false` for this event). |

### Track-number gap definition

Given the set of parsed, positive, unique track numbers:

- Sort ascending.
- The sequence is valid only if:
  - The first track number is `1`, and
  - Every subsequent number equals the prior number + 1.

Examples:

- `[1,2,3,4]` => no gaps (`hasTrackNumberGaps = false`)
- `[1,3,4,5]` => gaps (`true`)
- `[2,3,4]` => gaps (`true`, because it does not start at 1)

### Default example script (matches current requirement)

```javascript
function check(ctx) {
  if (ctx.mediaFilesCount < 3) return false;
  if (ctx.totalDurationMinutes < 10) return false;
  if (ctx.hasTrackNumberGaps) return false;
  return true;
}
```

## Actions on deny (`false`)

The host must support an event-specific deny behavior.
For directory processing, the base behavior on deny is:

- Skip processing the directory.
- Optionally delete the directory if `directoryProcessingDelete` returns `true`.

For other events, the minimum deny behavior is:

- Stop the operation and return a generic “not allowed” result to the UI (without leaking sensitive details).

Strongly recommended additional action:

- `quarantine`: move the directory to a quarantine location for later review (directory processing only).

## Blazor event context requirements (initial)

All contexts must be plain JSON-like objects (no live .NET objects) and must avoid secrets.
When user input is included, prefer derived/sanitized fields (lengths, hostnames, booleans) over raw values.

### Context contract for `userRegistrationStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userNameLength` | number | Length of the requested username. |
| `emailDomain` | string | Domain portion of the email. |
| `clientIp` | string | Client IP (best-effort). |
| `userAgent` | string | User-Agent (best-effort). |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

### Context contract for `userLoginStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userId` | number | Authenticated user id (if known at decision time). |
| `isAdmin` | boolean | Whether the authenticated user is an administrator. |
| `clientIp` | string | Client IP (best-effort). |
| `userAgent` | string | User-Agent (best-effort). |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

### Context contract for `userProfileUpdateStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userId` | number | User id being updated. |
| `changedFields` | string[] | Names of changed fields (no values). |
| `clientIp` | string | Client IP (best-effort). |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

### Context contract for `playlistCreateStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userId` | number | User creating the playlist. |
| `nameLength` | number | Playlist name length. |
| `initialSongCount` | number | Number of songs included at creation time (if applicable). |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

### Context contract for `podcastChannelAddStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userId` | number | User adding the channel (if applicable). |
| `urlScheme` | string | Scheme, e.g., `https`. |
| `urlHost` | string | Host portion of the URL. |
| `isHttps` | boolean | Whether scheme is `https`. |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

### Context contract for `shareCreateStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userId` | number | User creating the share. |
| `shareType` | string | Logical share type (album/song/playlist/etc.). |
| `expiresInDays` | number | Expiration in days (if supported). |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

### Context contract for `requestCreateStart`

Minimum recommended fields:

| Field | Type | Description |
| --- | --- | --- |
| `userId` | number | User creating the request. |
| `requestType` | string | Logical request type. |
| `clientIp` | string | Client IP (best-effort). |
| `utcNow` | string | Current UTC timestamp (ISO-8601). |

## Safety requirements for deletion

If `onDeny: delete` is enabled:

- Deletion must be constrained to the library's configured inbound root (or
  another explicit safe root).
- The host must verify the target is within the allowed root after path
  normalization to prevent traversal issues.
- Deletion must be audited with:
  - event name
  - libraryId
  - relative path
  - script selection (default vs override identifier)
  - decision result
  - action taken
  - errors (if any)

Optional safety controls:

- “Dry run” mode: logs the action without deleting.
- “Trash” mode: move to OS trash/quarantine instead of permanent deletion.

## Observability requirements

- Log script decision outcomes at a level appropriate for operations.
- Record execution duration and failures.
- Avoid logging full script bodies by default; log script identifiers and/or
  a hash/version to support auditing without leaking contents.

## Acceptance criteria

- Settings-driven scripting can be enabled/disabled per event.
- For `directoryProcessingStart`, scripts can veto processing and trigger the
  configured `onDeny` action.
- Per-library and per-path overrides are supported and deterministic.
- Script execution guardrails (timeout/statement limit) prevent hangs.
- Script failures behave according to configured `onError` policy.
- Deletion is safe (root constrained, normalized paths) and audited.
- Detailed end-user/admin documentation is created for the Docsy site under **Core Concepts** with the page title **Scripting**.
