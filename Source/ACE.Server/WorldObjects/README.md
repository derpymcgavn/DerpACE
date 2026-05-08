# ACEmulator Core Server

[![Discord](https://img.shields.io/discord/261242462972936192.svg?label=play+now!&style=for-the-badge&logo=discord)](https://discord.gg/C2WzhP9)

Build status: [![GitHub last commit (master)](https://img.shields.io/github/last-commit/acemulator/ace/master)](https://github.com/ACEmulator/ACE/commits/master) [![Windows CI](https://ci.appveyor.com/api/projects/status/rqebda31cgu8u59w/branch/master?svg=true)](https://ci.appveyor.com/project/LtRipley36706/ace/branch/master) [![docker build](https://github.com/ACEmulator/ACE/actions/workflows/docker-image.yml/badge.svg)](https://hub.docker.com/r/acemulator/ace)

[![Download Latest Server Release](https://img.shields.io/github/v/release/ACEmulator/ACE?label=latest%20server%20release) ![GitHub Release Date](https://img.shields.io/github/release-date/acemulator/ace)](https://github.com/ACEmulator/ACE/releases/latest)
[![Download Latest World Database Release](https://img.shields.io/github/v/release/ACEmulator/ACE-World-16PY-Patches?label=latest%20world%20database%20release) ![GitHub Release Date](https://img.shields.io/github/release-date/acemulator/ACE-World-16PY-Patches)](https://github.com/ACEmulator/ACE-World-16PY-Patches/releases/latest)

[![GitHub All Releases](https://img.shields.io/github/downloads/acemulator/ace/total?label=server%20downloads)](https://github.com/ACEmulator/ACE/releases) [![GitHub All Releases](https://img.shields.io/github/downloads/acemulator/ACE-World-16PY-Patches/total?label=database%20downloads)](https://github.com/ACEmulator/ACE-World-16PY-Patches/releases) [![Docker Pulls](https://img.shields.io/docker/pulls/acemulator/ace)](https://hub.docker.com/r/acemulator/ace)

**ACEmulator is a custom, completely from-scratch open source server implementation for Asheron's Call built on C#**
 * MySQL and MariaDB are used as the database engine.
 * Latest client supported.
 * [![License](https://img.shields.io/github/license/acemulator/ace)](https://github.com/ACEmulator/ACE/blob/master/LICENSE)

***
## Disclaimer
**This project is for educational and non-commercial purposes only, use of the game client is for interoperability with the emulated server.**
- Asheron's Call was a registered trademark of Turbine, Inc. and WB Games Inc which has since expired.
- ACEmulator is not associated or affiliated in any way with Turbine, Inc. or WB Games Inc.
***
## Getting Started
Extended documentation can be found on the project [Wiki](https://github.com/ACEmulator/ACE/wiki).
* [Developing ACE](https://github.com/ACEmulator/ACE/wiki/ACE-Development)
* [Hosting ACE](https://github.com/ACEmulator/ACE/wiki/ACE-Hosting)
* [Content Creation](https://github.com/ACEmulator/ACE/wiki/Content-Creation)

## Contributions
* Contributions in the form of issues and pull requests are welcomed and encouraged.
* The preferred way to contribute is to fork the repo and submit a pull request on GitHub.
* Code style information can be found on the [Wiki](https://github.com/ACEmulator/ACE/wiki/Code-Style).

Please note that this project is released with a [Contributor Code of Conduct](https://github.com/ACEmulator/ACE/blob/master/CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

## Bug Reports
* Please use the [issue tracker](https://github.com/ACEmulator/ACE/issues) provided by GitHub to send us bug reports.
* You may also discuss issues and bug reports on our discord listed below.

## Contact
* [Discord Channel](https://discord.gg/C2WzhP9)

***
## DerpACE Custom Changes

### Random Dye (Enigmatic Dye)
* Added `RandomDye` world object class (`WCID 420420420`) that applies a random palette to the target item
* Works on **armor, clothing, weapons (melee/missile), casters, and shields** — any item with a `ClothingBase` property
* Plays the crafting (`ClapHands`) animation before applying, matching the behavior of normal dyes
* Refreshes the item's appearance for all nearby players after dyeing (`GameMessageUpdateObject` / `GameMessageObjDescEvent` for equipped items)
* Consumes exactly 1 from the stack on use
* Switches the player out of combat mode if needed before animating
* Database `TargetType` should be set to `33031` (MeleeWeapon | Armor | Clothing | MissileWeapon | Caster)

### Foci Improvements
* Foci (Enchantment 15268, Artifice 15269, Verdancy 15270, Strife 15271, Shadow 43173) now default to **15 item slots** instead of 0
* Foci only accept **scarabs** (all tiers: lead, iron, copper, silver, gold, pyreal) and **prismatic tapers** (regular WCID 20631 and PEA variant WCID 20963)
* Attempting to place any other item into a foci displays the message: *"Only scarabs and prismatic tapers can be placed in a focus."*

### Loot Generation Additions
* Added **Lyceum Hood** (`ace44977_lyceumhood`, WCID 44977) to the leather armor loot table (`ArmorWcids.LeatherWcids`) at 2% chance
* Added **Fletcher's Cap** (`hatfletcher`, WCID 9624) to the leather armor loot table (`ArmorWcids.LeatherWcids`) at 2% chance

### Life Magic Casters
* Added **Martyr's Staff** (`ace420420421_martyrstaff`, WCID 420420421) — a custom life-magic caster weapon
* Appears in the **Caster loot table at T7 (1.5%) and T8 (3.5%)**
* Any caster with WCID in the `LifeCasterWcids` set automatically receives `W_DamageType = DamageType.Health` during loot mutation, enabling life-magic spell bonuses on the tooltip
* `UiEffects` should be set to `5` (Magical | BoostHealth) in the weenie SQL, not `4096` (Nether)

### Dart Flinger Loot Table
* Added 7 elemental **Dart Flingers** (WCIDs 5238245–5238251: Acid, Blunt, Electric, Fire, Frost, Piercing, Slashing) to the **Atlatl loot table**
* Appear at **T5 (1% each)** and **T6–T8 (1.5% each)** alongside slingstones and standard atlatls
* All weights rebalanced to sum exactly to 1.0

### Defender's Shield
* **5% of all shield loot drops** receive the "Defender's" prefix (e.g. *Defender's Kite Shield*)
* The item stores `PropertyBool.IsDefendersShield = true` on the world object
* Long description reads: *"This shield resonates with a protective challenge — enemies are more likely to target its bearer."*
* Monsters using **Random targeting** (the most common tactic) give the Defender's shield wearer **+0.5 weight** in `SelectWeightedDistance`, making them roughly 50% more likely to be targeted than an equal-distance player
* Effect is live — unequipping the shield removes the taunt immediately

### Admin Flight Mode (`@fly`)
* `@fly` / `@fly on` / `@fly off` — toggles gravity off on the player and broadcasts the physics state change to nearby clients
* Sets `IsAdminFlying = true` — a transient in-memory flag (resets on logout/restart)
* **Fall damage is suppressed** while `IsAdminFlying` is true (checked in `TakeDamage_Falling`)
* Requires **Developer** access level

### Admin Movement Commands
All commands require **Developer** access level and accept an optional distance argument (default: 10 units).

| Command | Effect |
|---|---|
| `@up [n]` | Teleport upward by n units |
| `@down [n]` | Teleport downward by n units |
| `@forward [n]` | Teleport in the direction you are facing |
| `@backward [n]` | Teleport opposite to facing direction |
| `@left [n]` | Strafe 90° left of facing |
| `@right [n]` | Strafe 90° right of facing |

Direction is calculated from the character's current heading (`RotationW`/`RotationZ`) at the time the command is issued. Each command triggers a brief server-side teleport.

### Archmagi Caster
* **5% of T7–T8 caster loot drops** receive the "Archmagi" suffix (e.g. *Orb of the Archmagi*)
* The item stores `PropertyBool.IsArchmagiCaster = true` on the world object
* On each successful spell cast, `TryProcArchmagi` fires with a **10% proc chance**:
  * Rolls a random level of the same spell school/family and casts it for free on the same target
  * Life casters proc a random `HealSelf` level; other casters proc a random level of the weapon's `SpellDID` family
* Proc is handled in `Player_Magic.cs` after `HandleCastSpell` succeeds

### Thief's Daggers
* **5% of T6+ dagger loot drops** (Dagger and DaggerMultiStrike types) are converted to a Thief's Dagger (e.g. *Obsidian Kris of the Thief*)
* The item stores `PropertyBool.IsThievesDagger = true` on the world object
* **Wield requirement:** Specialized Sneak Attack skill
* **Icon underlay:** Acid rending icon (`0x06003355`)
* **Long description:** notes the stealth, aggro reduction, and sneak attack proc

#### Stealth Effect (equip/unequip)
* Equipping a Thief's Dagger plays the `SkillDownBlack` particle effect, briefly deletes the player for nearby clients, then recreates them at **50% translucency** (`ObjScale` / `Translucency = 0.5f`)
* Unequipping reverses the process — `Translucency` is cleared and the player is recreated at full opacity with an `UnHide` particle
* **Dual-wield aware:** translucency is only removed when the *last* Thief's Dagger is unequipped
* Player receives a private `Magic` chat message: *"You slip into the shadows."* / *"You step out of the shadows."*
* `Translucency` is set synchronously before the action chain so the tracking system always serializes the correct value

#### Mob Aggro Reduction
* In `Monster_Awareness.SelectWeightedDistance`, Thief's Dagger bearers receive a **−0.4 weight penalty** in monster target selection
* Applied both to the global `invRatioSum` and per-target weight, making Thief's Dagger wielders roughly 40% less likely to be the primary attack target

#### Sneak Attack Bonus (proc)
* On each sneak attack hit with a Thief's Dagger equipped, there is a **10% proc chance** to deal an additional **+10% damage**
* When the proc fires, the player sees: `+N [Thief's Dagger]` in the combat chat channel (after the standard hit notification)
* Long description reads: *"Sneak attacks have a 10% chance to proc an additional 10% bonus damage."*

### Wacky Loot Event
* A lightweight server-side event flag system (`ServerEvents` static class) that requires no database entries
* `@start event wacky` — enables the Wacky Loot event; broadcasts *"A strange wind sweeps through Dereth..."* to all players
* `@end event wacky` — disables it; broadcasts *"The strange wind passes. Loot returns to normal."*
* Both commands require **Developer** access level
* While active, all **weapon and shield loot drops** receive:
  * A random `ObjScale` between **0.25 and 3.25** (tiny to gigantic)
  * The `[Whack]` prefix baked into the item name, placed **before the material type** (e.g. `[Whack] Ebony Sword`)
  * `MaterialType` is zeroed after baking the material name into `wo.Name` to prevent the client double-prepending it
* New events can be added by extending the `ServerEvents` class and the `start`/`end` switch statements in `DerpACEEventCommands.cs`
