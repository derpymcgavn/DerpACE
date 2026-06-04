# Custom ClothingBase Overrides

Place custom ClothingBase `*.json` override files in this folder.

At build time, ACE.Server copies these files to the runtime `Data/CustomClothingBase` folder next to the server binaries. `CustomClothingManager` loads JSON files from that runtime folder at startup and when the `@cbreload` command is used.

Files may be nested in subfolders; the loader scans this directory recursively.

For deployments that keep the overrides outside the server output folder, set `DERPACE_CUSTOM_CLOTHING_DIR` to the absolute path of the override directory.
