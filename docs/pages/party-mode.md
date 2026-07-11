---
title: Party Mode (Preview)
description: Evaluate Melodee's preview party-session, shared-queue, playback-intent, and endpoint features.
permalink: /party-mode/
tags:
  - party-mode
  - preview
  - playback
---

# Party Mode (Preview)

Party Mode stores collaborative sessions, participants, a revision-controlled
queue, playback intent, and playback endpoints. In Melodee 2.2.0 this feature is
an administrator/developer preview. Its server-side model and APIs are farther
along than the browser workflow, and it should not be presented to users as a
finished synchronized-listening experience.

## Enable the Preview

`partyMode.enabled` is recognized but is not seeded as a database setting in a
new 2.2.0 installation, so it resolves to `false`. Create it explicitly:

```bash
mcli configuration set partyMode.enabled true
```

Refresh or restart Melodee after changing it. The **Party Mode** navigation item
then opens `/party` for signed-in users.

To disable it:

```bash
mcli configuration set partyMode.enabled false
```

Disabling the flag prevents new sessions through the service. Existing database
rows are retained.

## What the 2.2.0 UI Provides

The `/party` page can:

- create a named session;
- list the signed-in user's active sessions;
- list active sessions that do not require a join code;
- join an open session by selecting it or entering its session GUID;
- open a session detail page.

The session page can display the stored participants, endpoint state, playback
state, and queue. It exposes endpoint attach/detach controls, playback-intent
controls, queue refresh/remove/clear/reorder actions, an invite-link copy button,
and leave-session behavior. The service layer enforces queue revisions, session
membership, roles, queue locks, and ended sessions.

Roles in the data model are:

| Role | Server-side policy |
|------|--------------------|
| `Owner` | Owns the session and can administer it |
| `DJ` | Can control playback and manage the queue |
| `Listener` | Cannot control playback; queue changes are denied when the queue is locked |

## Important Preview Limitations

Do not plan an event around the current UI without testing the exact build and
workflow. In 2.2.0:

- there is no anonymous guest flow; participants are Melodee users;
- the UI can create a join-code-protected session, but its join form accepts a
  session GUID and does not submit the separate join code;
- the session page does not provide a song search or add-to-queue workflow;
- clicking a queue item selects it locally but does not start that item;
- queue rows and now-playing metadata show placeholder song/user information;
- the participant moderation menu is not implemented in the page;
- the owner page has no completed end-session action and an owner cannot use the
  normal leave operation;
- a SignalR hub and server notifications exist, but the current session
  components do not subscribe to them; playback state is polled every five
  seconds and other views require explicit reloads;
- the UI does not automatically register the browser as a WebPlayer playback
  endpoint or provide synchronized playback in multiple browsers;
- there is no QR-code invitation, remote-listening synchronization, chat, or
  bulk album/artist/playlist queue UI.

These are current-product boundaries, not configuration problems.

## Playback Endpoints

The server model supports `WebPlayer`, `MpvBackend`, and `MpdBackend` endpoint
types with capabilities, ownership, attachment, heartbeat, and stale-state
tracking. An endpoint must be registered before the selector can attach it to a
session.

The [Jukebox](/jukebox/) playback backend has a native operation to register
itself as an endpoint. Jukebox setup, audio-device access, and media-path access
remain separate requirements. Merely enabling Party Mode does not create an
audio output.

## Native API Surface

Preview controllers are grouped beneath:

| Route family | Purpose |
|--------------|---------|
| `/api/v1/party-sessions` | Create, list, get, join, leave, end, and list participants |
| `/api/v1/party-sessions/{id}/queue` | Get, add, remove, reorder, and clear queue items |
| `/api/v1/party-sessions/{id}/playback` | Read state and submit play, pause, stop, next, seek, and volume intents |
| `/api/v1/party-sessions/{id}` moderation routes | Queue lock, role, kick, ban, unban, and audit operations |
| `/api/v1/party-endpoints` | Endpoint registration, capabilities, heartbeat, state, and detach |
| `/api/v1/endpoints` | List and attach endpoints for a session |
| `/party-hub` | SignalR notification hub |

All native routes require a Melodee JWT. The party API and DTOs are preview
contracts and can change; use the running `/scalar/v1` document for the exact
request shapes in the installed build. Test API authentication and each desired
operation before writing an integration.

Queue and playback mutations use expected revision values for optimistic
concurrency. On a conflict, reload current state and decide whether to retry
against the new revision rather than resubmitting blindly.

## Evaluation Checklist

For a controlled test:

1. Enable Party Mode for a non-production instance.
2. Create two ordinary test users and an open session.
3. Confirm both users can join and observe the same persisted queue.
4. Register and attach a tested Jukebox endpoint if audio output is needed.
5. Exercise role, queue-lock, revision-conflict, endpoint-offline, and session-end
   behavior through the API.
6. Review logs for party service, endpoint, playback, and SignalR errors.
7. Disable the flag again if the browser limitations are unsuitable for users.

When reporting a defect, include the Melodee version, UI or API path, user role,
session/queue/playback revision, endpoint type, sanitized response, and relevant
log correlation. Never publish JWTs, join codes, or private endpoint addresses.
