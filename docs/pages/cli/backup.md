---
title: CLI - Backup Export
description: Export Melodee settings and library definitions with mcli.
permalink: /cli/backup/
tags:
  - cli
  - backup
  - configuration
---

# Backup Export

`backup export` writes database-backed settings and library definitions to
JSON. It does not back up PostgreSQL data or media files.

```bash
mcli backup export [--output PATH] [--stdout] [--redact-secrets] [--raw]
```

Use `--redact-secrets` when the export will be attached to a support request or
stored somewhere less protected than a production backup:

```bash
mcli backup export --output melodee-settings.json --redact-secrets
mcli backup export --stdout --redact-secrets > melodee-settings.json
```

Without `--redact-secrets`, settings whose keys contain `secret`, `token`, or
`password` can be written in clear text. Protect the resulting file accordingly.

This command is useful for configuration auditing and recovery planning, but a
complete recovery set also needs a PostgreSQL dump and copies of each Docker
volume or host-mounted library. See [Backup and Restore](/backup/).
