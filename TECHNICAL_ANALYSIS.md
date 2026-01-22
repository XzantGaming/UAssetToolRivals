# Technical Analysis: UAssetTool Internals

This document provides a detailed technical analysis of the processes and algorithms used in UAssetTool for Marvel Rivals asset conversion.

## Table of Contents

1. [Overview](#overview)
2. [PAK File Extraction](#pak-file-extraction)
3. [Mipmap Stripping](#mipmap-stripping)
4. [SkeletalMesh Processing](#skeletalmesh-processing)
5. [StaticMesh Processing](#staticmesh-processing)
6. [Zen Package Conversion](#zen-package-conversion)
7. [IoStore Container Format](#iostore-container-format)
8. [Export Map Building](#export-map-building)
9. [Import Resolution](#import-resolution)

---

## Overview

UAssetTool converts legacy Unreal Engine assets (`.uasset`/`.uexp`/`.ubulk`) to the Zen package format used by UE5.3+ games like Marvel Rivals. The conversion process involves:

1. Parsing the legacy asset structure
2. Applying game-specific patches (material padding, mipmap stripping)
3. Building the Zen package header with correct offsets
4. Creating IoStore containers (`.utoc`/`.ucas`) for game injection

---

## PAK File Extraction

### Overview

**Location:** `PakReader.cs`

PAK files are Unreal Engine's legacy archive format for packaging game assets. UAssetTool supports extracting assets from PAK v11 (UE5) files with various compression methods.

### PAK File Structure

```
┌─────────────────────────────────────────┐
│ Entry 0: FPakEntry Header + Data        │
├─────────────────────────────────────────┤
│ Entry 1: FPakEntry Header + Data        │
├─────────────────────────────────────────┤
│ ... more entries ...                    │
├─────────────────────────────────────────┤
│ Path Hash Index (optional)              │
├─────────────────────────────────────────┤
│ Full Directory Index                    │
├─────────────────────────────────────────┤
│ Primary Index                           │
├─────────────────────────────────────────┤
│ FPakInfo Footer (221 bytes for v11)     │
└─────────────────────────────────────────┘
```

### FPakInfo Footer Structure

```
Offset  Size  Field
──────  ────  ─────────────────────────────
+0      16    EncryptionKeyGuid
+16     1     bEncryptedIndex
+17     4     Magic (0x5A6F12E1)
+21     4     Version (11 for UE5)
+25     8     IndexOffset
+33     8     IndexSize
+41     20    IndexHash (SHA1)
+61     160   CompressionMethods (5 × 32 bytes)
──────  ────  ─────────────────────────────
Total:  221 bytes
```

### FPakEntry Header Structure

```
Offset  Size  Field
──────  ────  ─────────────────────────────
+0      8     Offset (within PAK file)
+8      8     CompressedSize
+16     8     UncompressedSize
+24     4     CompressionMethod index
+28     20    Hash (SHA1)
+48     4     BlockCount (if compressed)
+52     16×N  CompressionBlocks[N]
              - CompressedStart (8 bytes)
              - CompressedEnd (8 bytes)
```

### Encoded Entry Flags (Index)

The PAK index stores entries in an encoded format to save space:

```csharp
uint flags = reader.ReadUInt32();

// Bit layout:
// Bits 0-5:   CompressionBlockSize (raw value, shift << 11 for bytes)
// Bits 6-21:  CompressionBlocksCount
// Bit 22:     IsEncrypted
// Bits 23-28: CompressionSlot (method index)
// Bit 29:     IsSizeSafe (use 32-bit size)
// Bit 30:     IsUncompressedSizeSafe (use 32-bit size)
// Bit 31:     IsOffsetSafe (use 32-bit offset)
```

### Block Size Calculation

**Critical:** The compression block size is stored as a 6-bit value that must be shifted left by **11** (not 10):

```csharp
// Per CUE4Parse FPakEntry.cs:
compressionBlockSize = (bitfield & 0x3f) << 11;

// Example: 32 << 11 = 65536 (64KB standard block size)
```

### Block Offset Handling

Compression block offsets in FPakEntry are **relative to the entry start**:

```csharp
// Block offsets are relative to entry position
long absoluteBlockStart = entryOffset + block.CompressedStart;
long absoluteBlockEnd = entryOffset + block.CompressedEnd;
```

### Decompression Process

```
For each entry:
1. Read FPakEntry header at entry.Offset
2. If compressed:
   a. Read block count and block info
   b. For each block:
      - Seek to entryOffset + blockStart
      - Read compressed block data
      - Decompress using appropriate method
      - Append to output buffer
3. If encrypted:
   - Decrypt data using AES-256-ECB
   - Apply UE4's 4-byte chunk reversal pattern
```

### Supported Compression Methods

| Method | Description |
|--------|-------------|
| None | Uncompressed data |
| Zlib | Standard zlib compression |
| Gzip | Gzip compression |
| Oodle | Oodle Kraken/Leviathan/Mermaid |
| LZ4 | LZ4 fast compression |
| Zstd | Zstandard compression |

### AES Decryption

PAK files use AES-256-ECB with a custom byte-swapping pattern:

```csharp
// For each 16-byte block:
// 1. Reverse each 4-byte chunk BEFORE decryption
// 2. Decrypt with AES-ECB
// 3. Reverse each 4-byte chunk AFTER decryption

private static void ReverseChunks(byte[] block)
{
    for (int i = 0; i < 16; i += 4)
    {
        (block[i], block[i + 3]) = (block[i + 3], block[i]);
        (block[i + 1], block[i + 2]) = (block[i + 2], block[i + 1]);
    }
}
```

---

## Mipmap Stripping

### Purpose
Texture mods often only include the highest resolution mipmap. The game expects texture data to match the `NumMips` property, so we strip lower mipmaps and update metadata accordingly.

### Process

**Location:** `ZenConverter.cs` - `StripMipmapsFromTextureData()`

```
Original Texture Structure:
┌─────────────────────────────────┐
│ FTexturePlatformData Header     │
│ - SizeX, SizeY                  │
│ - NumSlices                     │
│ - PixelFormat                   │
│ - NumMips (e.g., 12)            │
├─────────────────────────────────┤
│ Mip 0 (4096x4096) - KEEP        │
├─────────────────────────────────┤
│ Mip 1 (2048x2048) - STRIP       │
├─────────────────────────────────┤
│ Mip 2 (1024x1024) - STRIP       │
├─────────────────────────────────┤
│ ... more mips - STRIP           │
└─────────────────────────────────┘
```

### Algorithm

1. **Detect texture data** by searching for pixel format signatures
2. **Read mip count** from FTexturePlatformData header
3. **Calculate mip 0 size** based on dimensions and pixel format
4. **Truncate data** to only include mip 0
5. **Update NumMips** property to 1
6. **Recalculate SerialSize** for the export

### Pixel Format Sizes

| Format | Bits Per Pixel | Block Size |
|--------|---------------|------------|
| DXT1/BC1 | 4 | 4x4 |
| DXT5/BC3 | 8 | 4x4 |
| BC7 | 8 | 4x4 |
| RGBA8 | 32 | 1x1 |

### Size Calculation
```csharp
int blockSize = 4; // For BC formats
int blocksX = (width + blockSize - 1) / blockSize;
int blocksY = (height + blockSize - 1) / blockSize;
int mipSize = blocksX * blocksY * bytesPerBlock;
```

---

## SkeletalMesh Processing

### FGameplayTagContainer Padding

**Location:** `ZenConverter.cs` - `PatchSkeletalMeshMaterialSlots()`

Marvel Rivals expects an `FGameplayTagContainer` after each `FSkeletalMaterial` in the materials array. Legacy assets don't have this field, so we inject it during conversion.

### FSkeletalMaterial Structure (Legacy)

```
Offset  Size  Field
──────  ────  ─────────────────────────────
+0      4     MaterialInterface (FPackageIndex)
+4      8     MaterialSlotName (FName)
+12     8     ImportedMaterialSlotName (FName)
+20     20    FMeshUVChannelInfo
              - bInitialized (1 byte)
              - bOverrideDensities (1 byte)
              - padding (2 bytes)
              - LocalUVDensities[4] (16 bytes)
──────  ────  ─────────────────────────────
Total:  40 bytes
```

### FSkeletalMaterial Structure (Marvel Rivals Zen)

```
Offset  Size  Field
──────  ────  ─────────────────────────────
+0      4     MaterialInterface (FPackageIndex)
+4      8     MaterialSlotName (FName)
+12     8     ImportedMaterialSlotName (FName)
+20     20    FMeshUVChannelInfo
+40     4     FGameplayTagContainer (count=0)
──────  ────  ─────────────────────────────
Total:  44 bytes
```

### Patching Algorithm

1. **Find material array** by searching for pattern:
   - `int32` count (1-50)
   - Followed by negative `FPackageIndex` values spaced 40 bytes apart

2. **Validate pattern** by checking multiple consecutive materials

3. **Create patched buffer** with size = original + (materialCount × 4)

4. **For each material:**
   - Copy 40 bytes of material data
   - Insert 4 bytes of zeros (empty FGameplayTagContainer)

5. **Copy remaining data** after material array

### Detection Pattern
```csharp
// Search for: count + materials at 40-byte intervals
for (int i = 4; i < dataLength - 80; i++)
{
    int count = BitConverter.ToInt32(data, i);
    if (count < 1 || count > 50) continue;
    
    int firstPkgIdx = BitConverter.ToInt32(data, i + 4);
    if (firstPkgIdx >= 0 || firstPkgIdx < -100) continue;
    
    // Verify subsequent materials are 40 bytes apart
    bool valid = true;
    for (int m = 1; m < count; m++)
    {
        int pkgIdx = BitConverter.ToInt32(data, i + 4 + (m * 40));
        if (pkgIdx >= 0 || pkgIdx < -100) { valid = false; break; }
    }
    
    if (valid) return (offset: i, count: count);
}
```

---

## StaticMesh Processing

### FStaticMaterial Structure

StaticMesh uses a different material structure than SkeletalMesh. For Marvel Rivals, the struct is 36 bytes without padding.

```
Offset  Size  Field
──────  ────  ─────────────────────────────
+0      4     MaterialInterface (FPackageIndex)
+4      8     MaterialSlotName (FName)
+12     4     OverlayMaterialInterface (FPackageIndex)
+16     20    FMeshUVChannelInfo
──────  ────  ─────────────────────────────
Total:  36 bytes
```

### Note on StaticMesh Padding

Unlike SkeletalMesh, StaticMesh in Marvel Rivals does **not** require FGameplayTagContainer padding. The materials are serialized at 36-byte intervals without additional fields.

---

## Zen Package Conversion

### Overview

**Location:** `ZenConverter.cs` - `WriteZenPackage()`

The Zen package format is UE5's optimized asset format for IoStore containers. Converting from legacy involves:

1. Building the Zen header with all required sections
2. Transforming import/export maps to Zen format
3. Creating export bundles for dependency ordering
4. Writing the combined package data

### Zen Package Structure

```
┌─────────────────────────────────────────┐
│ FZenPackageSummary (variable size)      │
│ - Magic, HeaderSize, Name, Flags        │
│ - Section offsets                       │
├─────────────────────────────────────────┤
│ Name Map (FNameEntrySerialized[])       │
│ - Hashes and string data                │
├─────────────────────────────────────────┤
│ Imported Public Export Hashes           │
│ - uint64[] for each public import       │
├─────────────────────────────────────────┤
│ Import Map (FPackageObjectIndex[])      │
│ - Type + Hash pairs for imports         │
├─────────────────────────────────────────┤
│ Export Map (FExportMapEntry[])          │
│ - CookedSerialOffset, CookedSerialSize  │
│ - ObjectFlags, PublicExportHash         │
│ - Outer/Class/Super/Template indices    │
├─────────────────────────────────────────┤
│ Export Bundle Entries                   │
│ - CommandType (Create/Serialize)        │
│ - LocalExportIndex                      │
├─────────────────────────────────────────┤
│ Dependency Bundle Headers               │
│ - FirstEntryIndex, EntryCount           │
├─────────────────────────────────────────┤
│ Dependency Bundle Entries               │
│ - LocalImportOrExportIndex              │
├─────────────────────────────────────────┤
│ Imported Package Names                  │
│ - FName references to imported packages │
├─────────────────────────────────────────┤
│ [Preload Data - between Header and      │
│  CookedHeaderSize]                      │
├─────────────────────────────────────────┤
│ Export Data (serialized exports)        │
│ - Actual asset data                     │
└─────────────────────────────────────────┘
```

### Key Calculations

#### HeaderSize vs CookedHeaderSize

```
HeaderSize = Size of Zen header (up to end of imported package names)
CookedHeaderSize = HeaderSize + PreloadSize

Preload data sits between HeaderSize and CookedHeaderSize.
Export serial offsets are relative to CookedHeaderSize.
```

#### Export Serial Size Calculation

```csharp
// For each export, calculate actual serialized size
long actualSize = exportEnd - exportStart;

// Add material padding for SkeletalMesh (last export)
if (isSkeletalMesh && isLastExport)
    actualSize += materialCount * 4;

export.CookedSerialSize = actualSize;
```

---

## IoStore Container Format

### Overview

IoStore is UE5's container format consisting of:
- `.utoc` - Table of Contents (chunk metadata)
- `.ucas` - Container Archive Store (actual data)
- `.pak` - Companion PAK with chunk names (for mod loading)

### UTOC Structure

```
┌─────────────────────────────────────────┐
│ FIoStoreTocHeader                       │
│ - Magic (0x5F3F3F5F)                    │
│ - Version                               │
│ - ChunkCount, CompressedBlockCount      │
│ - DirectoryIndexSize                    │
├─────────────────────────────────────────┤
│ Chunk IDs (FIoChunkId[])                │
│ - 12 bytes each: ID + Type              │
├─────────────────────────────────────────┤
│ Chunk Offsets (FIoOffsetAndLength[])    │
│ - Offset into .ucas + Length            │
├─────────────────────────────────────────┤
│ Compression Blocks                      │
│ - Block metadata for decompression      │
├─────────────────────────────────────────┤
│ Directory Index                         │
│ - Path to chunk mapping                 │
├─────────────────────────────────────────┤
│ Chunk Perfect Hash Seeds                │
│ - For fast chunk lookup                 │
└─────────────────────────────────────────┘
```

### Chunk Types

| Type | Value | Description |
|------|-------|-------------|
| ExportBundleData | 0 | Package export data |
| BulkData | 1 | Bulk data (textures, etc.) |
| OptionalBulkData | 2 | Optional bulk data |
| MemoryMappedBulkData | 3 | Memory-mapped data |
| ScriptObjects | 4 | Script object database |
| ContainerHeader | 5 | Container metadata |
| ExternalFile | 6 | External file reference |
| ShaderCodeLibrary | 7 | Shader code |
| ShaderCode | 8 | Individual shaders |
| PackageStoreEntry | 9 | Package store entry |

### Chunk ID Calculation

```csharp
// Package chunk ID from package path
ulong packageId = CityHash64(packagePath.ToLower());
FIoChunkId chunkId = new FIoChunkId(packageId, 0, EIoChunkType.ExportBundleData);

// Bulk data chunk ID
FIoChunkId bulkChunkId = new FIoChunkId(packageId, 0, EIoChunkType.BulkData);
```

---

## Export Map Building

### Overview

**Location:** `ZenConverter.cs` - `BuildExportMapWithRecalculatedSizes()`

The export map defines each export's location and metadata in the Zen package.

### FExportMapEntry Structure (UE5.3+)

```
Offset  Size  Field
──────  ────  ─────────────────────────────
+0      8     CookedSerialOffset
+8      8     CookedSerialSize
+16     4     ObjectName (FMappedName)
+20     8     OuterIndex (FPackageObjectIndex)
+28     8     ClassIndex (FPackageObjectIndex)
+36     8     SuperIndex (FPackageObjectIndex)
+44     8     TemplateIndex (FPackageObjectIndex)
+52     8     PublicExportHash
+60     4     ObjectFlags
+64     1     FilterFlags
+65     3     Padding
──────  ────  ─────────────────────────────
Total:  72 bytes
```

### Serial Size Calculation

```csharp
// Calculate from legacy export positions
for (int i = 0; i < exports.Count; i++)
{
    long start = exports[i].SerialOffset - firstExportOffset;
    long end = (i + 1 < exports.Count) 
        ? exports[i + 1].SerialOffset - firstExportOffset
        : totalDataLength;
    
    long size = end - start;
    
    // Add padding for mesh exports
    if (isLastExport && materialPadding > 0)
        size += materialPadding;
    
    zenExport.CookedSerialSize = size;
}
```

### Public Export Hash

Public exports (accessible from other packages) need a hash for lookup:

```csharp
if (export.IsPublic)
{
    string fullPath = $"{packageName}.{exportName}";
    ulong hash = CityHash64(fullPath.ToLower());
    zenExport.PublicExportHash = hash;
}
```

---

## Import Resolution

### Overview

**Location:** `ZenConverter.cs` - `BuildImportMap()`

Imports reference objects from other packages. In Zen format, imports are resolved via script object hashes.

### Script Object Database

The game's `global.utoc` contains a script objects database mapping class paths to hashes:

```
/Script/Engine.StaticMesh -> 0x407CDE1A593E47CF
/Script/Engine.SkeletalMesh -> 0x6623523DEF01A2F7
/Script/Engine.Texture2D -> 0x...
```

### Import Types

1. **Script Objects** - Engine classes (StaticMesh, Texture2D, etc.)
   - Resolved via script object hash lookup
   - Type = ScriptImport

2. **Package Imports** - Assets from other packages
   - Resolved via package ID + export hash
   - Type = PackageImport

### FPackageObjectIndex Structure

```csharp
// 8 bytes total
enum EType : uint { Export = 0, ScriptImport = 1, PackageImport = 2, Null = 3 }

// Encoding:
// Bits 0-61: Value (hash or index)
// Bits 62-63: Type
ulong encoded = (value & 0x3FFFFFFFFFFFFFFF) | ((ulong)type << 62);
```

### Hash Lookup Process

```csharp
// For script imports (e.g., /Script/Engine.StaticMesh)
string fullPath = $"/Script/{packageName}.{className}";
ulong hash = ScriptObjectsDatabase.GetHash(fullPath);

// For package imports (e.g., MI_Character_Body)
string packagePath = import.PackagePath;
ulong packageId = CityHash64(packagePath.ToLower());
ulong exportHash = CityHash64($"{packagePath}.{exportName}".ToLower());
```

---

## Export Bundle Ordering

### Purpose

Export bundles define the order in which exports are created and serialized. Dependencies must be created before dependents.

### Bundle Entry Types

| Type | Value | Description |
|------|-------|-------------|
| ExportCommandType_Create | 0 | Create the export object |
| ExportCommandType_Serialize | 1 | Serialize the export data |

### Ordering Algorithm

```csharp
// 1. Build dependency graph
foreach (export in exports)
{
    if (export.OuterIndex > 0)
        dependencies[export].Add(exports[export.OuterIndex - 1]);
}

// 2. Topological sort for Create commands
List<int> createOrder = TopologicalSort(exports, dependencies);

// 3. Serialize in same order
List<ExportBundleEntry> entries = new();
foreach (int idx in createOrder)
    entries.Add(new ExportBundleEntry(idx, Create));
foreach (int idx in createOrder)
    entries.Add(new ExportBundleEntry(idx, Serialize));
```

---

## PackageGuid Handling

### Issue

Cooked assets in UE5 expect `PackageGuid` to be all zeros. Non-zero GUIDs cause loading failures.

### Solution

```csharp
// In WriteZenPackage, ensure GUID is zeroed
zenPackage.Summary.SavedHash = new FMD5Hash(); // All zeros
```

---

## Preload Data

### Purpose

Preload data contains dependency information that the engine reads before deserializing exports.

### Calculation

```csharp
// Preload size = dependency count * 4 bytes + header overhead
int preloadSize = preloadDependencyCount * 4;
if (preloadSize > 0)
    preloadSize += 37; // Header overhead

// CookedHeaderSize includes preload
int cookedHeaderSize = zenHeaderSize + preloadSize;
```

### Structure

```
┌─────────────────────────────────────────┐
│ Per-export dependency counts (4 bytes)  │
├─────────────────────────────────────────┤
│ Dependency indices (4 bytes each)       │
└─────────────────────────────────────────┘
```

---

## Summary

The conversion process handles numerous UE5-specific requirements:

1. **Mipmap Stripping** - Reduces texture size while maintaining compatibility
2. **Material Padding** - Adds FGameplayTagContainer for Marvel Rivals SkeletalMesh
3. **Export Map** - Recalculates serial sizes including padding
4. **Import Resolution** - Maps legacy imports to Zen script object hashes
5. **Export Bundling** - Orders exports by dependency for correct loading
6. **PackageGuid** - Zeros GUID for cooked asset compatibility
7. **Preload Data** - Calculates correct CookedHeaderSize

Each step is critical for the game to successfully load modded assets.
