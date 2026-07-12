---
title: Theming
description: Select built-in themes or create, validate, import, and administer custom Melodee theme packs.
permalink: /theming/
tags:
  - themes
  - customization
  - administration
---

# Theming Melodee

Melodee includes built-in Light and Dark themes and can load custom CSS theme
packs from its Themes library. Administrators manage packs and the system
default under **Administration > Themes**. Users select an available theme in
**Account > Profile**; the preference is stored on the user and applied through
the `melodee_ui_theme` cookie.

If no system/user selection can be resolved, Melodee falls back to Dark.

## Themes Library

A new 2.2.0 database seeds the Themes library at `/app/themes/`. The published
Compose file mounts the `melodee_themes` volume there. If an installation uses a
different library path, the database library record is authoritative.

The two built-in themes do not need this directory. Custom themes are discovered
one directory below it:

```text
/app/themes/
└── my-theme/
    ├── theme.json
    ├── theme.css
    └── optional-assets...
```

The administration page can create a missing Themes library, but its suggested
path may not match a container's mounts. Use `/app/themes/` for the repository's
Compose deployment unless you intentionally added another mount.

## Import and Administration

**Administration > Themes** can rescan, import, export, and delete custom
themes, show validation warnings, and set `system.defaultTheme`. Built-in Light
and Dark cannot be deleted.

For UI import, the ZIP root must directly contain `theme.json` and `theme.css`:

```text
my-theme.zip
├── theme.json
├── theme.css
└── preview.png
```

A ZIP whose only root entry is a `my-theme/` directory does not match the 2.2.0
importer's expected layout. Either repackage that directory's contents at the
ZIP root or extract its top-level theme directory directly beneath the Themes
library and rescan. The ready-made archives in the repository use a top-level
directory and are therefore suited to direct extraction unless repackaged.

Pre-built packs are in the [repository themes
directory](https://github.com/melodee-project/melodee/tree/main/themes). The
published set includes Melodee Light, Melodee Dark, Forest, Midnight Galaxy,
Ocean Breeze, Sunset Vibes, and Synthwave.

The service defaults to a 50 MiB maximum archive size when
`theme.maxUploadSizeMb` is absent or nonpositive. The browser accepts at most
100 MiB, but the smaller service limit still wins. Create the database setting
to choose a lower operational limit.

## `theme.json`

Only `id` and `name` are required by the metadata parser; `theme.css` is also a
required file. A complete example is:

```json
{
  "id": "my-theme",
  "name": "My Theme",
  "description": "A custom dark theme.",
  "author": "Example Admin",
  "version": "1.0.0",
  "baseTheme": "dark",
  "previewImage": "preview.png",
  "branding": {
    "logoImage": "logo.png",
    "favicon": "favicon.ico"
  },
  "fonts": {
    "base": "Inter, sans-serif",
    "heading": "Inter, sans-serif",
    "mono": "monospace"
  },
  "navMenu": {
    "hidden": ["jukebox", "party"]
  }
}
```

`baseTheme` selects Radzen's `dark` base only when its value is exactly `dark`;
all other values use the default/light base. Relative preview, logo, favicon,
font, CSS, and image files are served through the theme API.

Navigation IDs currently rendered by the main menu include `dashboard`,
`stats`, `artists`, `albums`, `charts`, `libraries`, `nowplaying`, `jukebox`,
`party`, `playlists`, `podcasts`, `radiostations`, `requests`, `songs`, `shares`,
`users`, `admin`, `themes`, and `about`.

Hiding navigation is visual customization only. It does not revoke a role,
protect a route, or replace server-side authorization.

## `theme.css`

Custom CSS loads after the chosen Radzen base. A valid imported pack must define
these Melodee tokens:

```css
:root {
  --md-surface-0: #121212;
  --md-surface-1: #1e1e1e;
  --md-surface-2: #2c2c2c;
  --md-text-1: #ffffff;
  --md-text-2: #c7c7c7;
  --md-text-inverse: #000000;
  --md-muted: #8a8a8a;
  --md-border: #3a3a3a;
  --md-divider: #303030;
  --md-primary: #9c6ade;
  --md-primary-contrast: #ffffff;
  --md-accent: #42c7b9;
  --md-accent-contrast: #000000;
  --md-focus: #ffffff;
  --md-success: #43a047;
  --md-warning: #f9a825;
  --md-error: #e53935;
  --md-info: #1e88e5;
}
```

Table, chip, and font-family `--md-*` tokens are optional in 2.2.0. Radzen
`--rz-*` variables and component selectors can be overridden after the required
tokens to customize parts of the UI that do not yet consume Melodee tokens.

## Validation and Contrast

Import rejects a missing/invalid `theme.json`, missing `theme.css`, missing
required tokens, a duplicate theme ID, an oversized archive, or ZIP path
traversal. Theme discovery records warnings for packs placed directly on disk.

Melodee checks a 4.5:1 contrast ratio for:

- `--md-text-1` on each of `--md-surface-0` and `--md-surface-1`;
- `--md-text-inverse` on `--md-primary`.

With `theme.enforceContrastValidation=true`, an insufficient tested ratio makes
an import invalid. Otherwise it is a warning. Contrast parsing understands
hexadecimal and `rgb(r,g,b)` colors; variables, named colors, and more complex
CSS values may not validate as intended.

## Security and Recovery

Import only trusted packs. Custom CSS and publicly served SVG/font/image assets
can alter what users see, make remote requests, obscure controls, and imitate
application UI. Review every file before import. Restrict filesystem write
access to administrators, and do not place credentials or private URLs in CSS
or metadata.

Theme assets are customization, not executable JavaScript: `.js` and arbitrary
file types are not served by the theme file endpoint. A theme cannot grant
permissions by revealing a hidden menu item.

Back up the `melodee_themes` volume. If a theme breaks the UI, remove the
`melodee_ui_theme` cookie or select `dark`, set `system.defaultTheme` to `dark`,
and then remove or correct the custom theme directory. See [Backup and
Restore](/backup/) for volume backup procedures.
