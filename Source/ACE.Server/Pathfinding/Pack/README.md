# Pathfinding navmesh packs

Drop a navmesh `.zip` produced by `/pathfinding export <path>` into this folder.

On server startup, `PathfindingPrebuilder.ExtractShippedPacks()` will:

1. Enumerate every `*.zip` in this directory.
2. Extract any `Indoors/*.mesh` and `Outdoors/*.mesh` entries into the active mesh root
   (default: `<ACE.Server bin>\Pathfinding\Meshes\Indoors|Outdoors`, or whatever
   `pathfinding_mesh_root` is set to).
3. Skip files that already exist (so player-rebuilt meshes aren't clobbered).
4. Write a `<zipName>.zip.extracted` sentinel so the pack is only extracted once
   per install (until the zip is replaced with a newer one).

Files in this folder are copied to the build output via `ACE.Server.csproj`
(`<None Include="Pathfinding\Pack\*.zip" CopyToOutputDirectory="PreserveNewest" />`).

Recommended workflow to ship a baked navmesh set:

```
/pathfinding export "C:\path\to\Source\ACE.Server\Pathfinding\Pack\DerpACE-Navmeshes.zip"
```

Then commit the zip. On any fresh build/checkout the server will auto-extract on
first boot and skip the slow build entirely.
