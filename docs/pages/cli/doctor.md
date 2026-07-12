---
title: CLI - Doctor Command
description: Diagnose Melodee connections, paths, tools, secrets, and DecentDB compatibility with mcli doctor.
permalink: /cli/doctor/
tags:
  - cli
  - diagnostics
  - troubleshooting
---

# Doctor Command

```bash
mcli doctor [--raw] [--verbose] [--write-test]
```

| Option | Purpose |
|--------|---------|
| `--raw` | Emit JSON suitable for inspection or automation |
| `--verbose` | Include detailed diagnostic and timing output |
| `--write-test` | Create and delete a temporary file in library directories |

Doctor checks startup configuration, PostgreSQL and DecentDB connections,
expected search schemas, configured library paths, required executables,
security keys, and other installation prerequisites. The write test is
non-destructive but requires the same mounts and effective permissions as the
application.

```bash
mcli doctor
mcli doctor --write-test
mcli doctor --raw > doctor-result.json
```

DecentDB error 8 means a generated database uses an unsupported file format.
Follow [DecentDB Usage & Migration](/decentdb/) rather than deleting PostgreSQL.
