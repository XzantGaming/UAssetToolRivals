using System;
using System.Collections.Generic;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetTool.ZenPackage;

/// <summary>
/// Reads a per-LOD <c>TArray&lt;bool&gt; HiddenMaterials</c> list authored on a
/// SkeletalMesh via an editor-side AssetUserData carrier (the same plugin pattern
/// used by <see cref="MaterialTagReader"/>) and injects it into each
/// <c>FSkeletalMeshLODInfo</c> entry as the proprietary
/// <c>DefaultHiddenMaterials: TArray&lt;bool&gt;</c> property at offset 0x0018.
///
/// Marvel Rivals' modified UE 5.3 inserted this field inside FSkeletalMeshLODInfo,
/// shifting every following member by 16 bytes. Vanilla-cooked SkeletalMeshes lack
/// this property, so the game's unversioned property reader walks past misaligned
/// offsets and crashes (see <c>skeletal_mesh_format_changes.md</c>).
///
/// Carrier format on the AssetUserData export (matching the editor plugin):
/// <code>
/// LODHiddenMaterials : TArray&lt;FRivalsLODHiddenMaterials&gt;
///   [i] FRivalsLODHiddenMaterials
///        HiddenMaterials : TArray&lt;bool&gt;
/// </code>
///
/// The carrier is left in the package; its imports are remapped to
/// <c>/Script/Engine.AssetUserData</c> by the same path used for MaterialTags
/// (see <c>ZenConverter.BuildImportMap</c>).
/// </summary>
public static class HiddenMaterialsReader
{
    /// <summary>
    /// Per-LOD list of hidden-material flags. Index = LOD index, value = bool[]
    /// where each entry is one material slot's "hidden by default" flag.
    /// </summary>
    public class HiddenMaterialsResult
    {
        public List<bool[]> PerLodFlags { get; } = new();
        public bool FoundUserData { get; set; }
        public List<string> Diagnostics { get; } = new();
    }

    /// <summary>
    /// Scan a UAsset for an AssetUserData carrier export and extract a
    /// <c>LODHiddenMaterials</c> property if present. Returns an empty
    /// (FoundUserData=false) result when nothing is found.
    /// </summary>
    public static HiddenMaterialsResult ReadFromAsset(UAsset asset)
    {
        var result = new HiddenMaterialsResult();
        if (asset == null)
        {
            result.Diagnostics.Add("Asset is null");
            return result;
        }

        // Search any AssetUserData carrier export for "LODHiddenMaterials".
        // We accept both MaterialTagAssetUserData (extended plugin) and a
        // dedicated RivalsMeshData class to keep the workflow flexible.
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            string? className = TryGetExportClassName(asset.Exports[i]);
            if (className == null) continue;

            bool looksLikeCarrier =
                className.Contains("MaterialTagAssetUserData", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("RivalsMeshData", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("RivalsLODHiddenMaterialsData", StringComparison.OrdinalIgnoreCase);

            if (!looksLikeCarrier) continue;
            if (asset.Exports[i] is not NormalExport carrier) continue;
            if (carrier.Data == null) continue;

            ArrayPropertyData? lodArrayProp = null;
            foreach (var prop in carrier.Data)
            {
                if (prop is ArrayPropertyData ap &&
                    string.Equals(ap.Name?.Value?.Value, "LODHiddenMaterials", StringComparison.OrdinalIgnoreCase))
                {
                    lodArrayProp = ap;
                    break;
                }
            }

            if (lodArrayProp?.Value == null || lodArrayProp.Value.Length == 0)
                continue;

            result.FoundUserData = true;
            result.Diagnostics.Add(
                $"Found LODHiddenMaterials on export[{i}] (class={className}), {lodArrayProp.Value.Length} LOD(s)");

            foreach (var lodEntry in lodArrayProp.Value)
            {
                if (lodEntry is not StructPropertyData lodStruct || lodStruct.Value == null)
                {
                    result.PerLodFlags.Add(Array.Empty<bool>());
                    continue;
                }

                bool[] flags = ExtractBoolArray(lodStruct, "HiddenMaterials");
                result.PerLodFlags.Add(flags);
                result.Diagnostics.Add(
                    $"  LOD[{result.PerLodFlags.Count - 1}]: {flags.Length} slot flag(s)");
            }

            // Found the carrier; stop scanning.
            return result;
        }

        result.Diagnostics.Add("No AssetUserData carrier with LODHiddenMaterials found");
        return result;
    }

    /// <summary>
    /// Inject (or update) <c>DefaultHiddenMaterials: TArray&lt;bool&gt;</c> into each
    /// <c>FSkeletalMeshLODInfo</c> struct in the export's <c>LODInfo</c> property.
    /// Returns the number of LOD entries that were modified.
    /// </summary>
    public static int InjectIntoLodInfo(NormalExport meshExport, HiddenMaterialsResult result, UAsset asset)
    {
        if (meshExport?.Data == null || result == null || !result.FoundUserData)
            return 0;

        ArrayPropertyData? lodInfoProp = null;
        foreach (var prop in meshExport.Data)
        {
            if (prop is ArrayPropertyData ap &&
                string.Equals(ap.Name?.Value?.Value, "LODInfo", StringComparison.OrdinalIgnoreCase))
            {
                lodInfoProp = ap;
                break;
            }
        }

        if (lodInfoProp?.Value == null || lodInfoProp.Value.Length == 0)
        {
            result.Diagnostics.Add("LODInfo property not found on mesh export — nothing injected");
            return 0;
        }

        int modified = 0;
        for (int lodIdx = 0; lodIdx < lodInfoProp.Value.Length; lodIdx++)
        {
            if (lodInfoProp.Value[lodIdx] is not StructPropertyData lodStruct)
                continue;
            lodStruct.Value ??= new List<PropertyData>();

            bool[] flags = lodIdx < result.PerLodFlags.Count
                ? result.PerLodFlags[lodIdx]
                : Array.Empty<bool>();

            // If no flags supplied for this LOD and no existing property,
            // skip — don't pad with empty arrays unless the user provided them.
            if (flags.Length == 0)
            {
                bool hasExisting = false;
                foreach (var p in lodStruct.Value)
                {
                    if (string.Equals(p?.Name?.Value?.Value, "DefaultHiddenMaterials", StringComparison.OrdinalIgnoreCase))
                    {
                        hasExisting = true;
                        break;
                    }
                }
                if (!hasExisting) continue;
            }

            BoolPropertyData[] boolProps = new BoolPropertyData[flags.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                // Inside an array, element names are unused for unversioned serialization,
                // but UAssetAPI requires a non-null FName. Use a stable dummy.
                var elemName = FName.DefineDummy(asset, i.ToString(), int.MinValue);
                boolProps[i] = new BoolPropertyData(elemName) { Value = flags[i] };
            }

            var dhmName = FName.FromString(asset, "DefaultHiddenMaterials");
            var boolPropTypeName = FName.DefineDummy(asset, "BoolProperty");
            var newProp = new ArrayPropertyData(dhmName)
            {
                ArrayType = boolPropTypeName,
                Value = boolProps
            };

            // Update in place if already present, else insert at index 3 (after
            // ScreenSize, LODHysteresis, LODMaterialMap), or — defensively —
            // immediately after LODMaterialMap if the order differs.
            int existingIdx = -1;
            int afterLodMaterialMapIdx = -1;
            for (int i = 0; i < lodStruct.Value.Count; i++)
            {
                string? n = lodStruct.Value[i]?.Name?.Value?.Value;
                if (n == null) continue;
                if (string.Equals(n, "DefaultHiddenMaterials", StringComparison.OrdinalIgnoreCase))
                    existingIdx = i;
                else if (string.Equals(n, "LODMaterialMap", StringComparison.OrdinalIgnoreCase))
                    afterLodMaterialMapIdx = i + 1;
            }

            if (existingIdx >= 0)
            {
                lodStruct.Value[existingIdx] = newProp;
            }
            else
            {
                int insertAt = afterLodMaterialMapIdx >= 0
                    ? afterLodMaterialMapIdx
                    : Math.Min(3, lodStruct.Value.Count);
                lodStruct.Value.Insert(insertAt, newProp);
            }

            modified++;
        }

        result.Diagnostics.Add($"Injected DefaultHiddenMaterials into {modified} LOD entry/entries");
        return modified;
    }

    /// <summary>
    /// Inverse of <see cref="InjectIntoLodInfo"/>: read the existing
    /// <c>DefaultHiddenMaterials</c> arrays out of a cooked SkeletalMesh export's
    /// <c>LODInfo</c> property. Returns one <c>bool[]</c> per LOD; LODs without
    /// the property yield an empty array. Used by tooling (e.g. MaterialSlotViewer)
    /// that wants to preview the game-side hidden-material flags.
    /// </summary>
    public static List<bool[]> ExtractFromCookedMesh(NormalExport meshExport)
    {
        var output = new List<bool[]>();
        if (meshExport?.Data == null) return output;

        ArrayPropertyData? lodInfoProp = null;
        foreach (var prop in meshExport.Data)
        {
            if (prop is ArrayPropertyData ap &&
                string.Equals(ap.Name?.Value?.Value, "LODInfo", StringComparison.OrdinalIgnoreCase))
            {
                lodInfoProp = ap;
                break;
            }
        }

        if (lodInfoProp?.Value == null) return output;

        foreach (var lodEntry in lodInfoProp.Value)
        {
            if (lodEntry is not StructPropertyData lodStruct || lodStruct.Value == null)
            {
                output.Add(Array.Empty<bool>());
                continue;
            }
            output.Add(ExtractBoolArray(lodStruct, "DefaultHiddenMaterials"));
        }
        return output;
    }

    /// <summary>
    /// Read a <c>TArray&lt;bool&gt; field</c> out of a struct's properties.
    /// </summary>
    private static bool[] ExtractBoolArray(StructPropertyData container, string fieldName)
    {
        if (container?.Value == null) return Array.Empty<bool>();

        foreach (var field in container.Value)
        {
            if (string.Equals(field?.Name?.Value?.Value, fieldName, StringComparison.OrdinalIgnoreCase) &&
                field is ArrayPropertyData arr && arr.Value != null)
            {
                var output = new bool[arr.Value.Length];
                for (int i = 0; i < arr.Value.Length; i++)
                {
                    if (arr.Value[i] is BoolPropertyData bp)
                        output[i] = bp.Value;
                }
                return output;
            }
        }
        return Array.Empty<bool>();
    }

    private static string? TryGetExportClassName(Export export)
    {
        try { return export.GetExportClassType()?.Value?.Value; }
        catch { return null; }
    }
}
