# DerpACE Server

DerpACE is a customized ACEmulator server fork for Asheron's Call. It keeps the ACE server foundation and layers in DerpACE challenge modes, loot mutators, global events, admin tooling, custom content pipelines, and server quality-of-life systems.

This README is the current high-level project overview. Operator command workflows live in [ADMIN_COMMANDS.md](ADMIN_COMMANDS.md). Historical implementation notes were removed from this file so it stays useful as a starting point instead of a patch diary.

## Upstream Base

DerpACE is based on ACEmulator ACE, a C# open-source Asheron's Call server implementation using MySQL or MariaDB for world and shard data.

Useful upstream references:

- [ACEmulator ACE wiki](https://github.com/ACEmulator/ACE/wiki)
- [ACE development guide](https://github.com/ACEmulator/ACE/wiki/ACE-Development)
- [ACE hosting guide](https://github.com/ACEmulator/ACE/wiki/ACE-Hosting)
- [ACE content creation guide](https://github.com/ACEmulator/ACE/wiki/Content-Creation)

## Disclaimer

This project is for educational and non-commercial purposes only. Use of the game client is for interoperability with the emulated server.

Asheron's Call was a trademark of Turbine, Inc. and WB Games Inc. DerpACE and ACEmulator are not associated with or endorsed by Turbine, Inc. or WB Games Inc.

## Runtime Configuration

DerpACE runtime settings are written to `DerpAce.json` next to the server binary. Use `@derpconfig reload` to reload runtime configuration and restart services that support live reload.

Many systems also expose live tuning through `@lootconfig list` and `@lootconfig set <key> <value>`. Changes that affect generated objects usually apply to future loot rolls, vendor restocks, or creature spawns; existing rolled objects keep their current properties unless a command explicitly changes them.

Important config areas:

| Area | Notes |
|---|---|
| Challenge modes | Ironman, Nomad, Hardcore, and Hardcore Crawler gates, XP scalars, lives, and progression settings. |
| Loot and mutators | Weapon, shield, armor, clothing, jewelry, caster, and mob modifier drop/proc settings. |
| Vendors | Random vendor loot, town/tier behavior, bank spending, and restock behavior. |
| Admin map | Host, port, auth token, map image, calibration, refresh, and browser tools. |
| Pathfinding | DotRecast navmesh generation, loading, rebuild, cache, import, and export settings. |
| Boss mechanics | Database-backed boss profile drafts, published revisions, JSON fallbacks, and web editor behavior. |
| Bank and mail | Currency banking, vendor bank spend, direct deposit, item mail, COD, and item preview behavior. |

## Player-Facing Systems

### Challenge Modes

DerpACE currently supports:

- **Ironman**: self-found challenge mode with isolated economy rules and optional blind progression.
- **Nomad Ironman**: weaponless/casterless Ironman variant using unarmed gauntlet and shoe damage, Nomad tools/runes, and optional Lifebound infinite-life play that preserves chosen character stats/trained skills, preserves Light Weapons and existing Melee Defense specializations, uses normal gear provenance, and has no public scoreboard placement.
- **Hardcore**: death-limited challenge mode with challenge gear provenance and economy isolation.
- **Hardcore Crawler**: Hardcore submode where skills, stats, levels, boons, and rewards are driven primarily by use-based progression.

Crawler highlights:

- Skills become ready to train or specialize through use, then the player chooses with `/crawler train <skill>` or `/crawler spec <skill>`.
- XP display is masked for Crawlers; normal quest XP converts into Crawler Favor, which can grant modest single-roll quest caches.
- Early sponsor packages can be chosen through level 3 with `/crawler origins` and `/crawler origin <number|name>`; they train a few normal skills, spend normal skill credits, and grant a starter kit.
- Crawlers who flag for player combat broadcast as pink radar blips; defeating one awards their skull and can roll a red-ringed sponsor bounty biased toward the Crawler's used combat skills.
- Specialized rank gains drive Crawler levels.
- Milestone caches, milestone trials, Fan Box rewards, queued boon choices, and flaw boons persist through logout/restart.
- Crawler magic has built-in spell foci.
- Spam-friendly skills have slower progression gates: Assess Creature, Assess Person, Arcane Lore, Recklessness, Loyalty, and Leadership take longer than normal use skills.
- Summoning can be learned through pet device use; practice scales from pet/device difficulty once summons succeed.
- Loyalty and Leadership slowly grow from allegiance XP pass-up sent and received.

### Global Quests

`/gquest` shows active half-hour, hourly, daily, and weekly global quests. Quest types include hunts, dungeon/mutator hunts, item races, chug races, Cardinal Trek, Dereth Express, Correct the Corruption, and high-tier luminance/currency variants.

### Mail, Bank, and Currency

Player mail supports text mail, MMD payment, item shipping, COD, claiming, declining, deletion, and item preview. Banking supports stackable currency and bankable item storage with optional direct deposit and vendor bank spend.

### Teleport Requests

When enabled, `/tp <player>` starts a player-to-player teleport request flow with accept, decline, and cancel commands plus configurable costs/timing.

## Loot and Mutators

DerpACE extends loot generation with custom weapon, shield, armor, clothing, jewelry, caster, pet device, and mob systems. Forced testing is available through `@lootgen`, `testlootgen`, and related admin tools.

Major loot/mutator families include:

| Family | Examples |
|---|---|
| Weapon and caster mutators | Thief, Quickening, Fencer, Pugilist, Ravager, Warden, Resolute, Polebreaker, Stalker, Breacher, Reaper, Archmagi, Hierophant, Shadow Clone, Bedlam, Skybreaker, Stormcaller, Orbitweaver. |
| Shield mutators | Defender, Thorns, Bashing, Reflection, Spell Mirror. |
| Armor and clothing mutators | Armor banes, Culinarian gloves, Alchemist gloves, Alchemical Instability, unarmed hand/footwear, dance boots. |
| Jewelry | Vampiric jewelry with configurable drop and regen/on-hit behavior. |
| Mob modifiers | Nocturnal, Exploding, Vampiric, Thief, Scout, Simulacrum, Healer, Tank, Reaper, Necromancer, Warder, and related future-spawn tuning. |

Notable current behavior:

- Quickening daggers stack attack animation speed up to 4x, cost matching stamina, then trigger a cooldown at max stack burst.
- Bedlam/Confusion casters make affected monsters friendly to the player and hostile toward nearby mobs for the effect window.
- Scavenger tools and Nomad items support the current Nomad/Crawler challenge ecosystem.

## Loot Lab and Admin Web Tools

The admin map service defaults to `http://127.0.0.1:9110/` and is configured through `DerpAce.json`. It includes authenticated admin-only tools such as:

- `/loot-lab`: tune T1-T100 loot tier profiles, spell weights, and WCID weights.
- `/boss-mechanics`: create, validate, publish, roll back, spawn, and despawn boss mechanics profiles.
- `/spell-workshop`: inspect, create, clone, validate, save, and reload custom spell JSON packages.

Do not expose the admin map publicly without firewall/VPN protection and a strong token.

## Custom Content Pipelines

| Pipeline | Path / commands |
|---|---|
| Custom spells | `Data/CustomSpells`, `@customspells reload`, `@customspells export`, `@customspells exportcopy`, `@customspells import`. |
| Custom ClothingBase | `Data/CustomClothingBase`, `@cbexport`, `@cbclone`, `@cbreload`, `@cbclear`. |
| Boss mechanics | Database profiles plus optional `Data/DerpACE/BossMechanics/<wcid>*.json` fallback templates. |
| Admin map assets | `Data/AdminMap/icons` using eight-digit hex DID PNG filenames. |

## Vendor Random Loot

Random vendor loot can be enabled and tuned at runtime. Vendor tier detection uses PointsOfInterest anchors and DerpACE town progression. Admins can inspect or pin a vendor tier with `@vendortier`; vendor inventories reroll when the vendor reloads or restocks according to current settings.

Dereth Express uses normal vendor interactions to learn eligible source towns for delivery-race global quests.

## Pathfinding

Monster pathfinding uses DotRecast navmeshes generated on demand and cached to disk. Admins can enable/disable pathfinding, rebuild the current landblock mesh, prebuild meshes, list cached meshes, and import/export mesh packs with `@pathfinding` commands.

## Startup and Maintenance

DerpACE keeps ACE startup behavior while reducing repeated expensive work:

- World customization SQL tracks a persistent metadata manifest so unchanged scripts and unchanged known failures are skipped.
- DDD can cache validated DAT file sizes and optionally precompress DAT records.
- Player and housing startup use bounded split-query batches and report phase timing.
- Deleted-character recovery remains active; the full orphan-property sweep is scheduled separately and tracked per shard database.
- Startup uses one compacting generation-2 garbage collection before opening the world.

## Common Commands

Player commands:

| Command | Purpose |
|---|---|
| `/ironman on`, `/ironman nomad`, `/ironman confirm`, `/ironman char`, `/ironman top` | Ironman and Nomad challenge flow. |
| `/hardcore on`, `/hardcore confirm`, `/hardcoretop` | Hardcore challenge flow. |
| `/crawler on`, `/crawler confirm`, `/crawler status`, `/crawler origins`, `/crawler origin`, `/crawler choices`, `/crawler pick`, `/crawler train`, `/crawler spec`, `/crawler trial` | Hardcore Crawler flow. |
| `/gquest` | Global quest status and rewards. |
| `/mail help` | Player mail commands. |
| `/bank`, `/cash`, `/ddt` | Banking, currency, and direct deposit controls. |
| `/tp`, `/tp accept`, `/tp decline`, `/tp cancel` | Player teleport request flow. |
| `/pop`, `/population` | Online population. |
| `/cast-style` | Casting animation style preference. |

Admin and operator commands are summarized in [ADMIN_COMMANDS.md](ADMIN_COMMANDS.md). Use in-game help for inherited ACE command discovery and exact access levels.

## Build and Test

Typical local verification:

```text
dotnet build Source\ACE.sln -c Release
dotnet test Source\ACE.Server.Tests\ACE.Server.Tests.csproj -c Release --no-restore
```

The compiled server output is under:

```text
Source\ACE.Server\bin\Release\net10.0
```

## Contributing

Keep changes focused and consistent with the existing ACE/DerpACE style. For DerpACE operator behavior, update [ADMIN_COMMANDS.md](ADMIN_COMMANDS.md) and this README when commands, mode rules, runtime config, or admin workflows change.

This project follows the [Contributor Code of Conduct](CODE_OF_CONDUCT.md).
