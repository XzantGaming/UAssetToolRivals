using UAssetAPI;
using UAssetAPI.UnrealTypes;
var asset = new UAsset(@"E:\WindsurfCoding\UasseToolRivals\IOStoreTest\SkelMEshReference\SK_1057_1057001.uasset", EngineVersion.VER_UE5_3);
Console.WriteLine($"Imports: {asset.Imports.Count}");
for (int i = 0; i < asset.Imports.Count; i++) {
    var imp = asset.Imports[i];
    Console.WriteLine($"[{i}] Outer={imp.OuterIndex.Index}, ClassPkg={imp.ClassPackage?.Value?.Value}, Class={imp.ClassName?.Value?.Value}, Name={imp.ObjectName?.Value?.Value}");
}
