---
title: Event Scripting
description: Use administrator-authored JavaScript to gate selected web pages and inbound-directory processing.
permalink: /scripting/
tags:
  - administration
  - scripting
  - preview
---

# Event Scripting

> Event scripting is a preview feature in 2.2.0. Read the enforcement boundaries below before using it as an operational control.

Melodee can run administrator-authored JavaScript at selected events. Scripts execute in-process with Jint and return either a boolean or an object containing a boolean `result`.

```javascript
function check(ctx, scriptConfig) {
  return { result: true, message: "Optional message" };
}
```

`true` allows the action and `false` denies it. A missing or disabled script, empty body, syntax/runtime error, timeout, statement-limit failure, or unsupported return value **fails open** and allows the action.

## Manage scripts

Open **Admin → Scripts**. Administrators can create, edit, test, enable, disable, and delete a setting for one of the registered events. Settings use the key:

```text
script.{eventName}
```

For example, `script.directoryProcessingStart`.

The editor's test action evaluates the script against administrator-supplied mock JSON. A passing test confirms the script contract, not that every production entry point enforces that event.

## Script contract

Melodee invokes:

```javascript
check(ctx, scriptConfig)
```

An expression without a `function check` declaration is wrapped automatically:

```javascript
ctx.mediaFilesCount >= 3
```

Supported returns are:

| Return | Effect |
|---|---|
| `true` | Allow |
| `false` | Deny |
| `{ result: false, message: "Reason" }` | Deny and provide a UI message |

Context and configuration properties are exposed with camel-case names.

`scriptConfig` currently contains:

| Property | Meaning |
|---|---|
| `eventName` | Registered event name |
| `settingKey` | Database setting key |
| `timeoutMs` | Jint timeout |
| `maxStatements` | Jint statement limit |
| `onDeny` | Default directory deny action |

Scripts do not receive live .NET objects or direct filesystem/network APIs. The Jint engine uses strict mode, a default 50 ms timeout, and a default 10,000-statement limit.

## Current configuration boundaries

A stored configuration has this shape:

```json
{
  "version": 1,
  "enabled": true,
  "engine": "jint",
  "timeoutMs": 50,
  "maxStatements": 10000,
  "default": {
    "enabled": true,
    "onDeny": "skip",
    "body": "function check(ctx, scriptConfig) { return true; }"
  },
  "overrides": []
}
```

In 2.2.0:

- only the top-level `enabled`, limits, `default.body`, and `default.onDeny` affect execution;
- `engine` is stored, but execution always uses Jint;
- `default.enabled` is stored but is not consulted;
- library/path overrides can be edited and are counted in the UI, but the orchestration service does not select them.

Use one default body per event. Do not rely on overrides until the runtime connects the override selector.

## Enforced events

### `directoryProcessingStart`

This is the server-side enforcement hook. It runs before each stable candidate directory is processed by the Inbound pipeline.

`ctx` contains:

| Property | Type |
|---|---|
| `path` | string |
| `directoryName` | string |
| `totalFilesCount` | number |
| `totalSizeMegabytes` | number |
| `mostRecentModified` | string |
| `mediaFilesCount` | number |
| `totalDurationMinutes` | number |
| `trackNumbers` | number[] |
| `hasTrackNumberGaps` | boolean |

When the result is false:

- `skip` leaves the directory in place and skips processing;
- `quarantine` moves it to `script.quarantine.path`;
- `delete` is deliberately converted to `skip` in the ingestion path.

The quarantine destination must be writable and should not be inside Inbound. `script.dryRun.enabled` protects the separate safe-delete service, but it does not suppress quarantine moves.

```javascript
function check(ctx, scriptConfig) {
  if (ctx.mediaFilesCount < 3) {
    return { result: false, message: "Release has fewer than three media files." };
  }
  return true;
}
```

`directoryProcessingDelete` remains a registered legacy name, but the ingestion pipeline does not evaluate it.

### Web-page gating events

These events run when a Blazor page initializes:

| Event | Current effect |
|---|---|
| `userRegistrationStart` | Disables the registration form |
| `userLoginStart` | Disables the login form |
| `userProfileUpdateStart` | Makes the profile page read-only |
| `playlistCreateStart` | Disables both playlist-import buttons |
| `podcastChannelAddStart` | Disables **Add Channel** |
| `requestCreateStart` | Disables **New Request** |

They are UI gates, not service/API authorization policies. Native and OpenSubsonic requests do not evaluate them.

Several context values are placeholders because evaluation happens before form submission:

| Event | Values available at evaluation |
|---|---|
| registration | `userNameLength=0`, `emailDomain=""`, plus IP, user agent, and time |
| login | `userId=null`, `roles=[]`, plus IP, user agent, and time |
| profile | actual user ID and email domain; `profileChangesCount=0` |
| playlist create | actual user ID; `nameLength=0`, `initialSongCount=0` |
| podcast add | actual user ID; `feedUrl=""`, `isNewSubscription=true` |
| request create | actual user ID; `requestType=""`, `isFirstRequestToday=true`, `dailyRequestCount=0` |

Do not write rules that claim to validate playlist names, podcast feed URLs, submitted registration emails, or daily request totals with those placeholders.

One valid page-level use is a time-based maintenance gate:

```javascript
function check(ctx, scriptConfig) {
  const hour = new Date(ctx.now).getUTCHours();
  return hour < 2 || hour >= 3
    ? true
    : { result: false, message: "Maintenance is in progress from 02:00–03:00 UTC." };
}
```

## Operational guidance

- Keep scripts small and deterministic.
- Test with allow, deny, malformed, timeout, and missing-property cases.
- Review logs by setting key and script hash; Melodee avoids logging the full body on evaluation failures.
- Pair UI gates with real authorization or configuration controls when bypass would matter.
- Back up PostgreSQL before large script-setting changes.
