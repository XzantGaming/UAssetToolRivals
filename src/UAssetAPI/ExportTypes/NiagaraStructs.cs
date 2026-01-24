using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// A single color entry in the ShaderLUT.
    /// Stored as 4 consecutive floats (R, G, B, A) in the flat array.
    /// </summary>
    public struct FShaderLUTColor
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public FShaderLUTColor(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public FShaderLUTColor(AssetBinaryReader reader)
        {
            R = reader.ReadSingle();
            G = reader.ReadSingle();
            B = reader.ReadSingle();
            A = reader.ReadSingle();
        }

        public void Write(AssetBinaryWriter writer)
        {
            writer.Write(R);
            writer.Write(G);
            writer.Write(B);
            writer.Write(A);
        }

        public static int SerializedSize => 4 * 4; // 16 bytes

        public override string ToString() => $"({R:F3}, {G:F3}, {B:F3}, {A:F3})";
    }

    /// <summary>
    /// ShaderLUT - Shader Lookup Table for NiagaraDataInterfaceColorCurve.
    /// Contains pre-baked color values for GPU shader sampling.
    /// 
    /// Structure: Flat float array where every 4 floats = 1 RGBA color.
    /// Typical size: 256 colors (1024 floats) for smooth gradients.
    /// 
    /// Note: This is NOT stored as Array&lt;LinearColor&gt; - it's Array&lt;FloatProperty&gt;
    /// for GPU efficiency. The colors are sequential: [R0,G0,B0,A0, R1,G1,B1,A1, ...]
    /// </summary>
    public class FShaderLUT
    {
        /// <summary>
        /// The color entries in the LUT. Each entry is 4 floats (RGBA).
        /// </summary>
        public List<FShaderLUTColor> Colors { get; set; }

        public FShaderLUT()
        {
            Colors = new List<FShaderLUTColor>();
        }

        /// <summary>
        /// Read ShaderLUT from a flat float array.
        /// </summary>
        /// <param name="reader">Binary reader positioned at array start</param>
        /// <param name="floatCount">Number of floats in the array</param>
        public void Read(AssetBinaryReader reader, int floatCount)
        {
            Colors = new List<FShaderLUTColor>();
            int colorCount = floatCount / 4;
            
            for (int i = 0; i < colorCount; i++)
            {
                Colors.Add(new FShaderLUTColor(reader));
            }
            
            // Handle any remaining floats (shouldn't happen in valid data)
            int remainder = floatCount % 4;
            if (remainder > 0)
            {
                for (int i = 0; i < remainder; i++)
                {
                    reader.ReadSingle(); // Skip orphan floats
                }
            }
        }

        /// <summary>
        /// Write ShaderLUT as a flat float array.
        /// </summary>
        public void Write(AssetBinaryWriter writer)
        {
            foreach (var color in Colors)
            {
                color.Write(writer);
            }
        }

        /// <summary>
        /// Get the float count (for array serialization).
        /// </summary>
        public int FloatCount => Colors.Count * 4;

        /// <summary>
        /// Set all colors to a single value.
        /// </summary>
        public void SetAllColors(float r, float g, float b, float a)
        {
            for (int i = 0; i < Colors.Count; i++)
            {
                Colors[i] = new FShaderLUTColor(r, g, b, a);
            }
        }

        /// <summary>
        /// Set a specific color by index.
        /// </summary>
        public void SetColor(int index, float r, float g, float b, float a)
        {
            if (index >= 0 && index < Colors.Count)
            {
                Colors[index] = new FShaderLUTColor(r, g, b, a);
            }
        }
    }

    /// <summary>
    /// A single float value entry in a ShaderLUT for NiagaraDataInterfaceCurve.
    /// Stored as a single float in the flat array.
    /// </summary>
    public struct FShaderLUTFloat
    {
        public float Value;

        public FShaderLUTFloat(float value)
        {
            Value = value;
        }

        public static int SerializedSize => 4; // 4 bytes

        public override string ToString() => $"{Value:F3}";
    }

    /// <summary>
    /// A single Vector2D entry in a ShaderLUT for NiagaraDataInterfaceVector2DCurve.
    /// Stored as 2 consecutive floats (X, Y) in the flat array.
    /// </summary>
    public struct FShaderLUTVector2D
    {
        public float X;
        public float Y;

        public FShaderLUTVector2D(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static int SerializedSize => 2 * 4; // 8 bytes

        public override string ToString() => $"({X:F3}, {Y:F3})";
    }

    /// <summary>
    /// ShaderLUT for NiagaraDataInterfaceCurve - contains float values.
    /// Used for single-channel curves like scale, opacity, speed, etc.
    /// </summary>
    public class FShaderLUTFloats
    {
        public List<FShaderLUTFloat> Values { get; set; }

        public FShaderLUTFloats()
        {
            Values = new List<FShaderLUTFloat>();
        }

        public int FloatCount => Values.Count;

        public void SetAllValues(float value)
        {
            for (int i = 0; i < Values.Count; i++)
            {
                Values[i] = new FShaderLUTFloat(value);
            }
        }

        public void SetValue(int index, float value)
        {
            if (index >= 0 && index < Values.Count)
            {
                Values[index] = new FShaderLUTFloat(value);
            }
        }
    }

    /// <summary>
    /// A single Vector3 entry in a ShaderLUT for NiagaraDataInterfaceVectorCurve.
    /// Stored as 3 consecutive floats (X, Y, Z) in the flat array.
    /// Can represent RGB colors or 3D positions/directions.
    /// </summary>
    public struct FShaderLUTVector3
    {
        public float X;
        public float Y;
        public float Z;

        public FShaderLUTVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static int SerializedSize => 3 * 4; // 12 bytes

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    /// <summary>
    /// ShaderLUT for NiagaraDataInterfaceVectorCurve - contains Vector3 values.
    /// Used for 3D curves like RGB colors, positions, directions, etc.
    /// </summary>
    public class FShaderLUTVector3s
    {
        public List<FShaderLUTVector3> Values { get; set; }

        public FShaderLUTVector3s()
        {
            Values = new List<FShaderLUTVector3>();
        }

        public int FloatCount => Values.Count * 3;

        public void SetAllValues(float x, float y, float z)
        {
            for (int i = 0; i < Values.Count; i++)
            {
                Values[i] = new FShaderLUTVector3(x, y, z);
            }
        }

        public void SetValue(int index, float x, float y, float z)
        {
            if (index >= 0 && index < Values.Count)
            {
                Values[index] = new FShaderLUTVector3(x, y, z);
            }
        }
    }

    /// <summary>
    /// ShaderLUT for NiagaraDataInterfaceVector2DCurve - contains Vector2D values.
    /// Used for 2D curves like UV offsets, 2D positions, etc.
    /// </summary>
    public class FShaderLUTVector2Ds
    {
        public List<FShaderLUTVector2D> Values { get; set; }

        public FShaderLUTVector2Ds()
        {
            Values = new List<FShaderLUTVector2D>();
        }

        public int FloatCount => Values.Count * 2;

        public void SetAllValues(float x, float y)
        {
            for (int i = 0; i < Values.Count; i++)
            {
                Values[i] = new FShaderLUTVector2D(x, y);
            }
        }

        public void SetValue(int index, float x, float y)
        {
            if (index >= 0 && index < Values.Count)
            {
                Values[index] = new FShaderLUTVector2D(x, y);
            }
        }
    }

    /// <summary>
    /// Curve key for FRichCurve (used in Niagara curves before baking to ShaderLUT).
    /// </summary>
    public struct FRichCurveKey
    {
        public byte InterpMode;
        public byte TangentMode;
        public byte TangentWeightMode;
        public float Time;
        public float Value;
        public float ArriveTangent;
        public float ArriveTangentWeight;
        public float LeaveTangent;
        public float LeaveTangentWeight;

        public FRichCurveKey(AssetBinaryReader reader)
        {
            InterpMode = reader.ReadByte();
            TangentMode = reader.ReadByte();
            TangentWeightMode = reader.ReadByte();
            reader.ReadByte(); // Padding
            Time = reader.ReadSingle();
            Value = reader.ReadSingle();
            ArriveTangent = reader.ReadSingle();
            ArriveTangentWeight = reader.ReadSingle();
            LeaveTangent = reader.ReadSingle();
            LeaveTangentWeight = reader.ReadSingle();
        }

        public void Write(AssetBinaryWriter writer)
        {
            writer.Write(InterpMode);
            writer.Write(TangentMode);
            writer.Write(TangentWeightMode);
            writer.Write((byte)0); // Padding
            writer.Write(Time);
            writer.Write(Value);
            writer.Write(ArriveTangent);
            writer.Write(ArriveTangentWeight);
            writer.Write(LeaveTangent);
            writer.Write(LeaveTangentWeight);
        }

        public static int SerializedSize => 4 + (6 * 4); // 28 bytes
    }

    /// <summary>
    /// FRichCurve - Curve data used by Niagara for color animation.
    /// The curve keys define control points; the ShaderLUT contains baked samples.
    /// </summary>
    public class FRichCurve
    {
        public float DefaultValue;
        public byte PreInfinityExtrap;
        public byte PostInfinityExtrap;
        public List<FRichCurveKey> Keys;

        public FRichCurve()
        {
            Keys = new List<FRichCurveKey>();
        }

        public void Read(AssetBinaryReader reader)
        {
            DefaultValue = reader.ReadSingle();
            PreInfinityExtrap = reader.ReadByte();
            PostInfinityExtrap = reader.ReadByte();
            reader.ReadBytes(2); // Padding
            
            int keyCount = reader.ReadInt32();
            Keys = new List<FRichCurveKey>(keyCount);
            for (int i = 0; i < keyCount; i++)
            {
                Keys.Add(new FRichCurveKey(reader));
            }
        }

        public void Write(AssetBinaryWriter writer)
        {
            writer.Write(DefaultValue);
            writer.Write(PreInfinityExtrap);
            writer.Write(PostInfinityExtrap);
            writer.Write((short)0); // Padding
            
            writer.Write(Keys.Count);
            foreach (var key in Keys)
            {
                key.Write(writer);
            }
        }
    }
}
