# Custom ClothingBase Overrides

Place custom ClothingBase `*.json` files in this folder.

At build time, ACE.Server copies these files to the runtime `Data/CustomClothingBase` folder next to the server binaries. `CustomClothingManager` loads JSON files from that runtime folder at startup and when `@cbreload` is used.

Files may be nested in subfolders; the loader scans this directory recursively.

For deployments that keep the overrides outside the server output folder, set `DERPACE_CUSTOM_CLOTHING_DIR` to the absolute path of the override directory.

## Safe Custom Items

Use a new, unused ClothingBase ID for custom items. Do not edit/export JSON using the original base ClothingBase ID unless you truly want every item using that base to change.

Recommended flow:

- `@cbclone <sourceBaseId> <newCustomId> [label]`
- Edit the generated JSON as needed.
- Set only the custom item's `PropertyDataId.ClothingBase` to `<newCustomId>`.
- Run `@cbreload`.

If a JSON file targets an existing portal.dat ClothingBase ID, the loader skips it by default to prevent broad base-item changes.

## Intentional Base Overrides

If you really do want to patch a base ClothingBase for every item that uses it, use:

- `@cbexport <id> [label]`

Exported base override files include:

```json
"AllowBaseOverride": true
```

You can also add that field manually to an existing JSON file. Without it, existing portal.dat IDs are ignored.

## Compatibility

The JSON format is compatible with OptimShi/CustomClothingBase style exports:

- Files named with a numeric ClothingBase prefix patch that ClothingBase, including existing portal.dat ClothingBase IDs.
- Filename prefixes may be decimal, `0x` hex, bare 8-digit hex, or an 8-hex-digit prefix followed by labels/suffixes.
- Files without a parseable filename ID fall back to JSON `Id`; existing portal.dat IDs still require `AllowBaseOverride: true` in that case.
- `ClothingBaseEffects` and `ClothingSubPalEffects` entries replace matching existing keys and preserve unrelated original keys.
- `PaletteSet` may point to either a normal palette-set file (`0x0F...`) or directly to a raw palette file (`0x04...`). Raw palettes are treated as one-entry palette sets.

## Filename Format

Use one of these forms:

- `0x10001234.json`
- `10001234.json`
- `268440116.json`
- `10001234_some_label.json`

If the filename begins with a numeric ClothingBase ID, that filename ID wins. If the filename has no parseable ID, the loader falls back to the JSON `Id`.

## Commands

- `@cbclone <sourceBaseId> <newCustomId> [label]` or `@clothingbase-clone <sourceBaseId> <newCustomId> [label]` creates an isolated custom ClothingBase JSON.
- `@cbexport <id> [label]` or `@clothingbase-export <id> [label]` exports an intentional base override JSON.
- `@cbreload` reloads all JSON files and clears cached ClothingTable entries.
- `@cbclear` or `@clear-clothing-cache` clears only cached ClothingTable entries.