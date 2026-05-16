# DerpACE Pathfinding System

## How It Works

The pathfinding system uses **Recast/Detour** to dynamically generate navigation meshes from ACE's world geometry. The system is **fully on-demand** and requires no prebuilding.

### On-Demand Generation

When a creature needs pathfinding in a landblock for the first time:

1. **Geometry Analysis**: The system loads dungeon cell or terrain geometry from the ACE dat files
2. **Navmesh Generation**: Recast/Detour analyzes the walkable surfaces, slopes, obstacles, and generates a navigation mesh
3. **Disk Caching**: The generated `.mesh` file is saved to `Pathfinding/Meshes/Indoors/` or `Outdoors/`
4. **Future Use**: Next time that landblock needs pathfinding, the cached mesh loads instantly

**No prebuilding required!** The first mob that needs pathfinding in a dungeon will trigger generation (taking 1-3 seconds), then every subsequent use is instant.

### Optional Prebuilding

For performance, you can pre-generate all navmeshes at once:

- **Command**: `/pathfind prebuild` - Scans all landblocks and generates meshes
- **Auto-prebuild**: Set `pathfinding_prebuild_on_boot=true` in config (not recommended, slow startup)
- **Shipped Packs**: Drop a pre-baked `.zip` in `Pathfinding/Pack/` (see `Pack/README.md`)

Prebuilding is purely optional. It just avoids the small delay when each landblock is first visited.

### Cache Distribution

To share pre-generated navmeshes:

```
/pathfind export "C:\path\to\DerpACE-Navmeshes.zip"
```

This creates a zip of all cached meshes that can be:
- Committed to the repo in `Pathfinding/Pack/`
- Extracted automatically on first boot (no build needed)
- Shared with other server operators

## Commands

- `/pathfind <x> <y>` - Draw a pathfinding route to coordinates
- `/pathfind prebuild` - Generate all navmeshes now (slow, optional)
- `/pathfind status` - Show prebuild progress
- `/pathfind rebuild <landblock>` - Regenerate mesh for one landblock
- `/pathfind export <path>` - Export all cached meshes to a zip

## Configuration

- `pathfinding` - Enable/disable pathfinding (default: true)
- `pathfinding_prebuild_on_boot` - Auto-prebuild on startup (default: false, not recommended)
- `pathfinding_mesh_root` - Directory for cached meshes (default: `<bin>/Pathfinding/Meshes`)

## Performance

- **First generation**: 1-3 seconds per landblock (one-time cost)
- **Cached load**: ~50-100ms per landblock (instant)
- **Memory**: ~50-200KB per landblock mesh
- **Disk**: ~20-100KB per `.mesh` file

## Technical Details

- Uses **DotRecast** (C# port of Recast/Detour)
- Analyzes actual ACE geometry (dungeon cells, terrain heightmaps)
- Supports indoor dungeons and outdoor terrain
- Two agent widths: `Narrow` (humanoid) and `Wide` (large creatures)
- Cross-landblock pathfinding with multi-hop routing

## TL;DR

**Just enable pathfinding and run the server.** Navmeshes generate automatically when first needed and cache to disk. Prebuilding is optional for startup performance only.
