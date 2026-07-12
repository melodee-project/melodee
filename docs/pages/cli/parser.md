---
title: CLI - Parser Command
description: Parse supported album metadata files with mcli.
permalink: /cli/parser/
tags:
  - cli
  - metadata
  - diagnostics
---

# Parser Command

`parser parse` exercises Melodee's directory metadata parsers for CUE, SFV,
M3U, and NFO files.

```bash
mcli parser /music/album/album.cue parse --verbose
mcli parser /music/album/album.nfo parse
```

The surrounding album directory and referenced media files may be needed to
produce a valid result. `--verbose` includes the parser result and timing data.
This is a diagnostic command; test it against a copy if the configured parser
is allowed to normalize supporting files.
