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

### Recent Patch Notes (Expansion Hybrid — Nomad Unarmed, Procs, Bonus Stats, Pet QoL)
Adapted from selected features in [ACE.BaseMod / Samples / Expansion / Features](https://github.com/aquafir/ACE.BaseMod/tree/master/Samples/Expansion/Features) and integrated directly into the DerpACE server (no runtime Harmony patches). Every feature is **toggleable at runtime** via `PropertyManager` and tuned for the Nomad/unarmed playstyle.

#### New PropertyManager toggles & balance knob (`Source/ACE.Server/Managers/PropertyManager.cs`)
| Property | Default | Purpose |
| --- | --- | --- |
| `unarmed_weapon_surrogate_enabled` | `true` | When the player is truly unarmed, the relevant glove or boot acts as the swing's weapon (stats, imbues, slayer/crit/resistance mods, and proc spell all flow through `DamageEvent`). |
| `unarmed_combo_streaks_enabled` | `true` | Adds an additive hit/kill-streak damage layer on top of the existing combo system. |
| `bonus_stats_enabled` | `true` | Enables in-memory bonus stat storage on `Creature` (attributes, vitals, skills). |
| `proc_on_attack_enabled` | `true` | Every attacker-equipped proc-bearing item rolls on attack (not just the swing weapon + aetheria). |
| `proc_on_hit_enabled` | `true` | Every defender-equipped proc-bearing item rolls when hit (not just the cloak). |
| `pet_attack_selected_enabled` | `true` | The combat pet biases `FindNextTarget` to the owner's currently selected target. |
| `pet_message_damage_enabled` | `true` | Pet hits are echoed to the owner: `[Pet] Fluffy hits Drudge for 47 Slash damage.` |
| `pet_auto_recover_enabled` | `true` | After the pet's target dies/becomes invalid, the pet waits a short cooldown before re-acquiring (less twitchy mid-animation snap-to-next-mob). |
| `unarmed_damage_scalar` (double) | `0.75` | Scales the **bonus portion** of combo + streak damage. Tuned slightly below finesse overall while keeping the combo loop fun. |

#### Nomad-style true unarmed (`Source/ACE.Server/WorldObjects/Player_Unarmed.cs` — new)
* `IsNomadUnarmed` — returns `true` only when the player has nothing in `MeleeWeapon | MissileWeapon | TwoHanded | Held` slots. **Shields are explicitly allowed** for blocking / tank mechanics.
* `GetUnarmedSurrogateWeapon()` — returns the equipped boot when `PowerLevel >= KickThreshold` (kick zone) or the equipped glove otherwise. Mirrors the boundary `Player_Melee.GetSwingAnimation()` already uses, so the surrogate stays perfectly in sync with the resolved `AttackType`.

#### Surrogate weapon integration into combat
* `Source/ACE.Server/Entity/DamageEvent.cs` — when the swing has no real weapon and the attacker is a player, the surrogate is promoted to `Weapon` so its slayer mod, crit mods, imbues, resistance mods, and `IgnoreMagicArmor` / `IgnoreMagicResist` all flow through damage calc naturally.
* `Source/ACE.Server/WorldObjects/Player_Melee.cs` — `Attack()` falls back to the surrogate when `GetEquippedMeleeWeapon()` returns `null`, so proc rolls (`TryProcEquippedItems`) use the surrogate's `ProcSpell` on the swing.

#### Combo system hybrid (`Source/ACE.Server/Entity/UnarmedComboSystem.cs` + `Source/ACE.Server/WorldObjects/Player_Combat.cs`)
* **Strict nomad gate**: `RecordAttack` is only called when `IsNomadUnarmed` is true. Equipping any disqualifying weapon instantly stops combo tracking.
* **Damage scalar**: combo bonus damage (`damage * (multiplier - 1)`) is multiplied by `unarmed_damage_scalar` before being applied. Combos still fire all their flavor/effects; only the bonus damage is tuned.
* **New streak layer** (adapted from `FakeCombo`):
  * `OnUnarmedHit(bool killed)` — increments hit streak (cap 10) and, on kill, kill streak (cap 10).
  * `OnUnarmedMiss()` — resets hit streak on evade / lifestone protection. Kill streak decays on its own 30-second timer.
  * `GetStreakDamageBonus()` — returns `(hitStreak * 0.02) + (killStreak * 0.05)`, scaled by `unarmed_damage_scalar`. Applied additively on top of combo damage.

#### In-memory bonus stats (`Source/ACE.Server/WorldObjects/Creature_BonusStats.cs` — new)
* Lazily allocated per-creature dictionaries for `PropertyAttribute`, `PropertyAttribute2nd`, and `Skill` bonuses.
* `GetBonus(...)`, `SetBonus(...)`, `IncBonus(...)`, `ClearBonusStats()`.
* Wired into:
  * `CreatureAttribute.StartingValue` — adds `GetBonus(Attribute)` (clamped at 0).
  * `CreatureVital.StartingValue` — adds `GetBonus(Vital)` (clamped at 0).
  * `CreatureSkill.InitLevel` — adds `GetBonus(Skill)` (clamped at 0).
* **Logout-resetting by design**: storage is instance-local on the `Creature`, so bonuses naturally vanish on logout / despawn (matches the "fun temporary buffs" intent without persisting power creep).

#### Proc expansion
* `Source/ACE.Server/WorldObjects/WorldObject_Combat.cs` — `TryProcEquippedItems` now, when `proc_on_attack_enabled` is true, iterates every equipped item with a proc spell on the attacker and rolls each (excluding items already rolled: `this`, the swing weapon, and the attacker itself). When toggled off, retail behavior (weapon + aetheria) is preserved exactly.
* `Source/ACE.Server/Entity/Cloak.cs` — new helper `Cloak.TryProcAllEquipped(defender, attacker, equippedCloak, damage_percent)`:
  * Always runs the original cloak proc path (vanilla behavior preserved).
  * When `proc_on_hit_enabled` is true, iterates every other equipped item on the defender and runs `RollProc` + `HandleProcSpell` for each one with a proc spell. Items without an `ItemLevel` fail `RollProc` naturally, so generic jewelry/armor is a safe no-op.
* All four `Cloak.TryProcSpell` call sites have been routed through the new helper: `Player_Combat.cs`, `SpellProjectile.cs`, and two paths in `WorldObject_Magic.cs` (boost + drain).

#### Pet quality of life (`Source/ACE.Server/WorldObjects/CombatPet.cs` + `Source/ACE.Server/WorldObjects/Monster_Melee.cs`)
* **1-pet limit**: already enforced by retail `CurrentActivePet` logic — no additional change needed; passive/combat pet stowing rules continue to work.
* **PetAttackSelected**: `FindNextTarget` checks the owner's `HealthQueryTarget`; if that GUID is in the nearby-attackable set, the pet targets it instead of the nearest mob. Falls back to nearest when no valid selection exists.
* **PetMessageDamage**: when the attacker is a `CombatPet` with a `Player` owner, the owner receives a `CombatSelf` chat line each time the pet deals damage, including target name, damage amount, and damage type.
* **PetAutoRecover (less twitchy)**: `HandleFindTarget` defers re-acquisition by 0.75 s after the current target dies / becomes invalid. The first tick noticing the loss arms the cooldown and clears `AttackTarget`; subsequent ticks wait out the timer before calling `FindNextTarget()`. Prevents the pet from instantly whipping to the next mob mid-animation.

### Recent Patch Notes (May 17, 2026 — ClothingMod wiring & content pipeline)
* **CustomClothingManager — startup wiring hardened** (`Source/ACE.Server/Managers/CustomClothingManager.cs`):
  * `Initialize()` now registers `DatDatabase.ClothingTableMergeHook = MergeCustom` **before** calling `LoadAll()`, so any `ReadFromDat<ClothingTable>` racing with init still goes through the merge.
  * After loading, `Initialize()` calls `ClearCache()` once to flush any `ClothingTable` entries cached during DAT preload, guaranteeing the override is applied on the first post-init read.
  * `LoadAll()` now logs:
    * a warning if `Data/CustomClothingBase/` is missing,
    * an info line if zero JSON files are present,
    * a debug line per loaded `ClothingTable` id,
    * a final `Loaded N/M custom clothing table(s) from <path>` summary.
* **CustomClothingBase content now copies to the build output** (`Source/ACE.Server/ACE.Server.csproj`):
  * Added `<None Include="Data\CustomClothingBase\**\*.json" CopyToOutputDirectory="PreserveNewest" />`.
  * JSON overrides dropped into `Source/ACE.Server/Data/CustomClothingBase/` are now copied to `bin/x64/<cfg>/net10.0/Data/CustomClothingBase/` automatically on build, so they're visible to the running server.
* **Developer commands for the clothing override pipeline** (`Source/ACE.Server/Command/Handlers/DerpACEClothingBaseCommands.cs`):
  * `@cbexport <id> [label]` — exports a `ClothingBase` entry from `portal.dat` to `Data/CustomClothingBase/<id>[_label].json`. ID accepts hex (`0x10001234`) or decimal. Example: `@cbexport 0x10001234 male plate` → `10001234_male_plate.json`.
  * `@cbreload` — reloads every JSON file from `Data/CustomClothingBase/` and flushes the `ClothingTable` cache so edits take effect without a server restart.
  * `@cbclear` — clears only the `ClothingTable` entries from the portal.dat file cache, forcing a fresh re-read on next use.
  * All three commands require `AccessLevel.Developer`.
* **Authoring workflow**:
  1. Export an existing entry with `@cbexport 0x10001234`, or hand-author a JSON file (must contain `Id`, plus the `ClothingBaseEffects` and/or `ClothingSubPalEffects` you want to override).
  2. Save it under `Source/ACE.Server/Data/CustomClothingBase/<id>.json`.
  3. Rebuild (or copy to the running server's `bin/.../Data/CustomClothingBase/`).
  4. Run `@cbreload` in-game, or restart the server. Look for `CustomClothingManager: Loaded N/M custom clothing table(s)` in the server log.
* **Merge semantics** (`MergeCustom`): the override upserts into the live `ClothingTable` — entries present in the JSON replace the portal.dat values, entries omitted from the JSON are left alone. Brand-new `Id`s that don't exist in `portal.dat` are returned as fresh `ClothingTable` instances so completely custom items can be added.

### Recent Patch Notes (May 17, 2026)
* **Standard Ironman — Mana Conversion auto-train for magic primaries** (`IronmanFactory.RollSkills`):
  * When the rolled primary weapon skill is **Life Magic, Void Magic, or War Magic**, `ManaConversion` is now auto-trained **immediately after the weapon train/spec step**, before the rest of the primary pool is shuffled and rolled.
  * Its credit cost is pulled from `SkillBase.TrainedCost` and deducted via `Player.TrainSkill(...)` so the remaining shuffled rolls work off the **reduced** credit pool.
  * Mana Conversion is then removed from the shuffled primary pool to prevent a double spend. If credits are insufficient (very rare), the player is notified and the rest of the plan continues without MC.
* **Ironman Nomad — element progression** (`IronmanFactory.GrantNextNomadElement`):
  * Starter gauntlets and shoes now **share a single element** instead of rolling independently, so element collection is clean.
  * Hooked into `CheckIronmanLevelGrants`: on every **even level from 2 through 14**, a nomad is granted a matched gauntlet/shoe pair of a random element they don't yet own.
  * By **level 14** a nomad has collected **all 7 non-void elements** (Slash, Pierce, Bludgeon, Fire, Cold, Acid, Electric). The grant no-ops once the full set is collected.
  * Each new pair sends a `[Nomad] You have unlocked a new element: <Name>!` broadcast message.
* **Ironman Nomad — gauntlet/shoe inscription visibility fix**:
  * `PropertyBool.Inscribable` is now set to **`true`** on nomad gauntlets and shoes. With `false` the client was hiding the inscription text and the player couldn't see the stamped damage stats and proc info.
* **Ironman Nomad — custom unarmed procs (Cleave Flurry / Healing Strike)**:
  * Every nomad gauntlet/shoe now rolls one of two custom procs at creation time, stamped onto the item as `PropertyInt.NomadProcType` + `PropertyFloat.NomadProcChance` + `PropertyFloat.NomadProcMagnitude`. The proc description is appended to the M. Stranger inscription so the player can read exactly what the item does.
  * **Cleave Flurry** (type 1): ~8–15% chance on Punch/Kick hit to unleash **2–4 fast extra strikes** at 30–45% damage each, using the item's stamped damage type. Uses a `_nomadProcInProgress` recursion guard so the extra strikes don't recursively proc themselves. Splatter VFX per hit and a `Cleave Flurry! N extra strikes for X damage` combat-self message.
  * **Healing Strike** (type 2): ~8–15% chance on Punch/Kick hit to heal the wielder for **100–110% of damage dealt** (1–10% above the damage you hit for). Uses `UpdateVitalDelta(Health, ...)` (caps at MaxHealth), records via `DamageHistory.OnHeal`, plays `HealthUpRed` VFX, and sends a `Healing Strike! +X health from <target>` combat-self message.
  * Proc evaluation runs in `Player_Combat.DamageTarget`, gated on `AttackType == Punch || Kick` and pulled from `HandArmor` for Punch / `FootArmor` for Kick — only the nomad's stamped gauntlets/shoes trigger.
  * New persistent properties added: `PropertyInt.NomadProcType = 9030`, `PropertyFloat.NomadProcChance = 9026`, `PropertyFloat.NomadProcMagnitude = 9027`.

### Recent Patch Notes (May 2026 — Ironman Nomad)
* **Ironman Nomad submode** (`/ironman nomad`):
  * Players cannot wield weapons or casters of any kind. Wield attempts are rejected with *"Nomads cannot wield weapons or casters."*
  * Attributes roll **randomly between 10 and 100** per stat (instead of the standard 100/46 split).
  * Light Weapons is forced as the rolled primary skill (trained + specialized) and Arcane Lore is specialized.
  * All damage comes from elemental gauntlets and shoes granted on commit.
    * Starter pair are leather gauntlets (`WCID 56`) and leather boots (`WCID 115`) rerolled with `UnarmedBaseDamage`, `UnarmedDamageType`, and `UnarmedDamageVariance` so the existing unarmed-armor pipeline picks them up.
    * Each piece independently rolls one damage type from **Slash, Pierce, Bludgeon, Fire, Cold, Acid, Electric**.
    * Renamed for clarity (e.g. *Flame Nomad Gauntlets*, *Lightning Nomad Shoes*).
    * **Inscribed by M. Stranger** — the inscription lists the base damage, variance, and element so the player can read exactly what the item does. Marked non-`Inscribable` so the text cannot be overwritten.
  * Without armor (clothes only), nomads have a **natural body AL of 450** averaged across all damage types.
  * When a nomad wears `ItemType.Armor`, the armor layer's effective AL contribution is **halved** because nomads don't know how to wear it.
  * Persisted via `PropertyBool.IsIronmanNomad = 9039`; mode title is set to `NOMAD`.
  * `/ironman nomad` opens a 30-second confirmation window (same UX as `/ironman on`); `/ironman confirm` finalizes either standard or nomad based on which was requested.
* **Ironman leaderboard now shows Lives and Status** (`/ironmantop` / `/ironman top`):
  * New `Lives` column reads `PropertyInt.HardcoreLives` per player.
  * New `Status` column reads `DEAD` (lives ≤ 0), `NOMAD`, or `ALIVE`.
* **Biota integrity hardening** — addresses recurring duplicate-key errors (`biota_properties_int.PRIMARY`) caused by orphaned child rows + recycled dynamic GUIDs:
  * `ShardDatabase.SaveBiota` and `ShardDatabaseWithCaching.SaveBiota` now purge stale `biota_properties_*` rows before inserting a brand-new biota.
  * `ShardDatabaseOfflineTools.RunStartupCleanup()` runs at server boot (after `DatabaseManager.Start()`, before `GuidManager.Initialize()`) and purges all `IsDeleted` characters plus orphan rows across every `biota_properties_*` table.
  * Ironman/Hardcore final-death cleanup now waits for `PlayerManager.GetOnlinePlayer(charId) == null` before calling `PurgeCharacter(...)`, eliminating the logout-finalization race that NRE'd in `SwitchPlayerFromOnlineToOffline`.

### Recent Patch Notes (May 2026)
* Added server-wide activation broadcasts when players commit to modes:
  * Ironman: `[IRONMAN] <name> has taken the Ironman path. There is no turning back!`
  * Hardcore: `[HARDCORE] <name> has entered Hardcore mode. One life remains.`
* Added server-wide death/fall broadcasts:
  * Ironman deaths announce killer + victim level with mild ridicule flavor text.
  * Hardcore deaths announce killer + victim level.
* Expanded Ironman command UX:
  * `/ironman` now shows an Ironman help menu for committed players.
  * `/ironman char` shows progression details.
  * `/ironman topkillers` is available through `/ironman` subcommand routing.
* Ironman progression display improvements:
  * Milestones now show unlock level.
  * Specialized skills are marked with `[Spec]`.
* Added Global Kill Quest system:
  * Rotates a server-wide kill target every 30 minutes.
  * Players can track progress with `/gquest`.
  * Completing the objective grants a 4x XP bonus based on kill XP earned toward the quest.
  * Quest expiry is enforced; late kills do not count after timer expiry.
  * End-of-quest wrap-up broadcast announces completion count before the next quest starts.
* Foci now allow mana stones in addition to scarabs and prismatic tapers.
* Added admin Ironman toggle command: `@ironmanmode on|off|toggle|status`.
* Added admin special-mob spawn command: `/cimob <vamp|thief|sim> <wcid or classname>`.

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
* **5% of T6+ épée / rapier / schlager loot drops** (`TreasureWeaponType.SwordMS`) are converted to a Fencer's Blade (e.g. *Obsidian Rapier of the Fencer*)
* The item stores `PropertyBool.IsFencerBlade = true` on the world object
* **Icon overlay:** `0x06002699`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.FencerArmorPiercePct` — 1–5% of the armor mitigation refunded as bonus damage when the pierce proc fires
  * `PropertyFloat.FencerArmorPierceProc` — 1–4% per-hit chance to fire pierce
  * `PropertyFloat.FencerDeflectChance` — 1–2% per-incoming-hit chance to deflect 10% of the damage back at the attacker

#### Armor Pierce (proc)
* On each hit, rolls `FencerArmorPierceProc`; on success adds `max(0, DamageMitigated) × FencerArmorPiercePct` as bonus damage
* Combat message: `+N pierce [Fencer's Blade]`

#### Deflect (proc)
* In `TakeDamage`, when struck by a Creature attacker while wielding a Fencer's Blade, rolls `FencerDeflectChance`
* On success, attacker takes `round(damageTaken × 0.10)` as `DamageType.Pierce`
* Player sees `[Fencer's Blade] Deflected! -N [AttackerName]` (PvE only)

### Ravager's Axes
* **5% of T6+ axe loot drops** (`TreasureWeaponType.Axe` and `TreasureWeaponType.TwoHandedAxe`) are converted to a Ravager's Axe (e.g. *Obsidian Axe of the Ravager*)
* The item stores `PropertyBool.IsRavagersAxe = true` on the world object
* **Icon overlay:** `0x06002878`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.RavagerBleedProc` — 2–5% per-hit chance to apply a bleed
  * `PropertyFloat.RavagerBleedPct` — fraction of the triggering hit (default 30–60%) dealt as total bleed; **two-handed axes get a `RavagerTwoHandMult` multiplier (default 1.5×) baked in at loot time**

#### Bleed (proc, DoT)
* When the proc fires, total bleed = `hit damage × RavagerBleedPct`, split evenly across `RavagerBleedTicks` ticks (default 3) at `RavagerBleedInterval` second intervals (default 2.0s)
* Implemented as an `ActionChain` on the wielder; each tick: re-checks `target.IsAlive`, deals `perTick` damage of the same type as the triggering hit, plays `SplatterMidLeftBack` on the target, and emits `-N bleed [TargetName] [Ravager's Axe]` in the combat chat channel
* On proc, an immediate announce: `[TargetName] is bleeding (+N) [Ravager's Axe]` (N = total bleed across all ticks)
* All values runtime-tunable via `@lootconfig` (`ravager.drop`, `ravager.tier`, `ravager.procmin`, `ravager.procmax`, `ravager.bleedmin`, `ravager.bleedmax`, `ravager.twohandmult`, `ravager.ticks`, `ravager.interval`)

### Warden's Mauls
* **5% of T6+ mace loot drops** (`TreasureWeaponType.Mace`, `MaceJitte`, and `TwoHandedMace`) are converted to a Warden's Maul (e.g. *Ebony Mace of the Warden*)
* The item stores `PropertyBool.IsWardensMaul = true` on the world object
* **Icon overlay:** `0x06002878`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.WardenConcussProc` — 4–8% per-hit chance to apply the concussion debuff
  * `PropertyFloat.WardenConcussPenalty` — flat defense-skill penalty (default 10–30; **two-handed maces get a `WardenTwoHandMult` multiplier (default 1.5×) baked in at loot time**)
  * `PropertyFloat.WardenConcussDuration` — debuff duration in seconds (default 5–10)

#### Concussion (proc, debuff)
* On proc, sets transient in-memory fields on the target `Creature`: `ConcussedUntil = now + duration` and `ConcussedPenalty = penalty`
* If the target is already concussed with an equal-or-stronger penalty, only the duration is refreshed (does not stack down)
* Penalty is subtracted from `effectiveDefense` in **both** `Creature.GetEffectiveDefenseSkill` (mob-side defense rolls) and `Player.GetTargetEffectiveDefenseSkill` (player-attacks-mob path), so attackers see a higher hit rate
* No spell/enchantment is created — the debuff is purely server-side state and does not appear on the target's enchantment bar
* Plays `HealthDownYellow` on the target when the proc fires
* Combat message: `crushes [TargetName]'s guard — -N defense skill for D sec [Warden's Maul]`
* All values runtime-tunable via `@lootconfig` (`warden.drop`, `warden.tier`, `warden.procmin`, `warden.procmax`, `warden.penaltymin`, `warden.penaltymax`, `warden.durationmin`, `warden.durationmax`, `warden.twohandmult`)

### Resolute Blades
* **5% of T6+ sword loot drops** (`TreasureWeaponType.Sword` and `TreasureWeaponType.TwoHandedSword`; fencer `SwordMS` is excluded) are converted to a Resolute Blade (e.g. *Obsidian Sword of Resolve*)
* The item stores `PropertyBool.IsResoluteBlade = true` on the world object
* **Icon overlay:** `0x06002860`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.ResoluteHealProc` — 25–50% per-critical-hit chance to heal the wielder
  * `PropertyFloat.ResoluteHealPct` — 2–5% of the crit damage restored as health
  * `PropertyFloat.ResoluteKillBurstPct` — fraction of MaxHealth/MaxStamina restored on a killing blow (default 10%; **two-handed swords get a `ResoluteTwoHandMult` multiplier (default 1.5×) baked in at loot time**)

#### Heal-on-Critical (proc)
* On every **critical hit**, rolls `ResoluteHealProc`; on success heals `damage × ResoluteHealPct` to the wielder via `UpdateVitalDelta(Health, …)`
* Only fires when the wielder is below max health (no overheal waste)
* Combat message: `+N health [Resolute Blade]`

#### Bloodthirst (killing blow)
* When the killing blow is delivered to a non-player target, restores `MaxHealth × ResoluteKillBurstPct` health and `MaxStamina × ResoluteKillBurstPct` stamina to the wielder
* Plays `HealthUpRed` particle effect on the wielder
* Combat message: `Bloodthirst! +N health, +N stamina [Resolute Blade]`
* Does **not** fire on PvP kills (avoids stacking exploits in PK fights)
* All values runtime-tunable via `@lootconfig` (`resolute.drop`, `resolute.tier`, `resolute.procmin`, `resolute.procmax`, `resolute.healmin`, `resolute.healmax`, `resolute.killburst`, `resolute.twohandmult`)

### Polebreaker Staves
* **5% of T6+ staff loot drops** (`TreasureWeaponType.Staff`) are converted to a Polebreaker (e.g. *Obsidian Staff of the Polebreaker*)
* The item stores `PropertyBool.IsPolebreakerStaff = true` on the world object
* **Icon overlay:** `0x06002699`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.PolebreakerStackBonus` — bonus damage fraction added per stack (default 1–3% per stack)
  * `PropertyFloat.PolebreakerMaxStacks` — maximum stack count (default 4–6)

#### Consecutive Hit Escalation (rhythm)
* Tracks a hit streak against the same target via transient `LastPolebreakerTargetGuid` + `PolebreakerStackCount` fields on the player (no enchantment, resets on logout/restart)
* Each consecutive hit on the same target adds one stack (capped at the rolled `PolebreakerMaxStacks`); the *next* hit's bonus damage is `damage × StackBonus × (currentStack − 1)` — so the 1st hit has no bonus, the 2nd has +StackBonus, etc.
* Stacks reset to 1 when you switch to a different target, and reset to 0 if you score a hit with any non-Polebreaker weapon
* Combat message (only when stacks ≥ 2 and bonus damage applied): `[Polebreaker] +N (xS)` where N = bonus damage and S = current stack count
* All values runtime-tunable via `@lootconfig` (`polebreaker.drop`, `polebreaker.tier`, `polebreaker.stackmin`, `polebreaker.stackmax`, `polebreaker.maxstackmin`, `polebreaker.maxstackmax`)

### Stalker's Bows
* **5% of T6+ bow loot drops** (`TreasureWeaponType.Bow`) are converted to a Stalker's Bow (e.g. *Yew Shortbow of the Stalker*)
* The item stores `PropertyBool.IsStalkersBow = true` on the world object
* **Icon overlay:** `0x06002699`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.StalkerFirstStrikeProc` — chance the opening shot procs (default 30–50%)
  * `PropertyFloat.StalkerFirstStrikeBonus` — bonus damage fraction on a successful first strike (default +25–50%)

#### First Strike (proc, opening shot)
* Hooks into `Player.DamageTarget` (also fires for missile attacks via `ProjectileCollisionHelper`)
* Fires only when `target.DamageHistory.TotalDamage` does **not** yet contain the attacker's `Guid` — i.e. this is the first hit *this* player has landed on the target this encounter
* On proc, adds `damage × StalkerFirstStrikeBonus` as bonus damage *before* the hit is applied
* Combat message: `+N [Stalker's Bow] first strike`
* Naturally resets when the target dies (DamageHistory cleared on respawn) or when a different player is the first to engage; opening a new fight against a fresh target re-enables the proc
* All values runtime-tunable via `@lootconfig` (`stalker.drop`, `stalker.tier`, `stalker.procmin`, `stalker.procmax`, `stalker.bonusmin`, `stalker.bonusmax`)

### Breacher's Crossbows
* **5% of T6+ crossbow loot drops** (`TreasureWeaponType.Crossbow`) are converted to a Breacher's Crossbow (e.g. *Steel Heavy Crossbow of the Breacher*)
* The item stores `PropertyBool.IsBreachersCrossbow = true` on the world object
* **Icon overlay:** `0x06002878`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.BreacherArmorIgnoreChance` — chance per shot to completely ignore the target's armor mitigation (default 5–15%)

#### Armor Bypass Proc
* Small chance per shot to trigger armor bypass, allowing the full pre-mitigation damage to pass through
* When triggered: `Damage = DamageBeforeMitigation` (armor entirely bypassed for that one hit)
* Combat message: `+N armor bypass [Breacher's Crossbow]` (N = the damage that armor blocked)
* Fits the crossbow archetype: rare, dramatic armor-piercing moments instead of steady chip damage
* All values runtime-tunable via `@lootconfig` (`breacher.drop`, `breacher.tier`, `breacher.ignorechancemin`, `breacher.ignorechancemax`)

### Reaper's Atlatls
* **5% of T6+ atlatl loot drops** (`TreasureWeaponType.Atlatl`) are converted to a Reaper's Atlatl (e.g. *Obsidian Atlatl of the Reaper*)
* The item stores `PropertyBool.IsReapersAtlatl = true` on the world object
* **Icon overlay:** `0x06002860`
* Stats are rolled per-weapon at loot time and stored on the WO:
  * `PropertyFloat.ReaperKillProc` — chance the heal procs on a killing blow (default 30–60%)
  * `PropertyFloat.ReaperKillHealPct` — fraction of MaxHealth restored on proc (default 5–15%)

#### Kill-Fed Sustain (proc)
* Hooks into `Player.DamageTarget` after the standard hit-resolution block (same path as Resolute Bloodthirst); fires when `!target.IsAlive && targetPlayer == null`
* On proc, restores `MaxHealth × ReaperKillHealPct` health to the wielder via `UpdateVitalDelta(Health, …)`
* Plays `HealthUpRed` particle effect on the wielder
* Combat message: `Reaped! +N health [Reaper's Atlatl]`
* Only fires when the wielder is below max health (no overheal waste); does **not** fire on PvP kills (avoids stacking exploits in PK fights)
* All values runtime-tunable via `@lootconfig` (`reaper.drop`, `reaper.tier`, `reaper.procmin`, `reaper.procmax`, `reaper.healmin`, `reaper.healmax`)

### Armor Bane Roll Rates
* Bumps the per-bane roll chance in `ArmorSpells.Roll` so banes show up more often on loot
* **Normal armor** (leather, chain, plate, etc.): per-bane chance raised from retail's `0.15` to `ArmorBaneChanceNormal` (default `0.20` — slight bump)
* **Covenant armor** (`TreasureArmorType.Covenant`, including covenant shields): per-bane chance raised to `ArmorBaneChanceCovenant` (default `0.60` — significant bump, often 3+ banes per piece)
* Applies to all 7 banes (Blade / Piercing / Bludgeon / Flame / Frost / Acid / Lightning); `Impenetrability1` keeps its original `1.00` chance
* `ArmorSpells.Roll` now takes the `TreasureRoll` so it can branch on armor type; the parameterless overload is preserved for legacy callers
* Runtime-tunable via `@lootconfig` (`armor.banenormal`, `armor.banecovenant`)

### Mob Modifiers
Rare "affix" variants applied to freshly-spawned hostile mobs (think Diablo rare-pack prefixes). Stage 1 ships **Vampiric** and **Thieving**; Warden / Nocturnal / Assassin slots are reserved (`PropertyBool 9025/9026/9027`) for follow-up stages.

* Hooked into `GeneratorProfile.Spawn()` immediately after `WorldObjectFactory.CreateNewWorldObject(...)` and before `EnterWorld()` — `MobModifierFactory.TryApplyModifiers(wo)` rolls each enabled modifier independently so multiple can stack on one mob (e.g. *Vampiric Thieving Drudge*)
* **Eligibility gate** (all must pass):
  * `MobModifierEnabled` master switch is true
  * Object is a `Creature`, not a `Player`, not a `Pet`, not an `IsNPC` (no vendors / advocates)
  * `Attackable == true` OR `TargetingTactic != None` (mirrors `Monster.IsMonster`)
  * `DeathTreasure?.Tier ?? (Level/10) >= MobModifierMinTier` (default T5)
* All modifier flags + transient state are **in-memory only** — a server restart resets every spawned mob to vanilla
* Visual indicator is the **renamed creature** only (e.g. *Vampiric Drudge*); no spawn broadcast or particle
* Master toggles via `@lootconfig` (`mobmod.enabled`, `mobmod.tier`)

#### Vampiric (lifesteal on hit)
* Per-spawn chance `VampiricMobChance` (default `0.02`)
* On spawn: `PropertyBool.IsVampiricMob = true`, `PropertyFloat.VampiricLifestealPct` rolled between `VampiricLifestealMin..Max` (default 5–15%), name prepended with `"Vampiric "`
* **Visual tells:** `ObjScale` is increased by `+0.5` (a Vampiric mob is roughly half a unit larger than its base form) and the creature is shifted toward red — `PaletteTemplate = Red` and `Shade = 1.0` push palette-set-driven creatures to their reddest variant (no-op on creatures whose appearance is pure CSetup/AnimPart, but the size bump always reads)
* On every successful hit on a player (hooked in `Player.TakeDamage` after `DamageHistory.Add`), the mob is healed by `round(damageDealt × VampiricLifestealPct)` via `UpdateVitalDelta(Health, ...)`; only fires if mob is below max HP
* Plays `HealthUpRed` particle on the mob; victim sees `"<Mob> drains N health from you. [Vampiric]"` in CombatEnemy chat (squelch-aware)
* Tunable: `vampiric.chance`, `vampiric.lifestealmin`, `vampiric.lifestealmax`

#### Thieving (pickpocket tradenotes)
* Per-spawn chance `ThiefMobChance` (default `0.02`)
* On spawn: `PropertyBool.IsThiefMob = true`, name prepended with `"Thieving "`
* Adds three transient fields to `Creature`: `StolenTradeNoteWcid`, `StolenTradeNoteAmount`, `StolenFromGuid`
* On hit (same hook as Vampiric), if mob isn't already holding a stolen stack, rolls `ThiefStealProc` (default `0.10`):
  * Picks the **smallest tradenote stack** from the victim's inventory (any item with `ItemType.PromissoryNote` — covers all retail denominations 100→250k *and* any custom tradenotes), ordered by lowest `StackSize × Value`
  * `TryRemoveFromInventoryWithNetworking(...)` → `Destroy()`; the WCID + stack size are stored on the mob
  * Plays `HealthDownYellow` on the player; chat: `"Pickpocketed! <Mob> stole a tradenote stack (N). Kill it to recover. [Thief]"`
* On death (`Creature.OnDeath` after XP grant), if the mob is holding a stolen stack:
  * Recreates the tradenote via `WorldObjectFactory.CreateNewWorldObject(wcid)`, sets the original `StackSize`
  * Auto-credits the **killing-blow player** (whoever the `lastDamager` is — not necessarily the original victim) via `TryCreateInInventoryWithNetworking(...)`
  * Falls back to dropping on the ground at the corpse if the killer's pack is full
  * Player sees: `"You recover N stolen tradenotes from <Mob>. [Thief]"`
* On death (regardless of whether anything was stolen), a Thieving mob has a `ThiefChestDropChance` (default `0.50`) chance to **spawn a Chest of Tradenotes** (WCID `80524`, configurable via `ThiefChestWcid`) on the ground at the mob's death location — separate from the corpse so it can't be looted as part of normal death-treasure. The chest auto-despawns after `ThiefChestDespawnSeconds` (default `30`) via a queued `ActionChain` → `Destroy()` (set to `0` to disable).
* Tunable: `thiefmob.chance`, `thiefmob.proc`, `thiefmob.chestchance`, `thiefmob.chestwcid`, `thiefmob.chestdespawn` (renamed from `thief.*` to avoid collision with the existing Thief's Dagger keys)

#### Simulacrum (player doppelgänger)
* **Restricted to mobs with `CreatureType.Simulacrum` (59)** — every other mob type silently skips the modifier
* **Always applies** to every Simulacrum-typed spawn (no per-spawn chance roll, and the master tier gate is bypassed so even low-level Simulacrum mobs clone). `SimulacrumMobChance` is retained only as a kill-switch — set it to `0` to disable globally
* At spawn time, picks a random online `Player` whose `Location.LandblockId` matches the mob's spawn landblock; if no players are present in that landblock, the modifier silently skips and the mob spawns vanilla
* When a target is chosen, the mob is reskinned to look exactly like that player (same path used by `Creature.CreateCorpse` for player corpses):
  * Copies `SetupTableId`, `MotionTableId`, `PhysicsTableId`, `PaletteBaseDID`, `ClothingBase`, plus `PaletteTemplate` / `Shade` / `ObjScale` if the player has them
  * Snapshots `target.CalculateObjDesc()` and clones `AnimPartChanges`, `SubPalettes`, `TextureChanges` into the mob's `Biota.PropertiesAnimPart` / `PropertiesPalette` / `PropertiesTextureMap` collections — the existing "no equipped items" branch in `Creature.CalculateObjDesc` then renders the mob using that saved ObjDesc, so **armor / clothing / hair / face all carry over** (identical to how a player corpse displays the player's gear)
* Sets `PropertyBool.IsSimulacrumMob = true` (PropertyBool 9028)
* **Steals the player's name verbatim into the creature's `Name` field** — overrides any other modifier prefix (e.g. a Vampiric Simulacrum that procced both still ends up named after the player)
* All copied state is in-memory only; the original player is unaffected and a server restart wipes it
* Tunable: `simulacrum.chance` (kill-switch only — any value `> 0` enables, `0` disables)

#### Admin Spawn Helper
* Added `/cimob <vamp|thief|sim> <wcid or classname>` for admins to spawn a creature and force-apply a specific modifier without RNG.
* `sim` follows the same eligibility rules as normal Simulacrum logic (requires Simulacrum creature type and a nearby player in the same landblock).

### Ironman Mode
A hardcoded port of [aquafir's Ironman BaseMod](https://github.com/aquafir/ACE.BaseMod/tree/master/Samples/Ironman). Players opt in with a chat command; the choice is **irreversible** for the lifetime of the character.

#### Commands

| Command | Access | Description |
|---|---|---|
| `/ironman` | Player | If already an Ironman: show skill plan status. Otherwise: show usage. |
| `/ironman on` | Player | Begin commitment — prints a warning and opens a 30-second confirmation window. Only available at level 10 or below. |
| `/ironman nomad` | Player | Begin **NOMAD Ironman** commitment — no weapons or casters, unarmed damage via elemental gauntlets/shoes, natural AL 450 in clothes. Same 30-second confirm window. |
| `/ironman confirm` | Player | Finalize the conversion within the window. **Cannot be undone.** |
| `/ironman char` | Player | Show Ironman character progression milestones and unlocked skills. |
| `/ironman top` | Player | Show the Ironman leaderboard (top 10 players by creature kills). |
| `/ironman topkillers` | Player | Show the top 10 creatures that have killed the most Ironman players. |
| `/ironmantop` | Player | Shortcut for `/ironman top`. |
| `/ironmantopkillers` | Player | Show the top 10 creatures that have killed the most Ironman players. |
| `@ironmanmode on|off|toggle|status` | Admin | Live server toggle for Ironman opt-in availability. |

> **Flow:** type `/ironman on`, read the warning, then type `/ironman confirm` within 30 seconds. If the window expires you must run `/ironman on` again.

* On commit, the character is rerolled and re-equipped:
  * Attributes wiped: one random primary attribute set to **100**, the others to **46**
  * All skills reset; a **level-milestone plan** is rolled:
    * One random **primary** skill (`TwoHandedCombat`, `MissileWeapons`, `WarMagic`, `VoidMagic`, `LightWeapons`, `HeavyWeapons`, `FinesseWeapons`) is trained + specialized immediately at no credit cost
    * A **secondary** skill is trained (and specialized if non-magic); `ManaConversion` if a magic primary was rolled
    * 2–4 random skills are flagged **at-creation** and trained ~2 s after commit (same session, no relog required)
    * Remaining skills are distributed across level milestones (5, 12, 20, 32, 50, 70, 100, 130, 150, 175, 200, 225, 250, 275) or marked **not obtainable**
    * Skills unlock automatically on level-up in real time (no relog); the client skill panel updates immediately
    * Skill credits are always shown as **0** to the player — the system handles all training automatically
  * Inventory wiped (every wielded + carried item destroyed)
  * Spellbook wiped, then a fixed low-level spell set learned (life/creature/item/war basics) after a short delay
  * Starter gear granted: Ironman-specific items based on the rolled primary skill, plus the standard new-character gear from `starterGear.json` for every skill the player has trained (including dual-wield bonus weapon)
  * Character name gets ` - IM` appended unless it already ends with that suffix
  * Quest flag `IronmanChallenge` stamped via `QuestManager`
  * `RadarColor` set to `Sentinel` (gold) so other players can identify Ironmen
* Hardcore lives:
  * `PropertyInt.HardcoreLives` set to `IronmanHardcoreStartingLives` (default 1)
  * On death, lives is decremented (gated by an `IronmanHardcoreSecondsBetweenDeaths` cooldown so back-to-back PK / accidents don't burn multiple lives)
  * On final death (lives ≤ 0): `Character.IsDeleted = true`, `DeleteTime` stamped, force log-off after 2 s, `PlayerManager.HandlePlayerDelete` + `ProcessDeletedPlayer`
  * Creature kills on Ironman players are recorded in `ironmanKillers.json` for the `/ironmantopkillers` leaderboard
* Ongoing restrictions (inlined into source — no Harmony):
  * **Wield gate** — `Player_Inventory.CheckWieldRequirements` rejects any item that isn't flagged `IsIronmanItem` with `WeenieError.YouCannotUseThatItem`
  * **Auto-tag** — `Player_Inventory.TryCreateInInventoryWithNetworking` flips `IsIronmanItem = true` on every item that successfully enters an Ironman's inventory; items with workmanship also get a ` [IM]` suffix appended to their name (e.g. `Ebony Sword [IM]`) so players can distinguish Ironman-bound gear at a glance (covers corpse loot, chest loot, vendor purchase, emote grants, etc.)
  * **Skill train/specialize lock** — `HandleActionTrainSkill` blocks spending skill credits to train new skills; `SkillAlterationDevice.VerifyRequirements` blocks Gems of Enlightenment (specialize) and Gems of Forgetfulness (lower/untrain). Raising already-trained skills with XP is unrestricted
  * **Allegiance** — `Player_Allegiance.IsPledgable` returns `false` if either party is an Ironman
  * **Fellowship** — `Player_Fellowship.FellowshipRecruit` blocks if either party is an Ironman
  * **External enchantments** — `WorldObject_Magic.CreateEnchantment` early-returns if the target is an Ironman and the caster is a different player who is not also an Ironman (self-buffs and item procs from the Ironman's own gear still work because the source resolves to the Ironman themselves)
* Persistent state (new properties):
  * `PropertyBool.IsIronman` (9029), `PropertyBool.IsHardcore` (9030), `PropertyBool.IsIronmanItem` (9031)
  * `PropertyInt.HardcoreLives` (9016)
  * `PropertyString.IronmanPlan` (9008) — serialized as `SkillName:level;...` where `0` = applied, `-1` = at-creation, `-2` = not obtainable, `>0` = level milestone
* Configuration (in `DerpACEConfig`):
  * `IronmanEnabled` (bool, default `true`) — master kill-switch for the `/ironman` command
  * `IronmanWelcomeMessage` (string)
  * `IronmanCreditsToPlanFor` (int, default 50)
  * `IronmanHardcoreStartingLives` (int, default 1)
  * `IronmanHardcoreSecondsBetweenDeaths` (float, default 7 days)
* Global announcements:
  * Ironman activation and Hardcore activation both broadcast server-wide.
  * Ironman and Hardcore deaths broadcast server-wide with killer + victim level context.

#### Ironman Nomad
A stricter Ironman submode for players who want a "monk-like" no-weapons playstyle. Entered with `/ironman nomad` + `/ironman confirm`. Stacks on top of standard Ironman + Hardcore — all of the base Ironman restrictions still apply.

* **Equipment**
  * Cannot wield any `MeleeWeapon`, `MissileWeapon`, `Caster`, or `MagicWieldable`. `Player_Inventory.CheckWieldRequirements` rejects them with *"Nomads cannot wield weapons or casters."*
  * Can wear armor and clothing, but armor effective AL is halved (see Armor below).
* **Attributes & skills**
  * `RollAttributesRandom(player)` — every attribute rolls 10–100 (instead of the standard 100/46 split).
  * Weapon skill is forced to **Light Weapons** (trained + specialized) — useful for the unarmed fist/foot attack skill check.
  * **Arcane Lore** is specialized in addition to being pre-trained.
  * Remaining milestone planning runs through the standard Ironman skill plan.
* **Unarmed damage (elemental gauntlets & shoes)**
  * On commit, the player is granted **leather gauntlets (WCID 56)** and **leather boots (WCID 115)** rerolled into unarmed damage sources.
  * The starter pair **share a single rolled damage type** from: **Slash, Pierce, Bludgeon, Fire, Cold, Acid, Electric** — so a nomad cleanly collects one new element per level-up grant.
  * On every **even level from 2 through 14**, the nomad is automatically granted a matched gauntlet/shoe pair of a random element they don't yet own (`IronmanFactory.GrantNextNomadElement`). By **level 14** all 7 elements are collected.
  * The rolled values are stored on each WO as:
    * `PropertyInt.UnarmedBaseDamage` (12 gauntlets / 10 shoes)
    * `PropertyInt.UnarmedDamageType` (= rolled `DamageType`)
    * `PropertyFloat.UnarmedDamageVariance` (0.50 gauntlets / 0.55 shoes)
  * The existing unarmed-armor pipeline (`Player.GetBaseDamageMod` + `Player.GetDamageType`) reads these properties automatically — Punch attacks pull from the gauntlets and Kick attacks pull from the shoes.
  * Renamed to surface the element (e.g. *Flame Nomad Gauntlets*, *Lightning Nomad Shoes*).
  * **Inscribed by M. Stranger** — `PropertyString.Inscription` lists the base damage, variance, element, and proc; `PropertyString.ScribeName = "M. Stranger"`; `PropertyBool.Inscribable = true` (required, otherwise the client hides the inscription text).
* **Unarmed procs (custom)**
  * Each gauntlet/shoe rolls one of two custom procs at creation time, stored on the WO as `PropertyInt.NomadProcType` + `PropertyFloat.NomadProcChance` + `PropertyFloat.NomadProcMagnitude`. The proc description is appended to the M. Stranger inscription.
  * **Cleave Flurry** (type 1): ~8–15% chance on Punch/Kick hit to unleash **2–4 fast extra strikes** at 30–45% damage each. Uses a recursion guard so the extra strikes don't fire their own procs. Splatter VFX per hit + `Cleave Flurry! N extra strikes for X damage [target]` message.
  * **Healing Strike** (type 2): ~8–15% chance on Punch/Kick hit to heal the wielder for **100–110% of damage dealt** (1–10% above the damage you hit for). Uses `UpdateVitalDelta(Health, ...)` (caps at MaxHealth) + `DamageHistory.OnHeal` + `HealthUpRed` VFX + combat-self message.
  * Evaluation lives in `Player_Combat.DamageTarget`, gated on `AttackType == Punch || Kick`, pulling proc properties from `HandArmor` for Punch and `FootArmor` for Kick.
* **Armor calculation** (`Creature_BodyPart.GetEffectiveArmorVsType`)
  * If the nomad has **no `ItemType.Armor` layers** equipped on a body part (clothes only or bare), the base AL for that body part is overridden to **450** with resistance `1.0` (average across all damage types).
  * If any armor layer is worn, that layer's effective AL contribution is multiplied by **0.5** — nomads don't know how to wear armor.
* **Persistent state**
  * `PropertyBool.IsIronmanNomad = 9039` is set on the player.
  * Mode title is set to `NOMAD` via `SetModeTitle`.
  * `IsHardcore` and `IsIronman` are also applied (nomad mode is a strict superset of standard Ironman).
* **Leaderboard integration**
  * `/ironmantop` shows a `Status` column reading `NOMAD` for nomad players, `DEAD` for any Ironman whose lives are exhausted, or `ALIVE` otherwise.
  * `Lives` column reads `PropertyInt.HardcoreLives` directly.

### Global Kill Quest
Server-wide rotating kill quest that gives all online players the same timed objective.

#### Commands

| Command | Access | Description |
|---|---|---|
| `/gquest` | Player | Shows current global quest target, required kills, your progress, and time remaining. |

#### Behavior
* A new quest rolls every 30 minutes and is announced globally.
* Quest objective is randomized from a curated creature pool with per-creature kill ranges.
* Progress is tracked per-player for the active quest epoch.
* Kills grant normal XP as usual; quest progress accumulates the XP earned on matching kills.
* On completion, player receives bonus XP equal to `4x` accumulated matching-kill XP (`XpType.Quest`).
* Expiry enforcement: once quest timer ends, additional kills do not count even before next tick roll.
* At rollover, previous quest wraps up with a global completion-count message, then the next quest is announced.
* Leaderboard data:
  * Player leaderboard (`/ironman top`) — live query over all online + offline players via `PlayerManager.GetAllPlayers()`, sorted by `CreatureKills` descending
  * Killer leaderboard (`/ironmantopkillers`) — persisted to `ironmanKillers.json` in the server exe directory; loaded at startup by `IronmanKillerTracker.Initialize()`, incremented on every Ironman player death caused by a non-player creature
* Notes / deviations from the source mod:
  * Appearance / heritage rerolling is **not** ported — that path mutates Biota directly and is fragile across DerpACE forks
  * The hardcore-death cooldown is tracked in a process-lifetime `ConcurrentDictionary` rather than a persistent property; on server restart the cooldown is fresh (acceptable trade-off)
  * Item-tagging uses an opt-in *auto-tag-on-pickup* model (any item that lands in an Ironman's inventory is tagged) rather than the source mod's per-source tagging patches; functionally equivalent for solo play and far simpler

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


***
## Full Command Reference

This is an auto-generated index of every in-game command registered via `[CommandHandler(...)]`. Player commands are typed with `/` (e.g. `/ironman`); staff/admin commands are typed with `@` (e.g. `@teleto`). The required access level for each command is shown by section.

For deeper documentation of DerpACE-specific systems (Defender's Shield, Ravager's Axe, Ironman, Hardcore, Loot Config, etc.), see the sections above.


#### Player

| Command | Description |
|---|---|
| `/acecommands` | Lists all commands. |
| `/acehelp` | Displays help. |
| `/aceversion` | Shows this server's version data |
| `/castmeter` | Shows the fast casting efficiency meter |
| `/config` | Manually sets a character option on the server.\nUse /config list to see a list of settings. |
| `/debugcast` | Shows debug information about the current magic casting state |
| `/fixbusy` | Attempts to remove the hourglass / fix the busy state for the player |
| `/fixcast` | Fixes magic casting if locked up for an extended time |
| `/gquest` | Show the current global kill quest status. |
| `/hardcore` | Toggle Hardcore self-found mode (IRREVERSIBLE). |
| `/house-select` | For characters/accounts who currently own multiple houses, used to select which house they want to keep |
| `/ironman` | Toggle Ironman mode (IRREVERSIBLE). |
| `/ironmantop` | Show the Ironman leaderboard (top players by mob kills). |
| `/ironmantopkillers` | Show the top 10 creatures that have killed the most Ironman players. |
| `/myquests` | Shows your quest log |
| `/objsend` | Force resend of all visible objects known to this player. Can fix rare cases of invisible object bugs. Can only be used once every 5 mins max. |
| `/passwd` | Change your account password. |
| `/pop` | Show current world population |
| `/reportbug` | Generate a Bug Report |

#### Advocate

| Command | Description |
|---|---|
| `@allstats` | Displays a summary of all server statistics and usage |
| `@attackable` | Sets whether monsters will attack you or not. |
| `@bestow` | Sets a character's Advocate Level. |
| `@gcstatus` | Displays a summary of server GC Information |
| `@landblockperformance` | Displays a summary of landblock performance statistics |
| `@landblockstats` | Displays a summary of landblock performance statistics |
| `@lbgroupstats` | Displays a summary of landblock group stats |
| `@remove` | Removes the specified character from the Advocate ranks. |
| `@serverperformance` | Displays a summary of server performance statistics |
| `@serverstatus` | Displays a summary of server statistics and usage |
| `@tele` | Teleports you(or a player) to some location. |

#### Sentinel

| Command | Description |
|---|---|
| `@adminvision` | Allows the admin to see admin-only visible items. |
| `@ban` | Bans the specified player account. |
| `@banlist` | Lists all banned accounts on this world. |
| `@boot` | Boots the character out of the game. |
| `@buff` | Buffs you (or a player) with all beneficial spells. |
| `@cloak` | Sets your cloaking state. |
| `@fellowbuff` | Buffs your fellowship (or a player's fellowship) with all beneficial spells. |
| `@finger` | Show the given character's account name or vice-versa. |
| `@gag` | Prevents a character from talking. |
| `@god` | Turns current character into a god! |
| `@home` | Teleports you to your sanctuary position. |
| `@mrt` | Toggles the ability to bypass housing boundaries |
| `@neversaydie` | Turn immortality on or off. |
| `@portal_bypass` | Toggles the ability to bypass portal restrictions. |
| `@run` | Temporarily boosts your run skill. |
| `@save` | Sets your sanctuary position or a named recall point. |
| `@telereturn` | Return a player to their previous location. |
| `@teleto` | Teleport yourself to a player |
| `@teletome` | Teleports a player to your current location. |
| `@unban` | Unbans the specified player account. |
| `@ungag` | Allows a gagged character to talk again. |
| `@ungod` | Returns character to a mortal state. |

#### Envoy

| Command | Description |
|---|---|
| `@crack` | Cracks the most recently appraised locked target. |
| `@delete` | Deletes the selected object. |
| `@gamecast` | Sends a world-wide broadcast. |
| `@heal` | Heals yourself (or the selected creature) |
| `@myiid` | Displays your Instance ID (IID) |
| `@regen` | Sends the selected generator a regeneration message. |
| `@rename` | Rename a character. (Do NOT include +'s for admin names) |
| `@smite` | Kills the selected target or all monsters in radar range if \ |
| `@time` | Displays the server's current game time. |
| `@trophies` | Shows a list of the trophies dropped by the target creature, and the percentage chance of dropping. |

#### Developer

| Command | Description |
|---|---|
| `@addallspells` | Adds all known spells to your own spellbook. |
| `@addalltitles` | Add all titles to yourself |
| `@addenc` | Spawns a new wcid or classname in the current outdoor cell as an encounter |
| `@additemspell` | Adds a spell to the last appraised item's spellbook. |
| `@addspell` | Adds the specified spell to your own spellbook. |
| `@addtitle` | Add title to yourself |
| `@animation` | Plays an animation on the current player, or optionally another object |
| `@auditobjectmaint` | Iterates over physics objects to find leaks |
| `@backward` | Teleports you backward by the specified distance (default 10). |
| `@barbershop` | Displays the barber ui |
| `@barrier-test` | Shows debug information for house barriers |
| `@bumpvelocity` | Bumps the velocity of the last appraised object. |
| `@castspell` | Casts a spell on the last appraised object |
| `@cbclear` | Clears only the ClothingTable entries from the portal.dat file cache (forces a fresh re-read on next use). |
| `@cbexport` | Exports a ClothingBase entry from portal.dat to a JSON file in Data/CustomClothingBase/. |
| `@cbreload` | Reloads all custom ClothingBase JSON files from Data/CustomClothingBase/ and clears the ClothingTable cache. |
| `@chatdump` | Spews 1000 lines of text to you. |
| `@check-collision` | Checks if the player is currently colliding with any other objects. |
| `@ci` | Creates an object in your inventory. |
| `@ciaetheria` | Spawns an Aetheria in the player's inventory |
| `@ciloot` | Generates randomized loot in player's inventory |
| `@cimob` | Manage creature mutators in realtime. |
| `@cirand` | Creates random objects in your inventory. |
| `@cisalvage` | Create a salvage bag in your inventory |
| `@clearcache` | Clears the various database caches. This enables live editing of the database information |
| `@clearphysicscaches` | Clears Physics Object Caches |
| `@cm` | Create a salvage bag in your inventory |
| `@comps` | Creates spell component items in your inventory for testing. |
| `@contract` | Query, stamp, and erase contracts on the targeted player |
| `@copychar` | Copies an existing character into your character list. |
| `@create` | Creates an object or objects in the world. |
| `@createcreature` | Debug command to spawn a creature in front of the player and save it as a static spawn if the static option is specified. |
| `@createinst` | Spawns a new wcid or classname as a landblock instance |
| `@createliveops` | Creates an object or objects with lifespans in the world for live events. |
| `@createnamed` | Creates a named object in the world. |
| `@currency` | Creates some currency items in your inventory for testing. |
| `@databaseperftest` | Test server/database performance. |
| `@databasequeueinfo` | Show database queue information. |
| `@database-shard-cache-npbrt` | Shard Database, Non-Player Biota Cache - Retention Time (in minutes) |
| `@database-shard-cache-pbrt` | Shard Database, Player Biota Cache - Retention Time (in minutes) |
| `@de_n` | Sends text to named player, formatted exactly as entered. |
| `@de_s` | Sends text to selected player, formatted exactly as entered, with no prefix of any kind. |
| `@deathxp` | Displays how much experience the last appraised creature is worth when killed. |
| `@debugboard` | Shows the current chess board state |
| `@debugchess` | Shows the chess move history for a player |
| `@debugdamage` | Toggles the display for player damage info |
| `@debugemote` | Enables emote debugging for the last appraised object |
| `@debugmove` | Toggles movement debugging for the last appraised monster |
| `@debugspell` | Toggles spell projectile debugging info |
| `@debugspellbook` | Shows the spellbook for the last appraised object |
| `@delevel` | Attempts to delevel the current player. Requires enough unassigned xp and unspent skill credits. |
| `@destructionqueue` | Shows the list of previously visible objects queued for destruction for a player |
| `@direct_emote_name` | Sends text to named player, formatted exactly as entered. |
| `@direct_emote_select` | Sends text to selected player, formatted exactly as entered, with no prefix of any kind. |
| `@dispel` | Removes all enchantments from the player |
| `@dist` | Returns the distance to the last appraised object |
| `@down` | Teleports you downward by the specified distance (default 10). |
| `@dungeonname` | Shows the dungeon name for the current landblock |
| `@echo` | Send text back to yourself. |
| `@echoflags` | Echo flags back to you |
| `@effect` | Plays an effect. |
| `@enable-aetheria` | Enables the aetheria slots for the player |
| `@end` | Ends a named custom server event. |
| `@equiptest` | Simulates equipping a new item to your character, replacing all other items. |
| `@event` | Maniuplates the state of an event |
| `@export-json` | Exports content from database to JSON file |
| `@export-json-folders` | Exports content from database to JSON file in a WeenieType/ItemType folder structure |
| `@export-sql` | Exports content from database to SQL file |
| `@export-sql-folders` | Exports weenie content from database to an SQL file in a WeenieType/ItemType folder structure |
| `@faction` | sets your own faction state. |
| `@fakelogin` | Fake Login Complete response |
| `@fellow-dist` | Shows distance to each fellowship member |
| `@fellow-info` | Shows debug info for fellowships. |
| `@fly` | Toggles admin flight mode (no gravity, no fall damage). |
| `@food` | Creates some food items in your inventory for testing. |
| `@forcegc` | Forces .NET Garbage Collection |
| `@forcegc2` | Forces .NET Garbage Collection with LOH Compact |
| `@forcelogoff` | Force log off of specified character or last appraised character |
| `@forcelogout` | Force log off of specified character or last appraised character |
| `@forward` | Teleports you forward by the specified distance (default 10). |
| `@gamecastemote` | Sends text to all players, formatted exactly as entered. |
| `@gamecastlocal` | Sends a server-wide broadcast. |
| `@gamecastlocalemote` | Sends text to all players within chat range, formatted exactly as entered. |
| `@generate-classnames` | Generates WeenieClassName.cs from current world database |
| `@generatordump` | Lists all properties for the last generator you examined. |
| `@getallspellformula` | Tests spell formula calculation |
| `@getinfo` | Shows basic info for the last appraised object. |
| `@getproperty` | Gets a property for the last appraised object |
| `@getspellformula` | Tests spell formula calculation |
| `@givemana` | Gives mana to the last appraised object |
| `@gps` | Display location. |
| `@grantitemxp` | Give item XP to the last appraised item. |
| `@grantluminance` | Give luminance to yourself (or the specified character). |
| `@grantxp` | Give XP to yourself (or the specified character). |
| `@harmself` | Sets all player vitals to 1 |
| `@idlist` | Shows the next ID that will be allocated from GuidManager. |
| `@import-json` | Imports json data from the Content folder |
| `@import-sql` | Imports sql data from the Content folder |
| `@import-sql-folders` | Imports all weenie sql data from the Content folder and all sub-folders |
| `@inv` | Creates sample items, foci and containers in your inventory. |
| `@knownobjs` | Shows the list of objects currently known to an object |
| `@knownplayers` | Shows the list of players known to an object |
| `@left` | Teleports you left by the specified distance (default 10). |
| `@listcb` | List Clothing Tables available |
| `@listplayers` | Displays all of the active players connected too the server. |
| `@listpositions` | Displays all available saved character positions from the database. |
| `@loadalllandblocks` | Loads all Landblocks. This is VERY crude. Do NOT use it on a live server!!! It will likely crash the server.  Landblock resources will be loaded async and will continue to do work even after all landblocks have been loaded. |
| `@lootconfig` | View or modify DerpACE loot item variables. |
| `@lootgen` | Generate a piece of loot from the LootGenerationFactory. |
| `@lostest` | Tests for direct visibilty with latest appraised object |
| `@makeiou` | Make an IOU and put it in your inventory |
| `@monsterspell` | The last appraised creature casts a spell. For targeted spells, defaults to the current player. |
| `@morph` | Morphs your bodily form into that of the specified creature. Be careful with this one! |
| `@movement` | Movement testing command, to be removed soon |
| `@MoveTo` | Used to test the MoveToObject message.   It will spawn a training wand in front of you and then move to that object. |
| `@myloc` | Shows the current player location, from the server perspective |
| `@netstats` | View network statistics |
| `@nudge` | Adjusts the spawn position of a landblock instance |
| `@pathfinding` | Manage the DotRecast monster pathfinding navmesh system. |
| `@pk` | sets your own PK state. |
| `@pktimer` | Sets your PK timer to the current time |
| `@playsound` | Plays a sound. |
| `@portalstorm` | Tests starting a portal storm on yourself |
| `@propertydump` | Lists all properties for the last world object you examined. |
| `@purchase-house` | Instantly purchase the house for the last appraised covenant crystal. |
| `@qst` | Query, stamp, and erase quests on the targeted player |
| `@readdat` | Tests reading the client_portal.dat |
| `@recordcast` | Records spell casting keypresses to server for debugging |
| `@reload-landblock` | Reloads the current landblock. |
| `@removeenc` | Removes the last appraised object from the encounters table |
| `@removeinst` | Removes the last appraised object from the current landblock instances |
| `@removeitemspell` | Removes a spell to the last appraised item's spellbook. |
| `@removespell` | Removes the specified spell to your own spellbook. |
| `@remove-vitae` | Removes vitae from last appraised player |
| `@requirecomps` | Sets whether spell components are required to cast spells. |
| `@resist-info` | Shows the resistance info for the last appraised creature. |
| `@retaliatetargets` | Shows the list of retaliate targets for a monster |
| `@right` | Teleports you right by the specified distance (default 10). |
| `@rotate` | Adjusts the rotation of a landblock instance |
| `@rotate-x` | Adjusts the rotation of a landblock instance along the x-axis |
| `@rotate-y` | Adjusts the rotation of a landblock instance along the y-axis |
| `@rotate-z` | Adjusts the rotation of a landblock instance along the z-axis |
| `@safecomps` | Enables / disables spell component burning |
| `@save-now` | Saves your session. |
| `@setcoin` | Set Coin display debug only usage |
| `@setglobalenviron` | Sets or clears server's global environment option |
| `@sethealth` | sets your current health to a specific value. |
| `@setlbenviron` | Sets or clears your current landblock's environment option |
| `@setposition` | Saves the supplied character position type to the database. |
| `@setproperty` | Sets a property for the last appraised object |
| `@setpurchasetime` | Sets the house purchase time for this player |
| `@setvital` | Sets the specified vital to a specified value |
| `@showsession` | Show IP and ID for network session of last appraised character |
| `@showstats` | Shows a list of a creature's current attribute/skill levels |
| `@showtier` | Shows the DeathTreasure tier for the last appraised monster |
| `@showvelocity` | Shows the velocity of the last appraised object. |
| `@show-wielded-treasure` | Shows the WieldedTreasure table for a Creature |
| `@spendallxp` | Spend all available XP on Attributes, Vitals and Skills. |
| `@splits` | Creates some stackable items in your inventory for testing. |
| `@start` | Starts a named custom server event. |
| `@sticky` | Sets whether you lose items should you die. |
| `@targetloc` | Shows the location of the last appraised object |
| `@teleallto` | Teleports all players to a player. If no target is specified, all players will be teleported to you. |
| `@teledist` | Teleports a some distance ahead of the last object spawned |
| `@teledungeon` | Teleport to a dungeon |
| `@teleloc` | Teleport yourself to the specified location. |
| `@telepoi` | Teleport yourself to a named Point of Interest |
| `@teletype` | Teleport to a saved character position. |
| `@telexyz` | Teleport to a location. |
| `@testaim` | Tests the aim high/low motions, and projectile spawn position |
| `@testdeathitems` | Test death item selection |
| `@tiermobs` | Shows a list of monsters for a particular tier # |
| `@turnto` | Turns the last appraised object to the player |
| `@up` | Teleports you upward by the specified distance (default 10). |
| `@usewith` | Uses specified object on last appraised object |
| `@vendordump` | Lists all properties for the last vendor you examined. |
| `@visibleobjs` | Shows the list of objects currently visible to an object |
| `@visibleplayers` | Shows the list of players visible to a player |
| `@visibletargets` | Shows the list of targets currently visible to a monster |
| `@vloc2loc` | Output a set of LOCs for a given landblock found in the VLOCS dataset |
| `@we` | Sends text to all players, formatted exactly as entered. |
| `@weapons` | Creates testing items in your inventory. |
| `@whoami` | Shows you your GUIDs. |

#### Admin

| Command | Description |
|---|---|
| `@accountcreate` | Creates a new account. |
| `@accountget` | Gets an account. |
| `@adminhouse` | House management tools for admins. |
| `@bornagain` | Restores a deleted character to an account. |
| `@cancel-shutdown` | Stops an active server shutdown. |
| `@cell-export` | Export contents of CELL DAT file. |
| `@cimobspawn` | Spawns a creature near you and force-applies a mob modifier. |
| `@deletecharacter` | Deletes a character and removes it from players restore list |
| `@exit` | Shut down server immediately. |
| `@fetchbool` | Fetches a server property that is a bool |
| `@fetchdouble` | Fetches a server property that is a double |
| `@fetchlong` | Fetches a server property that is a long |
| `@fetchstring` | Fetches a server property that is a string |
| `@fix-allegiances` | Fixes the monarch data for allegiances |
| `@fix-biota-emote-delay` | Fixes biota emotes with incorrect default delays |
| `@fix-gear-plating` | Corrects the name on Gear Plating. |
| `@fix-shortcut-bars` | Fixes the players with duplicate items on their shortcut bars. |
| `@fix-spell-bars` | Fixes the players spell bars. |
| `@getenchantments` | Shows the enchantments for the last appraised item |
| `@highres-export` | Export contents of client_highres.dat file. |
| `@image-export` | Export Texture/Image Files |
| `@ironmanmode` | Enable or disable Ironman opt-in server-wide. |
| `@language-export` | Export contents of client_local_English.dat file. |
| `@modifyattr` | Adjusts an attribute for the last appraised mob/NPC/player |
| `@modifybool` | Modifies a server property that is a bool |
| `@modifydouble` | Modifies a server property that is a double |
| `@modifylong` | Modifies a server property that is a long |
| `@modifypropertydesc` | Modifies a server property's description |
| `@modifyskill` | Adjusts the skill for the last appraised mob/player |
| `@modifystring` | Modifies a server property that is a string |
| `@modifyvital` | Adjusts the maximum vital attribute for the last appraised mob/player and restores full vitals |
| `@movetome` | Moves the last appraised object to the current player location. |
| `@portal-export` | Export contents of PORTAL DAT file. |
| `@reitem` | Rename the last appraised weapon or shield. |
| `@reload-loot-tables` | reloads the latest data from the loot tables |
| `@resyncproperties` | Resync the properties database |
| `@set-accountaccess` | Change the access level of an account. |
| `@set-accountpassword` | Set the account password. |
| `@set-characteraccess` | Sets the access level for the character |
| `@set-shutdown-interval` | Changes the delay, in seconds, before the server will shutdown. |
| `@show-allegiances` | Shows all of the allegiance chains on the server. |
| `@showprops` | Displays the name of all properties configurable via the modify commands |
| `@shutdown` | Begins the server shutdown process. Optionally displays a shutdown message, if a string is passed. |
| `@tester` | Toggles tester mode: 290 in all attributes, every skill specialized at max ranks. |
| `@testlootgen` | Generates Loot for testing LootFactories.  Do testlootgen -info for examples. |
| `@testlootgencorpse` | Generates Corpses for testing LootFactories |
| `@verify-armor-levels` | Verifies and optionally fixes any existing armor levels above AL cap |
| `@verify-attributes` | Verifies and optionally fixes any bugs with player attribute data |
| `@verify-beneficial-enchantments` | Verifies enchantment registry has correct StatModType for Beneficial spells and optionally fixes |
| `@verify-clothing-wield-level` | Verifies and optionally fixes any t7/t8 clothing that is missing a wield level requirement |
| `@verify-heritage-augs` | Verifies all players have their heritage augs. |
| `@verify-legendary-wield-level` | Verifies and optionally fixes any items with legendary cantrips that have less than 180 wield level requirement |
| `@verify-max-augs` | Verifies and optionally fixes any bugs with the # of augs each player has |
| `@verify-melee-rares` | Verifies and optionally fixes any melee rares to EoR wcids |
| `@verify-player-data` | Verifies and optionally fixes any bugs with player data. Runs all of the verify* commands. |
| `@verify-shield-rating` | Verifies and optionally fixes any lootgen shields with incorrectly assigned CD/CDR |
| `@verify-skill-credits` | Verifies and optionally fixes any bugs with player skill credits |
| `@verify-skills` | Verifies and optionally fixes any bugs with player skill data |
| `@verify-vitals` | Verifies and optionally fixes any bugs with player vitals data |
| `@verify-xp` | Verifies and optionally fixes any bugs with player xp |
| `@version` | Show server version information. |
| `@watchmen` | Displays a list of accounts with the specified level of admin access. |
| `@wave-export` | Export Wave Files |
| `@world` | Open or Close world to player access. |

