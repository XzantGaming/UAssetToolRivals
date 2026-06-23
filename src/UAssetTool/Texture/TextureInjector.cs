using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Pfim;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.ExportTypes.Texture;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
using UAssetAPI.PropertyTypes.Structs;

namespace UAssetTool.Texture;

/// <summary>
/// Supported compression formats for texture injection.
/// </summary>
public enum TextureCompressionFormat
{
    /// <summary>BC1/DXT1 - 4bpp, no alpha or 1-bit alpha</summary>
    BC1,
    /// <summary>BC3/DXT5 - 8bpp, smooth alpha gradient</summary>
    BC3,
    /// <summary>BC4 - 4bpp, single channel (grayscale)</summary>
    BC4,
    /// <summary>BC5 - 8bpp, two channels (normal maps)</summary>
    BC5,
    /// <summary>BC7 - 8bpp, high quality RGBA</summary>
    BC7,
    /// <summary>Uncompressed BGRA</summary>
    BGRA8
}

/// <summary>
/// Result of a texture injection operation.
/// </summary>
public class TextureInjectionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int MipCount { get; set; }
    public string? PixelFormat { get; set; }
    public long TotalDataSize { get; set; }
}

/// <summary>
/// Handles texture injection into UAsset files using UAssetAPI's object model.
/// Supports PNG, TGA, DDS input formats and BC1/BC3/BC5/BC7 compression.
/// </summary>
public class TextureInjector
{
    /// <summary>
    /// Inject an image into a Texture2D using the UAssetAPI object model (no binary patching).
    /// Mode B (generateMips=true): full mip chain in the cooked streaming layout — high-res mips →
    /// .uptnl (optional), mid mips → .ubulk (streaming), small mips inline in .uexp. Mode A
    /// (generateMips=false): a single inline mip in .uexp. The pixel format is preserved from the base
    /// texture; output dimensions adapt to the image. A two-pass write fixes inline DataResource
    /// SerialOffsets to their real .uexp positions.
    /// </summary>
    public static TextureInjectionResult InjectObjectModel(
        string baseUassetPath, string imagePath, string outputPath,
        bool generateMips = true, string? usmapPath = null)
    {
        var result = new TextureInjectionResult();
        try
        {
            if (!File.Exists(baseUassetPath)) { result.ErrorMessage = $"Base uasset not found: {baseUassetPath}"; return result; }
            Usmap? mappings = (!string.IsNullOrEmpty(usmapPath) && File.Exists(usmapPath)) ? new Usmap(usmapPath) : null;
            var asset = new UAsset(baseUassetPath, EngineVersion.VER_UE5_3, mappings);
            asset.UseSeparateBulkDataFiles = true;

            TextureExport? tex = null;
            foreach (var e in asset.Exports) if (e is TextureExport t) { tex = t; break; }
            if (tex?.PlatformData == null) { result.ErrorMessage = "Base asset has no parseable texture platform data (usmap required for unversioned assets)."; return result; }
            var pd = tex.PlatformData;

            // Keep the base texture's format so the PixelFormat FName stays valid.
            var fmt = DetectFormatFromUEName(pd.PixelFormat) ?? TextureCompressionFormat.BC7;

            using var image = LoadImage(imagePath);
            if (image == null) { result.ErrorMessage = $"Failed to load image: {imagePath}"; return result; }
            Console.WriteLine($"  Base format: {pd.PixelFormat} ({fmt}); new image: {image.Width}x{image.Height}");

            // Full mip chain DOWN TO 1x1 (cooked UE convention; the engine derives mip count from
            // dimensions and reads the whole chain — a truncated chain causes access violations).
            var mipImages = generateMips ? GenerateFullMipChain(image) : new List<Image<Rgba32>> { image.Clone() };
            var compressed = CompressMipmaps(mipImages, fmt);
            foreach (var mi in mipImages) mi.Dispose();
            Console.WriteLine($"  Generated {compressed.Count} mip(s): {string.Join(", ", compressed.Select(c => $"{c.Width}x{c.Height}"))}");

            // Keep the ImportedSize property in sync with the new dimensions (engine reads it).
            foreach (var prop in tex.Data)
            {
                if (prop is StructPropertyData sp && sp.Name?.Value?.Value == "ImportedSize"
                    && sp.Value.Count > 0 && sp.Value[0] is IntPointPropertyData ip)
                {
                    ip.Value = new[] { image.Width, image.Height };
                    Console.WriteLine($"  ImportedSize -> [{image.Width}, {image.Height}]");
                }
            }

            var origDR = asset.DataResources ?? new List<FObjectDataResource>();
            var outer = origDR.Count > 0 ? origDR[0].OuterIndex : FPackageIndex.FromRawIndex(0);

            var newMips = new List<FTexture2DMipMap>();
            var newDR = new List<FObjectDataResource>();
            // Inline (resident) mips: EObjectDataResourceFlags=None, LegacyBulkDataFlags = ForceInlinePayload|SingleUse
            // (0x48=72) — matches a real cooked texture; the game reads intent from LegacyBulkDataFlags.
            const EBulkDataFlags INLINE_FLAGS = EBulkDataFlags.BULKDATA_ForceInlinePayload | EBulkDataFlags.BULKDATA_SingleUse;
            // Streaming mips → .ubulk: PayloadAtEndOfFile|PayloadInSeperateFile|Force_NOT_InlinePayload|NoOffsetFixUp (0x10501).
            // Optional (high-res) mips → .uptnl: same + OptionalPayload (0x800) = 0x10D01. Matches the cooked layout:
            // the largest mip(s) are optional (.uptnl), mid mips stream (.ubulk), the smallest stay inline (.uexp).
            const uint STREAM_FLAGS = 0x10501;
            const uint OPTIONAL_FLAGS = 0x10D01;
            bool streaming = generateMips;   // Mode B; Mode A (no mips) = single inline mip
            var ubulk = new MemoryStream();
            var uptnl = new MemoryStream();
            for (int i = 0; i < compressed.Count; i++)
            {
                var c = compressed[i];
                int maxDim = Math.Max(c.Width, c.Height);
                if (!streaming || maxDim <= 64)
                {
                    // Inline (resident) in .uexp
                    var bd = new FByteBulkData(c.Data);
                    bd.Header.DataResourceIndex = i;
                    bd.Header.BulkDataFlags = INLINE_FLAGS;
                    newMips.Add(new FTexture2DMipMap(bd, c.Width, c.Height, 1));
                    newDR.Add(new FObjectDataResource(EObjectDataResourceFlags.None, 0, -1, c.Data.Length, c.Data.Length, outer, (uint)INLINE_FLAGS, 0));
                }
                else if (maxDim > 1024)
                {
                    // Optional high-res mip → .uptnl (empty inline Data; pixels go to .uptnl)
                    var bd = new FByteBulkData(Array.Empty<byte>());
                    bd.Header.DataResourceIndex = i;
                    bd.Header.BulkDataFlags = (EBulkDataFlags)OPTIONAL_FLAGS;
                    newMips.Add(new FTexture2DMipMap(bd, c.Width, c.Height, 1));
                    newDR.Add(new FObjectDataResource(EObjectDataResourceFlags.None, uptnl.Length, -1, c.Data.Length, c.Data.Length, outer, OPTIONAL_FLAGS, 0));
                    uptnl.Write(c.Data, 0, c.Data.Length);
                }
                else
                {
                    // Streaming mip → .ubulk (empty inline Data; pixels go to .ubulk)
                    var bd = new FByteBulkData(Array.Empty<byte>());
                    bd.Header.DataResourceIndex = i;
                    bd.Header.BulkDataFlags = (EBulkDataFlags)STREAM_FLAGS;
                    newMips.Add(new FTexture2DMipMap(bd, c.Width, c.Height, 1));
                    newDR.Add(new FObjectDataResource(EObjectDataResourceFlags.None, ubulk.Length, -1, c.Data.Length, c.Data.Length, outer, STREAM_FLAGS, 0));
                    ubulk.Write(c.Data, 0, c.Data.Length);
                }
            }
            pd.Mips = newMips;
            pd.SizeX = image.Width; pd.SizeY = image.Height;
            pd.FirstMipToSerialize = 0;
            pd.bIsVirtual = false;   // injected textures are regular Texture2D, NOT virtual textures
            asset.DataResources = newDR;

            string outDir = Path.GetDirectoryName(outputPath) ?? ".";
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            asset.Write(outputPath);

            // Write/clear .ubulk (streaming) and .uptnl (optional). Mode A leaves neither.
            string ubulkPath = Path.ChangeExtension(outputPath, ".ubulk");
            if (ubulk.Length > 0) { File.WriteAllBytes(ubulkPath, ubulk.ToArray()); Console.WriteLine($"  .ubulk: {ubulk.Length:N0} bytes"); }
            else if (File.Exists(ubulkPath)) File.Delete(ubulkPath);
            string uptnlPath = Path.ChangeExtension(outputPath, ".uptnl");
            if (uptnl.Length > 0) { File.WriteAllBytes(uptnlPath, uptnl.ToArray()); Console.WriteLine($"  .uptnl: {uptnl.Length:N0} bytes"); }
            else if (File.Exists(uptnlPath)) File.Delete(uptnlPath);

            // Pass 2: set inline DataResource SerialOffsets to their real .uexp positions (streaming
            // offsets already point into .ubulk). Find where the first inline mip's data landed.
            string outUexp = Path.ChangeExtension(outputPath, ".uexp");
            byte[] outBytes = File.ReadAllBytes(outUexp);
            int firstInline = newMips.FindIndex(m => m.BulkData.Data.Length > 0);
            int inlineStart = firstInline >= 0 ? IndexOfBytes(outBytes, newMips[firstInline].BulkData.Data) : -1;
            if (inlineStart >= 0)
            {
                long off = inlineStart;
                for (int i = 0; i < newDR.Count; i++)
                {
                    if (newMips[i].BulkData.Data.Length > 0) // inline mip → patch .uexp offset
                    {
                        var d = newDR[i];
                        newDR[i] = new FObjectDataResource(d.Flags, off, -1, d.SerialSize, d.RawSize, d.OuterIndex, d.LegacyBulkDataFlags, d.CookedIndex);
                        // Interleaved layout: next inline mip's data sits after this mip's dims (12) + the next mip's index (4).
                        off += d.RawSize + 16;
                    }
                    // streaming mips keep their .ubulk offset
                }
                asset.DataResources = newDR;
                asset.Write(outputPath);
                Console.WriteLine($"  Inline data at .uexp offset {inlineStart}; SerialOffsets fixed");
            }

            result.Success = true;
            result.Width = image.Width; result.Height = image.Height;
            result.MipCount = newMips.Count;
            result.PixelFormat = GetUEPixelFormatName(fmt);
            result.TotalDataSize = compressed.Sum(c => (long)c.Data.Length);
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"InjectObjectModel failed: {ex.Message}\n{ex.StackTrace}";
            return result;
        }
    }

    /// <summary>Find the first offset of needle within haystack (needle is large/unique here).</summary>
    private static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return -1;
        int last = haystack.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    /// <summary>
    /// Load an image from file (supports PNG, TGA, DDS, BMP, JPEG).
    /// </summary>
    private static Image<Rgba32>? LoadImage(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        
        if (ext == ".dds" || ext == ".tga")
        {
            // Use Pfim for DDS and TGA
            return LoadWithPfim(path);
        }
        else
        {
            // Use ImageSharp for PNG, BMP, JPEG, etc.
            return Image.Load<Rgba32>(path);
        }
    }
    
    /// <summary>
    /// Load DDS or TGA using Pfim library.
    /// </summary>
    private static Image<Rgba32>? LoadWithPfim(string path)
    {
        using var image = Pfimage.FromFile(path);
        
        // Decompress if needed (for DXT compressed textures)
        if (image.Compressed)
        {
            image.Decompress();
        }
        
        // Convert Pfim image to ImageSharp
        byte[] data = image.Data;
        int width = image.Width;
        int height = image.Height;
        int stride = image.Stride;
        int bytesPerPixel = image.BitsPerPixel / 8;
        
        var result = new Image<Rgba32>(width, height);
        
        switch (image.Format)
        {
            case Pfim.ImageFormat.Rgba32:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * stride + x * 4;
                        // BGRA format
                        result[x, y] = new Rgba32(data[i + 2], data[i + 1], data[i], data[i + 3]);
                    }
                }
                break;
                
            case Pfim.ImageFormat.Rgb24:
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * stride + x * 3;
                        // BGR format
                        result[x, y] = new Rgba32(data[i + 2], data[i + 1], data[i], 255);
                    }
                }
                break;
                
            default:
                // Generic handling based on bytes per pixel
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * stride + x * bytesPerPixel;
                        
                        if (bytesPerPixel >= 4)
                        {
                            result[x, y] = new Rgba32(data[i + 2], data[i + 1], data[i], data[i + 3]);
                        }
                        else if (bytesPerPixel == 3)
                        {
                            result[x, y] = new Rgba32(data[i + 2], data[i + 1], data[i], 255);
                        }
                        else if (bytesPerPixel == 1)
                        {
                            result[x, y] = new Rgba32(data[i], data[i], data[i], 255);
                        }
                    }
                }
                break;
        }
        
        return result;
    }
    
    /// <summary>
    /// Generate the FULL mipmap chain down to 1x1 (cooked UE convention), e.g. 4096 -> 13 mips.
    /// </summary>
    private static List<Image<Rgba32>> GenerateFullMipChain(Image<Rgba32> source)
    {
        var mips = new List<Image<Rgba32>> { source.Clone() };
        int width = source.Width, height = source.Height;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            var mip = source.Clone();
            mip.Mutate(x => x.Resize(width, height));
            mips.Add(mip);
        }
        return mips;
    }

    /// <summary>
    /// Compress mipmaps to the target BC format.
    /// </summary>
    private static List<CompressedMip> CompressMipmaps(List<Image<Rgba32>> mips, TextureCompressionFormat format)
    {
        var result = new List<CompressedMip>();
        
        if (format == TextureCompressionFormat.BGRA8)
        {
            // Uncompressed - just extract raw pixels
            foreach (var mip in mips)
            {
                byte[] data = new byte[mip.Width * mip.Height * 4];
                for (int y = 0; y < mip.Height; y++)
                {
                    for (int x = 0; x < mip.Width; x++)
                    {
                        var pixel = mip[x, y];
                        int i = (y * mip.Width + x) * 4;
                        data[i] = pixel.B;
                        data[i + 1] = pixel.G;
                        data[i + 2] = pixel.R;
                        data[i + 3] = pixel.A;
                    }
                }
                result.Add(new CompressedMip(mip.Width, mip.Height, data));
            }
        }
        else
        {
            // Use BCnEncoder for BC compression
            var encoder = new BcEncoder();
            encoder.OutputOptions.GenerateMipMaps = false; // We already have mips
            encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
            encoder.OutputOptions.Format = GetBCnFormat(format);
            
            foreach (var mip in mips)
            {
                // EncodeToRawBytes returns byte[][] (one array per mip), we want just the first
                byte[][] compressedMips = encoder.EncodeToRawBytes(mip);
                byte[] compressed = compressedMips.Length > 0 ? compressedMips[0] : Array.Empty<byte>();
                result.Add(new CompressedMip(mip.Width, mip.Height, compressed));
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Get BCnEncoder format from our enum.
    /// </summary>
    private static CompressionFormat GetBCnFormat(TextureCompressionFormat format)
    {
        return format switch
        {
            TextureCompressionFormat.BC1 => CompressionFormat.Bc1,
            TextureCompressionFormat.BC3 => CompressionFormat.Bc3,
            TextureCompressionFormat.BC4 => CompressionFormat.Bc4,
            TextureCompressionFormat.BC5 => CompressionFormat.Bc5,
            TextureCompressionFormat.BC7 => CompressionFormat.Bc7,
            _ => CompressionFormat.Bc7
        };
    }
    
    /// <summary>
    /// Get UE pixel format name string.
    /// </summary>
    private static string GetUEPixelFormatName(TextureCompressionFormat format)
    {
        return format switch
        {
            TextureCompressionFormat.BC1 => "PF_DXT1",
            TextureCompressionFormat.BC3 => "PF_DXT5",
            TextureCompressionFormat.BC4 => "PF_BC4",
            TextureCompressionFormat.BC5 => "PF_BC5",
            TextureCompressionFormat.BC7 => "PF_BC7",
            TextureCompressionFormat.BGRA8 => "PF_B8G8R8A8",
            _ => "PF_BC7"
        };
    }
    
    /// <summary>
    /// Detect compression format from UE pixel format name string.
    /// </summary>
    private static TextureCompressionFormat? DetectFormatFromUEName(string ueFormatName)
    {
        return ueFormatName switch
        {
            "PF_DXT1" => TextureCompressionFormat.BC1,
            "PF_DXT5" => TextureCompressionFormat.BC3,
            "PF_BC4" => TextureCompressionFormat.BC4,
            "PF_BC5" => TextureCompressionFormat.BC5,
            "PF_BC7" => TextureCompressionFormat.BC7,
            "PF_B8G8R8A8" => TextureCompressionFormat.BGRA8,
            _ => null
        };
    }
    
    /// <summary>
    /// Parse compression format from string.
    /// </summary>
    public static TextureCompressionFormat ParseFormat(string formatStr)
    {
        return formatStr.ToUpperInvariant() switch
        {
            "BC1" or "DXT1" => TextureCompressionFormat.BC1,
            "BC3" or "DXT5" => TextureCompressionFormat.BC3,
            "BC4" => TextureCompressionFormat.BC4,
            "BC5" => TextureCompressionFormat.BC5,
            "BC7" => TextureCompressionFormat.BC7,
            "BGRA8" or "BGRA" or "UNCOMPRESSED" => TextureCompressionFormat.BGRA8,
            _ => TextureCompressionFormat.BC7
        };
    }
}

/// <summary>
/// Represents a compressed mipmap level.
/// </summary>
public class CompressedMip
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    
    public CompressedMip(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }
}
