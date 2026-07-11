---
title: CLI - Tag Commands
description: Display embedded metadata tags from a media file with mcli.
permalink: /cli/tags/
tags:
  - cli
  - metadata
  - diagnostics
---

# Tag Commands

`tags show` reads a media file and displays the embedded tags recognized by
Melodee.

```bash
mcli tags /music/example.flac show
mcli tags /music/example.mp3 show --verbose
```

Use `--onlytags` (`-o`) to emit only the tag values as comma-separated output:

```bash
mcli tags /music/example.flac show --onlytags
```

The CLI process must have read access to the path. For MPEG frame diagnostics
instead of metadata, use [`mcli file mpeg`](/cli/file/).
