# GemRecommender

GemRecommender is a plugin for **Path of Exile 2** built for the **ExileCore2** overlay framework.

The plugin recommends which gem to craft, equip, or upgrade next based on:

- Your current build definition
- Equipped gems
- Inventory contents
- Available uncut gems
- Character level

It helps streamline progression by automatically prioritising gem upgrades and preserving valuable uncut gems for the highest-priority upgrades.

---

# Features

- Recommends the next optimal gem to:
  - Craft
  - Equip
  - Upgrade
- Reads builds from JSON files
- Supports:
  - Skill gems
  - Support gems
  - Spirit gems
- Smart uncut gem usage logic
- Character-level gated recommendations
- Gem family/tier progression support
- Inventory item highlighting
- In-game overlay navigation
- Live reload support for builds and CSV databases

---

# How It Works

Every frame, GemRecommender scans:

- Equipped skill/spirit gems
- Inventory gems
- Uncut gems
- Character level

It then processes the active build in **priority order** and produces recommendations in three passes.

---

## Pass 1 — Missing Gems

For gems not yet owned or equipped:

- Prefer equipping an existing inventory copy
- Otherwise recommend crafting from an uncut gem

Selection rules:

- Skill/spirit gems use the **highest available** uncut gem
- Support gems use the **lowest sufficient tier**
  to preserve higher-value uncut gems

---

## Pass 2 — Incomplete Upgrades

Detects equipped gems below the configured `maxGemLevel`.

Example:

- Build target = level 20
- Equipped gem = level 15
- Recommendation = upgrade

---

## Pass 3 — Optional Upgrades

Handles already-completed gems when leftover uncut gems exist.

This allows optional upgrades even after the main build is complete.

---

# Installation

## Requirements

- Path of Exile 2
- ExileCore2

## Setup

1. Clone or download this repository
2. Place the plugin folder inside your ExileCore2 plugins directory
3. Launch ExileCore2
4. Enable `GemRecommender`

---

# Build Configuration

Build files are stored in ExileCore2's per-plugin config folder:

```text
<ExileCore2>\config\GemRecommender\
```

On first run the example builds shipped with the plugin are copied here so you
have working starting points. Drop your own `.json` builds in the same folder.

You can override this location with the `BuildsFolderPath` setting (empty = the
config folder above; a relative path resolves against it; an absolute path is
used as-is).

Select the active build through:

- The in-game overlay
- Plugin settings

---

# Example Build File

```json
{
  "gems": [
    {
      "id": "Lightning Arrow",
      "type": "skill",
      "requiredGemLevel": 1,
      "maxGemLevel": 20,
      "minCharLevel": 0,
      "maxCharLevel": null,
      "displayName": null,
      "family": null,
      "targetSkill": null,
      "prioritizeOverEmptySockets": false
    }
  ]
}
```

---

# Build File Fields

## id *(required)*

Exact in-game gem name.

Example:

```json
"id": "Lightning Arrow"
```

---

## type *(required)*

Supported values:

- `"skill"`
- `"support"`
- `"spirit"`

---

## requiredGemLevel *(optional)*

Minimum uncut gem level required.

If omitted:

- Auto-detected from CSV database

Rules:

- Database minimum always applies
- Higher build requirement overrides database value

---

## maxGemLevel *(optional)*

Only applies to skill/spirit gems.

Once reached:

- Gem is considered complete
- No further upgrade recommendations appear

If omitted:

- No cap

---

## minCharLevel *(optional)*

Minimum player level required before recommendations appear.

Default:

```json
0
```

---

## maxCharLevel *(optional)*

Stops recommendations after exceeding this level.

---

## targetSkill *(required for support gems)*

The skill this support attaches to.

The support only becomes eligible when:

- The target skill exists
- A socket is available
- Or a lower-priority support can be replaced

---

## displayName *(optional)*

Custom label shown in the overlay.

Falls back to `id` if omitted.

---

## family *(optional)*

Groups mutually-exclusive gem tiers.

Only the highest-priority eligible tier is recommended.

Useful for tiered support progression.

Example:

```json
"family": "Elemental Armament"
```

---

## prioritizeOverEmptySockets *(optional)*

Default:

```json
false
```

When enabled:

- Gem recommendations bypass empty-socket filling priority

---

# Gem Ordering

The order of entries inside the `"gems"` array defines priority.

Higher-priority gems:

- Receive better uncut gems
- Are recommended first

Rules:

- Skill/spirit gems use highest available uncut gems
- Support gems preserve high-tier uncut gems whenever possible

---

# CSV Gem Database

Gem validation and automatic tier detection are driven by CSV files:

```text
Data/skill_gems.csv
Data/spirit_gems.csv
Data/support_gems.csv
```

Required columns:

```text
Gem Name
Tier
```

Supported delimiters:

- Comma
- Semicolon

---

# Settings

| Setting | Description |
|---|---|
| `BuildsFolderPath` | Folder containing build JSON files (empty = config folder) |
| `SelectedBuild` | Active build |
| `ReloadData` | Reload CSVs and build files |
| `ColorBorder` | Inventory highlight border colour |
| `BorderThickness` | Inventory border thickness |
| `DebugGemMode` | Simulate uncut gems |
| `DebugPlayerLevelMode` | Simulate player level |

---

# Overlay Features

- Current recommendation display
- Previous/next navigation
- Inventory highlighting
- Active build selection
- Real-time recommendation updates

---

# Planned Improvements

- Multi-build profiles
- Export/import tooling
- Better support socket visualisation
- Advanced filtering
- Build validation diagnostics

---

# Contributing

Contributions, bug reports, and feature suggestions are welcome.

Please open an issue or submit a pull request.

---

# License

MIT License