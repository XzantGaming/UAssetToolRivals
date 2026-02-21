# UAssetTool

A CLI tool for parsing, editing, and converting Unreal Engine 5 assets. Built on [UAssetAPI](https://github.com/atenfyr/UAssetAPI) with extensions for Zen/IoStore support, texture handling, and NiagaraSystem editing. **Optimized for Marvel Rivals modding.**

## Features

- **IoStore Mod Creation** - Create IoStore mod bundles (`.utoc`/`.ucas`/`.pak`) from legacy assets or PAK files
- **IoStore Extraction** - Extract game assets from IoStore containers to legacy `.uasset`/`.uexp` format
- **PAK Extraction** - Extract assets from legacy PAK files (Oodle, Zlib, Zstd, AES encryption)
- **JSON Conversion** - Export `.uasset` to JSON and back for easy property editing
- **NiagaraSystem Editing** - Modify particle effect colors with structured ShaderLUT parsing
- **MaterialTag Injection** - Auto-inject per-slot gameplay tags from `MaterialTagAssetUserData` during mod creation
- **StaticMesh Support** - ScreenSize expansion, unversioned property conversion, NavCollision handling
- **Blueprint Analysis** - Scan ChildBP assets for IsEnemy parameter redirects
- **GUI Backend** - JSON stdin/stdout API for frontend integration

---

## Installation

### Download (Recommended)

Grab the latest self-contained executable from [Releases](https://github.com/XzantGaming/UassetToolRivals/releases). No .NET SDK required.

### Build from Source

**Prerequisites:** .NET 8.0 SDK

```bash
git clone https://github.com/XzantGaming/UassetToolRivals.git
cd UassetToolRivals
dotnet build -c Release
```

**Publish self-contained executable:**
```bash
# Windows
dotnet publish src/UAssetTool/UAssetTool.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish

# Linux
dotnet publish src/UAssetTool/UAssetTool.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish
```

---

## Quick Start: Creating a Mod

The most common workflow for Marvel Rivals modding:

```bash
# 1. From a legacy .pak mod file (most common)
UAssetTool create_mod_iostore "output/MyMod_9999999_P" "my_mod.pak" --usmap "game.usmap"

# 2. From loose .uasset files
UAssetTool create_mod_iostore "output/MyMod_9999999_P" \
    "Marvel/Content/Marvel/Characters/1014/Meshes/SK_1014_1014001.uasset" --usmap "game.usmap"

# 3. From a directory of assets
UAssetTool create_mod_iostore "output/MyMod_9999999_P" "Marvel/Content/Marvel/Characters/1014/" --usmap "game.usmap"
```

Then copy the three output files (`.utoc`, `.ucas`, `.pak`) to:
```
MarvelRivals/MarvelGame/Marvel/Content/Paks/~mods/
```

---

## Building a Legacy PAK

If you need to create a legacy `.pak` file (e.g. for distribution or as input to `create_mod_iostore`):

```bash
# Create a PAK from a directory of cooked assets
UAssetTool create_pak <output.pak> <input_directory> [options]

# Create a PAK from specific files
UAssetTool create_pak <output.pak> <file1.uasset> <file2.uasset> ...
```

**Options:**
- `--mount-point <path>` - Mount point prefix (default: `../../../`)
- `--compress` - Enable Oodle compression
- `--no-compress` - No compression (default)

**Important:** Assets inside the PAK must follow the game's directory structure relative to the mount point. For example:
```
Marvel/Content/Marvel/Characters/1014/Meshes/SK_1014_1014001.uasset
Marvel/Content/Marvel/Characters/1014/Meshes/SK_1014_1014001.uexp
Marvel/Content/Marvel/Characters/1014/Meshes/SK_1014_1014001.ubulk
```

---

## IoStore Mod Creation

Create IoStore mod bundles from legacy assets for Marvel Rivals:

```bash
# From a .pak file (extracts and converts automatically)
UAssetTool create_mod_iostore "output/MyMod" "my_legacy_mod.pak"

# From individual assets
UAssetTool create_mod_iostore "output/MyMod" \
    "Content/Marvel/Characters/1014/Meshes/SK_1014_1014001.uasset"

# From a directory (recursively finds all .uasset files)
UAssetTool create_mod_iostore "output/MyMod" "Content/Marvel/Characters/1014/"

# With obfuscation (protects from FModel extraction)
UAssetTool create_mod_iostore "output/MyMod" --obfuscate "my_mod.pak"

# Without compression (faster, larger files)
UAssetTool create_mod_iostore "output/MyMod" --no-compress "my_mod.pak"
```

**Options:**
- `--usmap <path>` - Path to `.usmap` mappings file (needed for StaticMesh unversioned conversion)
- `--mount-point <path>` - Mount point (default: `../../../`)
- `--game-path <prefix>` - Game path prefix (default: `Marvel/Content/`)
- `--compress` / `--no-compress` - Toggle Oodle compression (default: enabled)
- `--obfuscate` - Encrypt with game's AES key to prevent FModel extraction
- `--pak-aes <hex>` - AES key for decrypting encrypted input `.pak` files
- `--no-material-tags` - Disable automatic MaterialTag injection

**Output files** (copy all three to `~mods`):
- `<output>.utoc` - Table of Contents
- `<output>.ucas` - Container Archive Store
- `<output>.pak` - Companion PAK with chunk names

**Marvel Rivals auto-fixes applied during conversion:**
- **SkeletalMesh**: FGameplayTagContainer padding, MaterialTag injection
- **StaticMesh**: ScreenSize expansion (64 → 128 bytes), versioned → unversioned property conversion, NavCollision CookedSerialSize override
- **StringTable**: FGameplayTagContainer padding

---

## IoStore Extraction

Extract game assets from IoStore containers (`.utoc`/`.ucas`) to legacy format:

```bash
# Extract specific assets by filter
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output_dir" --filter SK_1014 SK_1057

# Extract with dependencies
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output_dir" --filter Characters/1057 --with-deps
```

**Options:**
- `--filter <patterns...>` - Extract packages matching patterns (space-separated, OR logic)
- `--with-deps` - Also extract imported/referenced packages
- `--mod <path>` - Extract from mod containers (see below)
- `--script-objects <path>` - Path to ScriptObjects.bin for import resolution
- `--global <path>` - Path to global.utoc for script objects
- `--container <path>` - Additional container for cross-package imports

### Mod Extraction

```bash
# Extract all packages from a mod
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output_dir" --mod "path/to/mod.utoc"

# Extract from mod with filter
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output_dir" --mod "mod.utoc" --filter SK_1014

# Extract from all mods in a directory
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output_dir" --mod "C:/Mods/"

# Extract mod + game dependencies
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output_dir" --mod "mod.utoc" --with-deps
```

| Input | Behavior |
|-------|----------|
| `--mod "path/to/mod.utoc"` | Loads single mod container |
| `--mod "path/to/mods/"` | Loads all `.utoc` files in directory |
| `--mod "mod1.utoc" "mod2.utoc"` | Loads multiple containers |

Game containers use AES decryption; mod containers are loaded unencrypted.

---

## PAK Extraction

```bash
# Extract from PAK
UAssetTool extract_pak <pak_path> <output_dir>

# List files without extracting
UAssetTool extract_pak <pak_path> <output_dir> --list

# With AES decryption
UAssetTool extract_pak <pak_path> <output_dir> --aes "YOUR_AES_KEY_HEX"

# Filter by pattern
UAssetTool extract_pak mod.pak output --filter SK_1036 MI_Body
```

**Options:**
- `--aes <key>` - AES decryption key (64 hex chars)
- `--filter <patterns...>` - Only extract matching files (OR logic)
- `--list` - List files only

Supports PAK v11 (UE5), Oodle/Zlib/Zstd compression, encrypted and unencrypted.

---

## JSON Conversion

```bash
# Single file
UAssetTool to_json <uasset_path> [usmap_path] [output_dir]

# Batch (all .uasset in directory, preserves structure)
UAssetTool to_json <directory> [usmap_path] [output_dir]

# JSON back to uasset
UAssetTool from_json <json_path> <output_uasset_path> [usmap_path]
```

---

## NiagaraSystem Editing

Edit particle effect colors in NiagaraSystem assets:

```bash
# List NS files with metadata
UAssetTool niagara_list <directory> [usmap_path]

# Inspect color curves
UAssetTool niagara_details <asset_path> [usmap_path]
UAssetTool niagara_details <asset_path> [usmap_path] --full

# Edit colors
UAssetTool niagara_edit <asset_path> <R> <G> <B> [A] [options...] [usmap_path]

# Batch modify
UAssetTool modify_colors <directory> <usmap_path> [R G B A]
```

### Selective Targeting

```bash
UAssetTool niagara_edit asset.uasset 0 10 0 1 --export-name Glow
UAssetTool niagara_edit asset.uasset 0 10 0 1 --export-index 5
UAssetTool niagara_edit asset.uasset 0 10 0 1 --color-range 0 10
UAssetTool niagara_edit asset.uasset 0 10 0 1 --channels rgb
```

| Option | CLI Flag | Description |
|--------|----------|-------------|
| `exportIndex` | `--export-index <n>` | Target specific export |
| `exportNameFilter` | `--export-name <pattern>` | Filter by name (case-insensitive) |
| `colorIndex` | `--color-index <n>` | Single color index |
| `colorIndexStart/End` | `--color-range <start> <end>` | Color range (inclusive) |
| `modifyR/G/B/A` | `--channels <rgba>` | Which channels to modify |

### Workflow

```bash
# 1. Extract NS assets from game
UAssetTool extract_iostore_legacy "C:/Game/Paks" "output" --filter "VFX/Particles"

# 2. List available NS files
UAssetTool niagara_list "output/Content" "mappings.usmap"

# 3. Inspect a file
UAssetTool niagara_details "output/.../NS_Effect.uasset" "mappings.usmap"

# 4. Modify colors
UAssetTool niagara_edit "output/.../NS_Effect.uasset" 0 10 0 1 "mappings.usmap"

# 5. Create mod bundle
UAssetTool create_mod_iostore "mods/GreenFX" "output/.../NS_Effect.uasset"
```

---

## Other Commands

```bash
# Asset inspection
UAssetTool detect <uasset_path> [usmap_path]
UAssetTool dump <uasset_path> <usmap_path>
UAssetTool fix <uasset_path> [usmap_path]
UAssetTool batch_detect <directory> [usmap_path]
UAssetTool skeletal_mesh_info <uasset_path> <usmap_path>

# Zen conversion (single file)
UAssetTool to_zen <uasset_path> [usmap_path] [--no-material-tags]

# Zen inspection
UAssetTool inspect_zen <zen_asset_path>

# IoStore utilities
UAssetTool extract_script_objects <paks_path> <output_file>
UAssetTool recompress_iostore <utoc_path>
UAssetTool is_iostore_compressed <utoc_path>
UAssetTool clone_mod_iostore <utoc_path> <output_base>
UAssetTool cityhash <path_string>

# Blueprint analysis
UAssetTool scan_childbp_isenemy <paks_directory> [--aes <key>]
UAssetTool scan_childbp_isenemy <extracted_directory> --extracted
```

---

## Interactive JSON Mode

Run without arguments for a JSON stdin/stdout API (for GUI frontends):

```bash
UAssetTool
```

<details>
<summary>Available JSON Actions</summary>

**Asset Structure**
```json
{"action": "get_asset_summary", "file_path": "...", "usmap_path": "..."}
{"action": "get_name_map", "file_path": "..."}
{"action": "get_imports", "file_path": "..."}
{"action": "get_exports", "file_path": "..."}
{"action": "get_export_properties", "file_path": "...", "export_index": 0}
{"action": "get_export_raw_data", "file_path": "...", "export_index": 0}
```

**Property Editing**
```json
{"action": "set_property_value", "file_path": "...", "export_index": 0, "property_path": "MyProp", "property_value": 123}
{"action": "add_property", "file_path": "...", "export_index": 0, "property_name": "NewProp", "property_type": "int", "property_value": 42}
{"action": "remove_property", "file_path": "...", "export_index": 0, "property_path": "OldProp"}
```

**Save/Export**
```json
{"action": "save_asset", "file_path": "...", "output_path": "..."}
{"action": "export_to_json", "file_path": "..."}
{"action": "import_from_json", "file_path": "...", "json_data": "..."}
```

**Texture**
```json
{"action": "get_texture_info", "file_path": "..."}
{"action": "strip_mipmaps_native", "file_path": "...", "usmap_path": "..."}
{"action": "has_inline_texture_data", "file_path": "...", "usmap_path": "..."}
```

**Detection**
```json
{"action": "detect_texture", "file_path": "..."}
{"action": "detect_static_mesh", "file_path": "..."}
{"action": "detect_skeletal_mesh", "file_path": "..."}
{"action": "detect_blueprint", "file_path": "..."}
```

**Mod Creation**
```json
{"action": "create_mod_iostore", "output_path": "...", "input_dir": "...", "usmap_path": "...", "obfuscate": true}
{"action": "clone_mod_iostore", "file_path": "...", "output_path": "..."}
```

**Niagara**
```json
{"action": "niagara_list", "input_dir": "...", "usmap_path": "..."}
{"action": "niagara_details", "file_path": "...", "usmap_path": "..."}
```

</details>

---

## Project Structure

```
UassetToolRivals/
├── UAssetTool.sln
├── README.md
├── LICENSE
└── src/
    ├── UAssetTool/
    │   ├── Program.cs           # CLI entry point
    │   ├── UAssetTool.csproj
    │   ├── NiagaraService.cs    # Niagara particle editing
    │   ├── IoStore/             # IoStore/PAK reading & writing
    │   └── ZenPackage/          # Zen format conversion
    └── UAssetAPI/               # Core UAsset parsing (modified fork)
```

## Dependencies

- [UAssetAPI](https://github.com/atenfyr/UAssetAPI) - Core asset parsing (included, modified)
- [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) - JSON serialization
- [Blake3](https://www.nuget.org/packages/Blake3) - Hashing for IoStore
- [ZstdSharp](https://www.nuget.org/packages/ZstdSharp.Port) - Compression

## License

MIT License - See [LICENSE](LICENSE) for details.

## Acknowledgments

- [atenfyr/UAssetAPI](https://github.com/atenfyr/UAssetAPI) - Foundation for asset parsing
- [trumank/retoc](https://github.com/trumank/retoc) - Reference for Zen/IoStore format
