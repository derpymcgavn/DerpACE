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

### Current Loot Mutator Specs (June 12, 2026)
This section is the current source of truth for lootgen mutators and commands. Older notes below are historical and may describe earlier balance values.

#### Admin Commands
| Command | Purpose |
|---|---|
| `@lootconfig list` | Prints runtime loot, mutator, armor, mob, and vendor tuning values. |
| `@lootconfig set <key> <value>` | Changes a runtime tuning value immediately. Example: `@lootconfig set sentinel.cooldown 14`. |
| `@lootgen weapon <tier> [luck=0-1] [mutator=name]` | Creates a random loot weapon and can force a compatible weapon/caster mutator. Example: `@lootgen weapon 7 mutator=discus`. |
| `@lootgen <wcid-or-classname> <tier> [luck=0-1] [mutator=name]` | Mutates a specific item if that weenie has `PropertyInt.TsysMutationData`; forced shield mutators can be applied to shield WCIDs/classnames. Example: `@lootgen shieldtower 7 luck=1 mutator=bashing`. |
| `testlootgen -info` | Console examples for bulk loot generation. |
| `testlootgen <count> <tier> <melee|missile|caster|armor|jewelry|cloak|all>` | Console bulk loot test by table. |

Forced `@lootgen` mutator aliases: weapons/casters use `thief`, `quickening`, `fencer`, `ravager`, `warden`, `resolute`, `polebreaker`, `sentinel`, `stalker`, `breacher`, `dinnerware`, `discus`, `dartflinger`, `reaper`, `archmagi`, `shadowclone`, `hierophant`; shield WCIDs/classnames use `defender`, `thorns`, `bashing`.

#### Weapon And Caster Mutators
| Mutator | Eligible loot | Current effect |
|---|---|---|
| `thief` | Daggers | Requires specialized Sneak Attack. Sneak attacks can add bonus damage and open a hidden seam, reducing target defense briefly. Lowers monster targeting weight. |
| `quickening` | Daggers | On hit, can speed the wielder's attack animation for a short duration. No start visual; expiration gives feedback. |
| `fencer` | SwordMS: epee, rapier, schlager | Chance to recover part of armor-mitigated damage as bonus damage, plus a small riposte chance against incoming melee pressure. |
| `ravager` | Axes and two-handed axes | Axes bleed over ticks. Hammer-named axe variants use a crushing guard/stamina hit instead. |
| `warden` | Maces, jittes, two-handed maces | Chance to concuss the target, lowering effective defense for a short duration. |
| `resolute` | Swords and two-handed swords | Critical hits can heal from damage dealt; killing blows give a small health/stamina burst. |
| `polebreaker` | Staves | Hits at 70%+ power build same-target rhythm. At full rhythm, Break Guard plays a fast overhead slam, applies a defense penalty, resets rhythm, and starts a visible cooldown. |
| `sentinel` | Spears and two-handed spears | Goldleaf Sentinel. Hits at configured power or higher build same-target poise. At full stacks, drains target stamina, returns part of it, gives short damage reduction, and starts a visible cooldown. |
| `stalker` | Bows | First registered hit on a target can gain bonus damage. |
| `breacher` | Crossbows | Always recovers a small part of armor-mitigated damage as bonus pierce damage. |
| `dartflinger` | Dart flinger atlatl family only | Ricochet-style bounce behavior for dartflingers. Separate from standard atlatls. |
| `reaper` | Standard atlatls only | Killing blows can restore a small percentage of max health. Does not roll on dartflingers. |
| `dinnerware` | Throwable dinnerware | Banquet spin/bounce behavior. Projectiles visually bounce up to 5 targets with falling damage: 100%, 50%, 25%, 10%, 5%. |
| `discus` | Discus WCID 8211 as missile loot | Most are plain discus. About 1 in 100 rolls become `Discus of the Warrior Princess's Call`, using dinnerware bounce behavior with discus combat log flavor at a 5-8% proc rate. Lootgen strips admin-added spell/proc/resistance/crit extras so damage stays in line with dinnerware. |
| `archmagi` | Casters | Chance on successful cast to echo an additional same-family spell. |
| `hierophant` | Life casters / Martyr staff family | Heal support caster with heal boost, HoT chance, fellowship echo, and healer aggro tuning. |
| `shadowclone` | Void casters only | Umbral Mirror caster can summon a temporary shadow clone combat ally on a 120 second visible cooldown. Clone uses shadow visuals and void/ring style spell support. |
| `blast` | Rare elemental weapon overlay | T5+ elemental weapons can rarely also roll a level-3 blast-on-strike proc. Nether is excluded from general caster/weapon blast rolls. Ring procs cast from the player toward the target location. |

#### Shield Mutators
| Mutator | Current effect |
|---|---|
| Defender | Adds monster targeting weight to the shield bearer. |
| Thorns | Reflects a small percentage of damage actually taken on shield-blocked hits. Kept low to avoid runaway reflect builds. |
| Bashing | Requires specialized Shield. On block, can deal shield-AL-scaled bash damage, push the attacker back 10 feet, and interrupt a monster spell windup with fizzle feedback. |

#### Important `@lootconfig` Keys
| Family | Keys |
|---|---|
| Thief | `thief.drop`, `thief.tier`, `thief.proc`, `thief.bonus`, `thief.aggro`, `thief.seampenalty`, `thief.seamduration` |
| Goldleaf Sentinel | `sentinel.drop`, `sentinel.tier`, `sentinel.power`, `sentinel.stacks`, `sentinel.drain`, `sentinel.return`, `sentinel.cooldown`, `sentinel.poisedur`, `sentinel.poisedr`, `sentinel.aggro` |
| Polebreaker | `polebreaker.drop`, `polebreaker.tier`, `polebreaker.stackmin`, `polebreaker.stackmax`, `polebreaker.maxstackmin`, `polebreaker.maxstackmax` |
| Dinnerware / Discus | `dinnerware.drop`, `dinnerware.tier`, `dinnerware.spin`, `dinnerware.spintier`, `dinnerware.scale`, `dinnerware.radius` |
| Dartflinger / Ricochet | `ricochet.drop`, `ricochet.tier`, `ricochet.procmin`, `ricochet.procmax`, `ricochet.scale`, `ricochet.radius` |
| Quickening | `quickening.drop`, `quickening.tier`, `quickening.procmin`, `quickening.procmax`, `quickening.speedmin`, `quickening.speedmax`, `quickening.durmin`, `quickening.durmax` |
| Elemental blast | `blast.mintier`, `blast.chancemin`, `blast.chancemax`, `blast.ratemin`, `blast.ratemax` |

#### Custom Clothing Base Pipeline
Custom clothing JSON filenames now identify the custom `ClothingBase` id. Save files under `Source/ACE.Server/Data/CustomClothingBase/<clothingBaseId>[_label].json`; the loader uses the filename id so custom clothing items can add new entries instead of overwriting portal defaults. Use `@cbexport <id> [label]`, edit the JSON, then run `@cbreload` or restart.

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
* Foci only accept **scarabs** (all tiers: lead, iron, copper, silver, gold, pyreal), **prismatic tapers** (regular WCID 20631 and PEA variant WCID 20963), and **mana stones**
* Attempting to place any other item into a foci displays the message: *"Only scarabs, prismatic tapers, and mana stones can be placed in a focus."*

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
* **Icon overlay:** `0x06002878`
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
* **Icon overlay:** `0x06002860`
* On each successful spell cast, `TryProcArchmagi` fires with a **6% proc chance**:
  * Rolls a random level of the same spell school/family and casts it for free on the same target
  * Life casters proc a random `HealSelf` level; other casters proc a random level of the weapon's `SpellDID` family
* Proc is handled in `Player_Magic.cs` after `HandleCastSpell` succeeds

### Thief's Daggers
* **5% of T6+ dagger loot drops** (Dagger and DaggerMultiStrike types) are converted to a Thief's Dagger (e.g. *Obsidian Kris of the Thief*)
* The item stores `PropertyBool.IsThievesDagger = true` on the world object
* **Wield requirement:** Specialized Sneak Attack skill
* **Icon underlay:** `0x060065FC`
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
* On each sneak attack hit with a Thief's Dagger equipped, there is a **6% proc chance** to deal an additional **+10% damage**
* When the proc fires, the player sees: `+N [Thief's Dagger]` in the combat chat channel (after the standard hit notification)
* Long description reads: *"Sneak attacks have a 10% chance to proc an additional 10% bonus damage."*

### Sentinel's Spears
* **5% of T6+ spear loot drops** (Spear and TwoHandedSpear types) are converted to a Sentinel's Spear (e.g. *Obsidian Spear of the Sentinel*)
* The item stores `PropertyBool.IsSentinelSpear = true` on the world object
* **Icon overlay:** `0x06002699`
* **Long description:** notes the stamina drain and return proc

#### Stamina Drain (proc)
* On each hit with a Sentinel's Spear, there is a **6% proc chance** to drain **10% of the target's current stamina**
* **125% of the drained stamina is returned to the wielder**
* Plays `HealthDownYellow` on the target and `HealthUpYellow` on the player when the proc fires
* When the proc fires, the player sees: `-N stamina [TargetName] +N [Sentinel's Spear]` in the combat chat channel

### Fencer's Blades
* **5% of T6+ SwordMS loot drops** (épée, rapier, schlager) are converted to a Fencer's Blade (e.g. *Emerald Épée of the Fencer*)
* `TreasureWeaponType.SwordMS` covers exactly these three weapon types, so no additional WCID filter is needed
* The item stores `PropertyBool.IsFencerBlade = true` on the world object
* **Icon overlay:** `0x06002699` (placeholder — swap for a blade-appropriate overlay)
* Drop chance and all proc ranges are runtime-adjustable via `@lootconfig` (`fencer.*` keys)

#### Per-Weapon Values (rolled at loot time, stored as PropertyFloat)
| Property | Range | Description |
|---|---|---|
| `FencerArmorPiercePct` | 1–5% | Fraction of armor-mitigated damage recovered as bonus |
| `FencerArmorPierceProc` | 1–4% | Per-hit proc chance for the pierce |
| `FencerDeflectChance` | 1–2% | Per-incoming-hit chance to deflect |

#### Armor Pierce (outgoing proc)
* Fires in `Player.AttackTarget` after `DamageEvent.CalculateDamage` — only when `HasDamage` is true
* Bonus damage = `DamageMitigated × FencerArmorPiercePct` (i.e., X% of what armor actually blocked gets through)
* If armor blocked nothing (`DamageMitigated = 0`), the proc has no effect — pierce only matters against armored targets
* When the proc fires, the player sees: `+N pierce [Fencer's Blade]` in the combat chat channel

#### Deflect (incoming proc)
* Fires at the end of `Player.TakeDamage` after all damage, network messages, and cloak procs are resolved
* Reflect amount = `damageTaken × 0.10` — dealt to the attacker creature via `Creature.TakeDamage(player, DamageType.Pierce, amount)`
* Only fires against `Creature` attackers; does not trigger in PvP
* When the proc fires, the player sees: `[Fencer's Blade] Deflected! -N [AttackerName]` in the combat chat channel
* **Long description** example: *"This blade is perfectly balanced for dueling — each strike has a 3% chance to find a gap in the target's defenses, bypassing 4% of their armor. There is also a 2% chance per incoming hit to turn an attack aside and redirect 10% of its damage back at the assailant."*

### Ravager's Axes
* **5% of T6+ axe loot drops** (Axe and TwoHandedAxe types) are converted to a Ravager's Axe (e.g. *Obsidian Axe of the Ravager*)
* The item stores `PropertyBool.IsRavagersAxe = true` on the world object
* **Icon overlay:** `0x06002878`
* Drop chance and proc variables are runtime-adjustable via `@lootconfig` (`ravager.*` keys)

#### Bleed (proc)
* Each hit has a **2–5% proc chance** (rolled at loot time, stored as `PropertyFloat.RavagerBleedProc`) to inflict a bleed
* Bleed total = `hitDamage × bleedPct`, rolled per-weapon at loot time (default 30–60%, stored as `PropertyFloat.RavagerBleedPct`)
* **Two-handed axes** apply a `RavagerTwoHandMult` multiplier (default 1.5×) baked into the stored `RavagerBleedPct`, so they hit harder and longer
* Bleed total is split evenly across `ravager.ticks` ticks (default 3) at `ravager.interval` second intervals (default 2.0s) via an `ActionChain` on the wielder
* Each tick: re-checks `target.IsAlive`, deals `perTick` of the same damage type as the triggering hit, plays `SplatterMidLeftBack`, and emits `-N bleed [TargetName] [Ravager's Axe]` in CombatSelf chat
* On proc, an immediate announce message is sent: `[TargetName] is bleeding (+N) [Ravager's Axe]` (N = total bleed)
* **`@lootconfig` keys:** `ravager.drop`, `ravager.tier`, `ravager.procmin`, `ravager.procmax`, `ravager.bleedmin`, `ravager.bleedmax`, `ravager.twohandmult`, `ravager.ticks`, `ravager.interval`

### Warden's Mauls
* **5% of T6+ mace loot drops** (Mace and TwoHandedMace types) are converted to a Warden's Maul (e.g. *Ebony Mace of the Warden*)
* The item stores `PropertyBool.IsWardensMaul = true` on the world object
* Drop chance and proc variables are runtime-adjustable via `@lootconfig` (`warden.*` keys)

#### Concussion (proc)
* Each hit has a **2–5% proc chance** (stored as `PropertyFloat.WardenConcussProc`) to apply **Concussion** to the target
* Concussion reduces the target's effective defense skill by **10–30 points** (stored as `PropertyFloat.WardenConcussPenalty`) for **8 seconds**
* Tracked via `ConcussedUntil` timestamp + `ConcussedDefensePenalty` on the target `Creature`, checked in `DamageEvent` defense roll
* When the proc fires: `[TargetName] concussed (-N defense) [Warden's Maul]` in combat chat
* **Long description** example: *"This maul is weighted to shatter focus — each strike has a 3% chance to stagger the target, reducing their defense by N for 8 seconds."*
* **Planned `@lootconfig` keys:** `warden.drop`, `warden.tier`, `warden.procmin`, `warden.procmax`, `warden.penaltymin`, `warden.penaltymax`, `warden.duration`

### Berserker's Staves
* **5% of T6+ staff loot drops** (Staff and TwoHandedStaff types) are converted to a Berserker's Staff (e.g. *Granite Staff of the Berserker*)
* The item stores `PropertyBool.IsBerserkerStaff = true` on the world object
* Drop chance and proc variables are runtime-adjustable via `@lootconfig` (`berserker.*` keys)

#### Stamina Restore (proc)
* Each hit has a **2–5% proc chance** (stored as `PropertyFloat.BerserkerRestoreProc`) to restore stamina = `damage × restorePct` to the wielder
* Restore % rolled at loot time (25–50%), stored as `PropertyFloat.BerserkerRestorePct`
* Uses `UpdateVitalDelta(Stamina, restore)` — mirrors Sentinel's Spear but targets the wielder's own stamina
* When the proc fires: `+N stamina [Berserker's Staff]` in combat chat
* **Long description** example: *"This staff thrums with brutal momentum — each strike has a 3% chance to surge stamina back to the wielder, restoring N stamina from the impact."*
* **Planned `@lootconfig` keys:** `berserker.drop`, `berserker.tier`, `berserker.procmin`, `berserker.procmax`, `berserker.restoremin`, `berserker.restoremax`

### Resolute Blades
* **5% of T6+ sword loot drops** (Sword and TwoHandedSword types) are converted to a Resolute Blade (e.g. *Emerald Sword of Resolve*)
* The item stores `PropertyBool.IsResoluteBlade = true` on the world object
* Drop chance and proc variables are runtime-adjustable via `@lootconfig` (`resolute.*` keys)

#### Life Drain on Critical (proc)
* When a **critical hit** lands, there is a **2–5% proc chance** (stored as `PropertyFloat.ResoluteHealProc`) to restore health = `damage × healPct` to the wielder
* Heal % rolled at loot time (5–15%), stored as `PropertyFloat.ResoluteHealPct`
* Fires in `Player_Combat.AttackTarget` gated on `damageEvent.IsCritical`
* When the proc fires: `+N health [Resolute Blade]` in combat chat
* **Long description** example: *"This blade is forged for the decisive moment — critical strikes have a 3% chance to draw life from the wound, restoring N health to the wielder."*
* **Planned `@lootconfig` keys:** `resolute.drop`, `resolute.tier`, `resolute.procmin`, `resolute.procmax`, `resolute.healmin`, `resolute.healmax`

### Stalker's Bows
* **5% of T6+ bow loot drops** are converted to a Stalker's Bow (e.g. *Yew Bow of the Stalker*)
* The item stores `PropertyBool.IsStalkersbow = true` on the world object
* Drop chance and proc variables are runtime-adjustable via `@lootconfig` (`stalker.*` keys)

#### First Strike Bonus (proc)
* If the current hit is the **first registered hit against this target** (attacker GUID not yet in target `DamageHistory`), there is an **N% proc chance** (rolled at loot time, 1–4%, stored as `PropertyFloat.StalkerFirstHitProc`) for a **+25–50% bonus damage** multiplier (stored as `PropertyFloat.StalkerFirstHitBonus`)
* Hook point: `ProjectileCollisionHelper` after `DamageEvent.CalculateDamage`, before `TakeDamage`
* When the proc fires: `+N [Stalker's Bow] first strike` in combat chat
* **Long description** example: *"This bow is strung for the killing shot — the first arrow loosed at an unsuspecting target has a 3% chance to strike true for an additional N% damage."*
* **Planned `@lootconfig` keys:** `stalker.drop`, `stalker.tier`, `stalker.procmin`, `stalker.procmax`, `stalker.bonusmin`, `stalker.bonusmax`

### Breacher's Crossbows
* **5% of T6+ crossbow loot drops** are converted to a Breacher's Crossbow (e.g. *Steel Crossbow of the Breacher*)
* The item stores `PropertyBool.IsBreachersXbow = true` on the world object
* Drop chance and pierce % are runtime-adjustable via `@lootconfig` (`breacher.*` keys)

#### Armor Pierce (always-on)
* Every hit applies bonus pierce damage = `DamageMitigated × piercePct` — **no proc roll**, always fires when armor absorbs something
* Pierce % rolled at loot time (1–5%), stored as `PropertyFloat.BreaherPiercePct`
* Distinct from Fencer's Blade: the crossbow pierce is **deterministic** — rewards targeting heavily armored enemies without a luck requirement
* Hook point: `ProjectileCollisionHelper` — apply bonus after `TakeDamage` when `damageEvent.DamageMitigated > 0`
* When it fires: `+N pierce [Breacher's Crossbow]` in combat chat
* **Long description** example: *"This crossbow is built to punch through plate — each bolt recovers N% of what the target's armor absorbs as direct piercing damage."*
* **Planned `@lootconfig` keys:** `breacher.drop`, `breacher.tier`, `breacher.piercemin`, `breacher.piercemax`

### Reaper's Atlatls
* **5% of T6+ atlatl loot drops** are converted to a Reaper's Atlatl (e.g. *Ivory Atlatl of the Reaper*)
* The item stores `PropertyBool.IsReapersAtlatl = true` on the world object
* Drop chance and proc variables are runtime-adjustable via `@lootconfig` (`reaper.*` keys)

#### Kill Feed (proc)
* When a killing blow is landed (`!target.IsAlive` after `TakeDamage`), there is an **N% proc chance** (rolled at loot time, 3–8%, stored as `PropertyFloat.ReaperKillProc`) to restore **X% of the wielder's max health** (stored as `PropertyFloat.ReaperKillHealPct`)
* Heal % rolled at loot time (5–15%)
* Hook point: `ProjectileCollisionHelper` — check `!target.IsAlive` after the `TakeDamage` call
* When the proc fires: `+N health [Reaper's Atlatl] kill` in combat chat
* **Long description** example: *"This atlatl is bound to the hunt — finishing a kill has a 5% chance to surge N health back into the wielder."*
* **Planned `@lootconfig` keys:** `reaper.drop`, `reaper.tier`, `reaper.procmin`, `reaper.procmax`, `reaper.healmin`, `reaper.healmax`

### Elemental Unarmed Weapons
* **5% of magical elemental unarmed loot drops** (cestus, katar, nekode — acid, electric, fire, frost variants) receive a cast-on-strike proc
* Drop chance and proc rate range are runtime-adjustable via `@lootconfig` (`unarmed.drop`, `unarmed.procmin`, `unarmed.procmax`)
* Each qualifying weapon rolls a **random proc rate between 1–5%** at loot time; the exact value is reflected in the long description

#### Element Details
| Element | Name Suffix | Icon Overlay | UiEffect | Proc Spell |
|---|---|---|---|---|
| Fire | *of Cinders* | `0x06005B3A` | `Fire` | `FlameBlast3` |
| Frost | *of Rime* | `0x06005B3E` | `Frost` | `FrostBolt3` |
| Acid | *of Vitriol* | `0x0600667B` | `Acid` | `AcidBlast3` |
| Lightning | *of Tempests* | `0x06006680` | `Lightning` | `LightningBlast3` |

* The proc fires through the standard `TryProcEquippedItems` → `TryProcItem` path — no custom combat code required
* **Long description** example: *"This weapon crackles with frost energy — each strike has a 3% chance to discharge a frost blast."*

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
