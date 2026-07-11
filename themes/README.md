# Melodee Custom Themes

This directory contains pre-built custom theme packs ready for administrators to import into their Melodee instance.

> **Note**: Melodee includes built-in **Light** and **Dark** themes that work without any configuration. These custom themes provide additional styling options.

📖 **Full documentation**: [melodee.org/theming](https://melodee.org/theming)

## Prerequisites

Custom themes require a **Theme library** to be configured in your Melodee instance:

1. Go to **Admin > Libraries**
2. Ensure a Theme library exists (the Compose default is `/app/themes/`)

## Available Themes

### Melodee Signature Themes

| Theme | Version | Description | Base |
|-------|---------|-------------|------|
| [melodee.zip](melodee.zip) | 1.0.0 | Official Melodee light theme - white/gray backgrounds with purple/magenta accents | light |
| [melodee-dark.zip](melodee-dark.zip) | 1.0.0 | Official Melodee dark theme - dark backgrounds with purple/magenta accents | dark |

### Community Themes

| Theme | Version | Description | Base |
|-------|---------|-------------|------|
| [forest.zip](forest.zip) | 1.0.0 | Natural green earth tones | light |
| [midnight-galaxy.zip](midnight-galaxy.zip) | 1.0.0 | Deep space purples with starry accents | dark |
| [ocean-breeze.zip](ocean-breeze.zip) | 1.0.0 | Calming ocean blues and teals | light |
| [sunset-vibes.zip](sunset-vibes.zip) | 1.0.0 | Warm orange and coral sunset colors | light |
| [synthwave.zip](synthwave.zip) | 1.0.0 | Retro 80s neon aesthetic with magenta and cyan | dark |

## Installation

1. Download the theme zip file you want.
2. Extract its top-level theme directory into the configured Themes library.
3. Go to **Admin > Themes** and rescan.

The repository archives contain a top-level directory. Melodee 2.2.0's UI
importer instead expects `theme.json` and `theme.css` at the ZIP root, so
repackage the directory contents before using **Import Theme**. See **Admin >
Libraries** for the effective Themes path.

## Versioning

Theme packs follow semantic versioning (`MAJOR.MINOR.PATCH`). The version is specified in the `theme.json` file inside each zip:

```json
{
  "id": "my-theme",
  "name": "My Theme",
  "version": "1.0.0",
  ...
}
```

- **MAJOR**: Breaking changes (e.g., required new CSS variables)
- **MINOR**: New features, backward compatible
- **PATCH**: Bug fixes, color adjustments

## Creating Custom Themes

See the [theming documentation](https://melodee.org/theming) for instructions on creating your own themes.
