# DerpACE Admin Command Guide

This guide covers DerpACE-specific operator workflows. Commands are entered in game with `@`; the parser also accepts `/` on servers configured with the standard DerpACE command behavior. Access requirements are enforced by each command handler.

## Global Quests

| Command | Access | Purpose |
|---|---|---|
| `@gquestreroll daily` | Admin | Ends and rerolls the current daily quest. |
| `@gquestreroll weekly` | Admin | Ends and rerolls the current weekly quest. |
| `@gquestreroll all` | Admin | Rerolls daily, then weekly. |
| `/gquest` | Player | Displays half-hour, hourly, daily, and weekly objectives, personal progress, rewards, completion state, and remaining time. |

Forced rerolls use the normal freshness rules: daily and weekly cannot match each other, and neither lane repeats its outgoing quest type. The replacement is announced and saved immediately.

Persistent global quests include tier-8 hunts, mutator and dungeon hunts, Correct the Corruption, Cardinal Trek, Dereth Express, chug races, and item races. Cardinal Trek counts grounded overworld travel only. Dereth Express parcels are stamped to the purchaser, source vendor, and quest epoch. Correct the Corruption coins only drop while that event is active, stack to save pack space, and partial contributions can be paid when the event ends.

## Runtime Configuration

| Command | Access | Purpose |
|---|---|---|
| `@derpconfig reload` | Developer | Reloads `DerpAce.json` and restarts services that require it, including the admin map. |
| `@lootconfig list` | Developer | Lists live loot, mutator, mob, armor, and vendor tuning. |
| `@lootconfig set <key> <value>` | Developer | Changes a supported live tuning value. |
| `@ironmanmode on|off|toggle|status` | Admin | Controls whether players may opt into Ironman modes. |
| `@wiflag <player> [reroll|on|off]` | Admin | Shows or changes a player WI loot bias flag for testing. |

Changes affecting generated objects apply to future loot rolls, spawns, or vendor restocks. Existing objects retain their rolled properties unless explicitly converted.

## Vendor Tools

| Command | Access | Purpose |
|---|---|---|
| `@vendortier` | Developer | Shows the detected town and automatic tier for the last appraised vendor. |
| `@vendortier <1-8>` | Developer | Pins an explicit vendor loot tier; automatic town progression uses T1-T7. |
| `@vendortier clear` | Developer | Removes the override and restores automatic town resolution. |
| `@vendordump` | Developer | Displays properties for the last examined vendor. |

Automatic vendor tiers use PointsOfInterest anchors and the seven-tier DerpACE town progression documented in [README.md](README.md#town-tier-resolution-sourceaceserverfactoriestablesvendortowntiercs).

Dereth Express learns eligible source vendors through normal vendor interactions. At least two proven towns must be present in the saved global quest vendor registry before a delivery race can roll.

## Loot And Mutator Testing

| Command | Access | Purpose |
|---|---|---|
| `@lootgen weapon <tier> [luck=0-1] [mutator=name]` | Developer | Generates a random weapon and optionally forces a compatible mutator. |
| `@lootgen <wcid-or-classname> <tier> [luck=0-1] [mutator=name]` | Developer | Mutates a specific compatible base item. |
| `testlootgen -info` | Admin/console | Displays bulk loot-generation examples. |
| `testlootgen <count> <tier> <category>` | Admin/console | Bulk-generates a loot category for testing. |
| `@cimobspawn <modifier>` | Admin | Spawns a test creature with a forced mob modifier. |

Use `@lootconfig list` to inspect current probabilities before comparing generated samples.

## Custom Spell And Clothing Data

| Command | Access | Purpose |
|---|---|---|
| `@customspells reload` | Admin | Reloads custom spell JSON data. |
| `@customspells export <spellId>` | Admin | Exports a spell package. |
| `@customspells exportcopy <spellId>` | Admin | Clones a spell to a free custom ID, exports it, and loads it. |
| `@customspells import <file.sql>` | Admin | Imports a DerpACE spell package. |
| `@cbexport <clothingBaseId> [label]` | Developer | Exports a ClothingBase JSON override. |
| `@cbclone <sourceId> <newId>` | Developer | Clones a ClothingBase definition. |
| `@cbreload` | Developer | Reloads custom ClothingBase JSON. |
| `@cbclear` | Developer | Flushes the clothing-table cache. |

The browser Spell Workshop is at `http://127.0.0.1:9110/spell-workshop` and requires an authenticated admin-map session. Load a known template spell, choose a custom ID from `65001` through `65535`, edit or validate the generated JSON, then use **Save + Reload** to write it under `Data/CustomSpells`. The right-side reference explains every supported top-level field and the advanced `SpellBase` and `DbSpell` records.

## Boss Mechanics

| Command | Access | Purpose |
|---|---|---|
| `@boss create <profile> <sourceWcid> [newBossWcid]` | Admin | Creates a draft profile. If `newBossWcid` is provided, exports cloned boss SQL that must be imported/reloaded before spawning. |
| `@boss add-minions <profile> <health%> <minionWcid> <count>` | Admin | Adds maintained 100-health nuisance minions at a health threshold. |
| `@boss add-taunt <profile> <health%> <local|fellowship> <text>` | Admin | Adds threshold speech using local or fellowship delivery. |
| `@boss add-say <profile> <health%> <text>` | Admin | Adds simple local threshold speech. |
| `@boss add-effect <profile> <health%> <PlayScript>` | Admin | Adds a validated PlayScript effect. |
| `@boss show|validate|publish|rollback <profile>` | Admin | Reviews, validates, publishes, or rolls back a profile revision. |

The browser operations page is at `http://127.0.0.1:9110/boss-mechanics` and uses the same admin map login/session. It lists every database profile plus valid JSON templates from `Data/DerpACE/BossMechanics`, imports templates as database drafts, edits the authoritative draft JSON and individual rules, validates and publishes revisions, rolls back or enables/disables profiles, spawns published bosses at an online player or full LOC, and lists/despawns active boss instances. World spawns and despawns are queued onto the server world action loop. Published profiles are cached by WCID and invalidated when changed. JSON fallbacks can live in `Data/DerpACE/BossMechanics/<wcid>*.json`. The built-in `frost_rain` action accepts `target`, `count` (1-8 waves), and `damageScale` (0.05-1.0). Push, pull, scatter, knock-up, and blink each use a movement-appropriate visual effect. The `mirror_minions` action spawns hostile Simulacrum-style copies of random nearby players or the triggering player's fellowship, using `weenieClassId` as the shell, plus `count`, `health`, `source`, `radius`, `durationSeconds`, `noXp`, `dropItems`, `noCorpse`, and `translucency` toggles. Copied player equipment is for the clone appearance/combat package and is filtered from corpse loot.

## Pathfinding

| Command | Access | Purpose |
|---|---|---|
| `@pathfinding status` | Developer | Shows whether pathfinding is enabled and how many meshes are cached/pending. |
| `@pathfinding on|off` | Developer | Toggles the server pathfinding property. |
| `@pathfinding load|rebuild|unload` | Developer | Loads, rebuilds, or unloads the current landblock navmesh. |
| `@pathfinding list` | Developer | Lists cached navmesh ids. |
| `@pathfinding prebuild [stop]` | Developer | Starts or cancels background mesh prebuilding. |
| `@pathfinding export <zipPath>` / `@pathfinding import <zipPath>` | Developer | Shares cached indoor/outdoor navmesh packs. |

Meshes are generated on demand and cached under the configured pathfinding mesh root. Rebuild affected landblocks if old cached outdoor meshes still produce bad routes.

## Admin Map

The web admin map is configured in `DerpAce.json` with the `admin_map_*` settings. After changing host, port, token, map image, calibration, or refresh values, run:

```text
@derpconfig reload
```

The default local address is `http://127.0.0.1:9110/`. Admin accounts can use controls and edit inventory data; player accounts are limited to their own account/fellowship view. The admin-only boss profile editor and live spawn operations are available at `/boss-mechanics`; the custom Spell Workshop is available at `/spell-workshop`. Inventory icons load from `Data/AdminMap/icons` using eight-digit hexadecimal DID PNG filenames. Do not expose the service publicly without a strong token and appropriate network controls.

## General Command Discovery

Use the built-in help command to inspect inherited ACE commands and their required access level. The full generated command index remains in [README.md](README.md#full-command-reference); this guide intentionally stays focused on DerpACE operator workflows.