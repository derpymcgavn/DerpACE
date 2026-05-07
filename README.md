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
