---
title: CLI - File Commands
description: Inspect MPEG audio files with mcli.
permalink: /cli/file/
tags:
  - cli
  - media
  - diagnostics
---

# File Commands

`file mpeg` loads one file, displays its MPEG information, and reports whether
Melodee considers it a valid MPEG audio file.

```bash
mcli file /music/example.mp3 mpeg
```

The CLI process must be able to read the supplied path. When running inside the
application container, use the container path, such as `/storage/...`, rather
than a host-only path.

For metadata tags rather than MPEG frame information, use
[`mcli tags show`](/cli/tags/).
