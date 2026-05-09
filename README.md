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
  * `PropertyFloat.BreacherPiercePct` — fraction of `DamageMitigated` added back as bonus damage on **every** hit (default 1–5%)

#### Always-On Armor Pierce
* Same `DamageMitigated × N%` formula as the Fencer's Blade pierce, but **no proc roll** — every bolt that strikes adds back a portion of what armor blocked
* Bonus = `max(0, DamageMitigated) × BreacherPiercePct`, rounded; only displayed when ≥ 1
* Combat message: `+N pierce [Breacher's Crossbow]`
* Fits the slow-but-punishing crossbow archetype: low proc theatrics, steady armor-defeating chip damage on every shot
* All values runtime-tunable via `@lootconfig` (`breacher.drop`, `breacher.tier`, `breacher.piercemin`, `breacher.piercemax`)

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

### Ironman Mode
A hardcoded port of [aquafir's Ironman BaseMod](https://github.com/aquafir/ACE.BaseMod/tree/master/Samples/Ironman). Players opt in with a chat command; the choice is **irreversible** for the lifetime of the character.

#### Commands

| Command | Access | Description |
|---|---|---|
| `/ironman` | Player | If already an Ironman: show skill plan status. Otherwise: show usage. |
| `/ironman on` | Player | Begin commitment — prints a warning and opens a 30-second confirmation window. Only available at level 10 or below. |
| `/ironman confirm` | Player | Finalize the conversion within the window. **Cannot be undone.** |
| `/ironman top` | Player | Show the Ironman leaderboard (top 10 players by creature kills). |
| `/ironmantop` | Player | Shortcut for `/ironman top`. |
| `/ironmantopkillers` | Player | Show the top 10 creatures that have killed the most Ironman players. |

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
  * **Auto-tag** — `Player_Inventory.TryCreateInInventoryWithNetworking` flips `IsIronmanItem = true` on every item that successfully enters an Ironman's inventory (covers corpse loot, chest loot, vendor purchase, emote grants, etc.)
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
