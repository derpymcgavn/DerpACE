# Olthoi Morphic Transformation System

## Overview
The Morphic System allows Olthoi players to permanently transform into a creature form by creating a **new morphed character**. This works similarly to the `/morph` admin command but is restricted to Olthoi players, locks them to a single creature type permanently, starts them at level 1, and applies Ironman-style stat/skill rolls.

## Features

### Creates New Character
- `/morphic` creates a **brand new character** and logs you out
- The new character is based on your locked creature's weenie
- Works like the `/morph` admin command
- Character name format: `<OriginalName>-Morphed` (or with timestamp if name unavailable)

### Locked Creature System
- **First Use**: Player must specify a creature WCID or name to lock permanently
- **Permanent Lock**: Once locked, the lock persists across characters on that account
- **Subsequent Uses**: After locking, player can morph with just `/morphic` (no parameters)

### Level 1 Start with Ironman Stats/Skills
- New morphed character starts at **Level 1**
- **Random Attribute Roll**: One primary attribute set to 100, others to 46 (Ironman style)
- **Random Skill Roll**: Weapon and primary skills rolled randomly like Ironman mode
- Skills unlock at milestone levels as the character progresses

### Complete Visual Transformation
The morphed character receives the complete visual package of the locked creature:
- **Setup** - The creature's 3D model/mesh
- **MotionTable** - Creature animations and movement behaviors
- **SoundTable** - Creature sound effects
- **PaletteBase** - Color scheme/palette
- **ClothingBase** - Texture appearance
- **Default Equipment** - Any wieldable/containable items from the creature weenie

### PK-Free Combat
- Morphed character is set to `PlayerKillerStatus.Free`
- Can attack and be attacked by both PK and NPK players
- Can participate in both PvE and PvP

## Commands

### `/morphic <wcid or name>` (First Time)
Lock to a specific creature permanently and create your first morphed character.

**Examples:**
```
/morphic 7           # Lock to Drudge Skulker (WCID 7) and morph
/morphic olthoi      # Lock to an Olthoi creature and morph
/morphic drudge      # Lock by creature name and morph
```

**On Success:**
- Account is locked to the chosen creature permanently
- New morphed character is created
- Character is set to Level 1
- Ironman-style stats/skills rolled
- You are logged out
- New character appears in your character list

### `/morphic` (After Locked)
Create another morphed character using your locked creature. No parameters needed.

**On Success:**
- New morphed character created using your locked creature
- Level 1 with new Ironman stat/skill rolls
- You are logged out
- New character appears in your character list

## Implementation Details

### How It Works
1. Check if player is Olthoi (race restriction)
2. Check if account has a locked creature WCID
   - If not, require parameter and lock permanently
3. Load the locked creature weenie from database
4. Create a new `Player` object from the creature weenie (like `/morph`)
5. Preserve original player's WeenieType
6. Generate character name (`<OriginalName>-Morphed`)
7. Set location to original player's location
8. Equip creature's default wearables/items
9. **Set level to 1**
10. **Call `IronmanFactory.RollAttributes()` and `IronmanFactory.RollSkills()`**
11. Set `PlayerKillerStatus.Free`
12. Mark with morphic properties
13. Save new character to database
14. Add to account's character list
15. Log out original player

### Custom Properties

#### PropertyInt
- `MorphicLockedCreatureWCID = 9029` - Permanently stores locked creature WCID (persists across characters)
- `MorphicCreatureWCID = 9028` - Stores current morphed creature WCID (on morphed character)

#### PropertyBool
- `IsMorphicForm = 9038` - Marks morphed characters

### Files Modified
- `ACE.Entity/Enum/Properties/PropertyInt.cs` - Added `MorphicLockedCreatureWCID`
- `ACE.Entity/Enum/Properties/PropertyBool.cs` - Added `IsMorphicForm`
- `ACE.Server/Command/Handlers/PlayerCommands.cs` - `/morphic` command implementation
- `ACE.Server/Factories/IronmanFactory.cs` - Made `RollAttributes()` and `RollSkills()` public

### Race Restrictions
- Only `HeritageGroup.Olthoi` and `HeritageGroup.OlthoiAcid` can use morphic transformation
- Command returns error message for other races

### Character Naming
- First attempt: `<OriginalName>-Morphed`
- If unavailable: `<OriginalName>-Morphed-<Timestamp>`

## Design Philosophy

### New Character Creation
Unlike the previous in-place transformation design, morphic now creates entirely new characters. This:
- Matches the `/morph` admin command behavior
- Provides a clean slate for level 1 progression
- Avoids complex state management of transforming existing characters
- Allows multiple morphed characters per account
- Prevents issues with equipment, spells, and other player state

### Permanent Lock
The locked creature persists at the **account level** through the original Olthoi character's properties. This:
- Creates meaningful choice and identity
- Prevents abuse/exploitation through constant rerolling
- Allows for multiple morphed characters of the same creature type
- Maintains lock even if original character is deleted

### Level 1 + Ironman Rolls
Starting at Level 1 with randomized stats/skills:
- Creates true "class choice" feel with meaningful progression
- Provides balanced starting point
- Integrates with existing XP/level systems
- Adds replayability through random rolls
- Mirrors the Ironman hardcore challenge experience

### Comparison to `/morph` Admin Command
- `/morph` - Admin tool, allows any weenie, no restrictions
- `/morphic` - Player command, Olthoi-only, creature lock, level 1, Ironman rolls

## Future Enhancements
Possible additions:
- Truly random creature assignment (no player choice)
- Creature-specific abilities/skills based on morphed type
- Morphic form XP bonuses/penalties
- Creature type restrictions/whitelist/blacklist
- Morphic form stat modifiers based on creature difficulty
- Special morphic-only areas or content
- Morphic leaderboards
