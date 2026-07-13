# DerpACE Admin Map

Place or generate the overworld map image here when using the Admin Map Web UI.

Default image path in `DerpAce.json`:

```json
"admin_map_image_path": "Data/AdminMap/dereth-map.png"
```

Generate the map from current DAT files:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project Source\ACE.AdminMapGen\ACE.AdminMapGen.csproj -- --cell "C:\Turbine\Asheron's Call\client_cell_1.dat" --portal "C:\Turbine\Asheron's Call\client_portal.dat" --out Source\ACE.Server\Data\AdminMap\dereth-map.png
```

The generator defaults to `--scale 4`, producing an 8192x8192 image for admin zoom. Use `--scale 2` for 4096x4096, or `--scale 1` for the exact 2048x2048 terrain-cell resolution.

The player pins are mapped to Dereth's `-102..102` coordinate square. Tune these if the image border does not line up exactly:

```json
"admin_map_bounds_left_pct": 0.45,
"admin_map_bounds_top_pct": 0.45,
"admin_map_bounds_right_pct": 99.55,
"admin_map_bounds_bottom_pct": 99.55
```

## Inventory icons

Place Admin Map inventory icon PNGs in `Data/AdminMap/icons` using the full eight-digit hexadecimal Data ID as the filename:

```text
06002191.png
06002AC1.png
06005B29.png
```

The default configuration is:

```json
"admin_map_icon_path": "Data/AdminMap/icons"
```

The directory is copied to the server output by `ACE.Server.csproj`. An absolute path may still be configured when desired.