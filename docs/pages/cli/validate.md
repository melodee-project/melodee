---
title: CLI - Validate Commands
description: Validate Melodee album metadata files and database records with mcli.
permalink: /cli/validate/
tags:
  - cli
  - metadata
  - validation
---

# Validate Commands

`validate` and `validate album` validate `melodee.json` album metadata. Select a
target with one of the supported identifiers:

| Option | Target |
|--------|--------|
| `--file PATH` | A specific `melodee.json` file |
| `--id ID` | A metadata record ID |
| `--apiKey GUID` | One album API key |
| `--artistApiKey GUID` | All albums for an artist API key |
| `--library NAME` | A named library |

```bash
mcli validate album --file /storage/Artist/Album/melodee.json
mcli validate --library Staging --json
mcli validate --apiKey 00000000-0000-0000-0000-000000000000 --verbose
```

Use `--json` (`-j`) for structured results. This command validates album
metadata; [`library validate`](/cli/library/) instead compares PostgreSQL
records with files on disk and optionally removes orphaned records.
