---
title: Custom Blocks
description: Add sanitized Markdown notices to the Melodee login, registration, password, and dashboard pages.
permalink: /custom-blocks/
tags:
  - customization
  - administration
  - security
---

# Custom Blocks

Custom Blocks let an administrator place small Markdown notices in predefined
locations without rebuilding Melodee. Files live beneath the Templates library,
are rendered to HTML, and pass through a strict HTML sanitizer before display.

They are suitable for welcome text, maintenance notices, support links, and
simple styled banners. Scripts, embedded content, and arbitrary CSS are not
supported.

## Available Keys

Melodee 2.2.0 renders these keys:

| Key | Location |
|-----|----------|
| `login.top` | Above the login form |
| `login.bottom` | Below the login form |
| `register.top` | Above the registration form |
| `register.bottom` | Below the registration form |
| `forgot-password.top` | Above the password-reset request form |
| `forgot-password.bottom` | Below the password-reset request form |
| `reset-password.top` | Above the new-password form |
| `reset-password.bottom` | Below the new-password form |
| `dashboard.top` | At the top of the signed-in dashboard |

Creating a file for any other key has no effect unless that key is added to the
application in a future release. There are no global, dashboard bottom, or
dashboard announcement slots in 2.2.0.

## File Locations

A dotted key maps to directories and a Markdown filename beneath
`custom-blocks` in the Templates library:

```text
login.top              -> custom-blocks/login/top.md
forgot-password.bottom -> custom-blocks/forgot-password/bottom.md
dashboard.top          -> custom-blocks/dashboard/top.md
```

The published Compose configuration mounts the Templates library at
`/app/templates/`, so the first example is normally:

```text
/app/templates/custom-blocks/login/top.md
```

If the Templates library path was changed in administration, use that path
instead. Keys and paths are lowercase and case-sensitive on Linux. Key segments
may contain lowercase letters, digits, and hyphens only.

## Create a Block

Create the required directories in the Templates volume, then add a `.md` file:

```markdown
## Scheduled maintenance

Playback may be unavailable Saturday from **02:00-03:00 UTC**.

[View service updates](https://status.example.com)
```

For the Compose deployment, one way to inspect the effective library is:

```bash
docker compose exec melodee.blazor \
  find /app/templates/custom-blocks -maxdepth 3 -type f -name '*.md' -print
```

Use your normal volume-management workflow to create or edit files. The
application container only needs read access; avoid installing editors or
changing ownership inside a running production container.

## Configuration

Custom Blocks use host configuration in `appsettings.json` or equivalent
environment variables:

```json
{
  "CustomBlocks": {
    "Enabled": true,
    "MaxBytes": 262144,
    "CacheSeconds": 30
  }
}
```

| Option | Default | Effect |
|--------|---------|--------|
| `Enabled` | `true` | Enables loading and rendering |
| `MaxBytes` | `262144` | Maximum size of one source file (256 KiB) |
| `CacheSeconds` | `30` | In-memory lifetime of a successfully loaded block |

Environment variable names use double underscores, such as
`CustomBlocks__Enabled=false`. Restart the application after changing host
configuration. Missing files are not cached, so a newly created block can be
discovered immediately; an existing cached block can remain visible until its
cache entry expires.

## Allowed Content

Markdown formatting is supported. Raw HTML is accepted as input only where the
sanitizer permits it. The allowed element set includes headings, paragraphs,
lists, links, block quotes, `div`, `span`, `pre`, `code`, emphasis, rules, and
line breaks.

Allowed attributes are limited to `class`, `id`, `style`, and `href`. Links may
use `http`, `https`, or `mailto`. A restricted set of visual CSS properties is
allowed, including colors, backgrounds, borders, spacing, text formatting, and
basic dimensions.

The sanitizer removes content such as:

- `script`, `style`, `iframe`, `object`, and `embed` elements;
- event attributes such as `onclick` and `onerror`;
- unsafe URL schemes;
- positioning and stacking properties not on the CSS allowlist.

Do not use a Custom Block for analytics JavaScript, third-party widgets,
external stylesheets, forms, or executable code.

## Troubleshooting

If a block is absent:

1. Confirm `CustomBlocks.Enabled` is `true` in the effective host configuration.
2. Verify the Templates library exists and note its configured path.
3. Check the exact key-to-path mapping, lowercase spelling, and `.md` extension.
4. Confirm the application user can read the file and it is no larger than
   `MaxBytes`.
5. Wait for `CacheSeconds` after replacing an existing block.
6. Search application logs for `CustomBlock` warnings or errors.

An empty, oversized, invalidly named, unreadable, or missing block is treated as
not found and leaves the slot empty. If markup appears incomplete, check whether
the sanitizer removed an unsupported element, attribute, URL, or CSS property.

Back up the `melodee_templates` volume with the rest of the installation. See
[Libraries](/libraries/) and [Backup and Restore](/backup/).
