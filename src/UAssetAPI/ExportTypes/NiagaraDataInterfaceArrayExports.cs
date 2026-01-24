using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// Export type for NiagaraDataInterfaceArrayColor assets.
    /// Provides structured access to ColorData array for particle effects.
    /// Colors are stored as LinearColor structs (RGBA).
    /// </summary>
    public class NiagaraDataInterfaceArrayColorExport : NormalExport
    {
        public List<FLinearColor> Colors { get; set; }
        private int _colorDataPropertyIndex = -1;

        public NiagaraDataInterfaceArrayColorExport(Export super) : base(super) { }
        public NiagaraDataInterfaceArrayColorExport(UAsset asset, byte[] extras) : base(asset, extras) { }
        public NiagaraDataInterfaceArrayColorExport() { }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);
            ParseColorData();
        }

        private void ParseColorData()
        {
            Colors = new List<FLinearColor>();
            if (Data == null) return;

            for (int i = 0; i < Data.Count; i++)
            {
                var prop = Data[i];
                if (prop.Name?.Value?.Value != "ColorData") continue;
                
                if (prop is ArrayPropertyData arrayProp)
                {
                    _colorDataPropertyIndex = i;
                    foreach (var elem in arrayProp.Value)
                    {
                        if (elem is StructPropertyData structProp && structProp.Value != null)
                        {
                            // LinearColor struct contains R, G, B, A float properties
                            float r = 0, g = 0, b = 0, a = 1;
                            foreach (var field in structProp.Value)
                            {
                                if (field is FloatPropertyData floatProp)
                                {
                                    string fieldName = field.Name?.Value?.Value ?? "";
                                    switch (fieldName)
                                    {
                                        case "R": r = floatProp.Value; break;
                                        case "G": g = floatProp.Value; break;
                                        case "B": b = floatProp.Value; break;
                                        case "A": a = floatProp.Value; break;
                                    }
                                }
                            }
                            Colors.Add(new FLinearColor(r, g, b, a));
                        }
                        else if (elem is LinearColorPropertyData linearProp)
                        {
                            Colors.Add(linearProp.Value);
                        }
                    }
                    break;
                }
            }
        }

        private void SyncColorDataToProperties()
        {
            if (Colors == null || _colorDataPropertyIndex < 0 || Data == null) return;
            if (Data[_colorDataPropertyIndex] is not ArrayPropertyData arrayProp) return;

            for (int i = 0; i < Colors.Count && i < arrayProp.Value.Length; i++)
            {
                var color = Colors[i];
                if (arrayProp.Value[i] is StructPropertyData structProp && structProp.Value != null)
                {
                    foreach (var field in structProp.Value)
                    {
                        if (field is FloatPropertyData floatProp)
                        {
                            string fieldName = field.Name?.Value?.Value ?? "";
                            switch (fieldName)
                            {
                                case "R": floatProp.Value = color.R; break;
                                case "G": floatProp.Value = color.G; break;
                                case "B": floatProp.Value = color.B; break;
                                case "A": floatProp.Value = color.A; break;
                            }
                        }
                    }
                }
                else if (arrayProp.Value[i] is LinearColorPropertyData linearProp)
                {
                    linearProp.Value = color;
                }
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            SyncColorDataToProperties();
            base.Write(writer);
        }

        public int ColorCount => Colors?.Count ?? 0;

        public void SetAllColors(float r, float g, float b, float a)
        {
            if (Colors == null) return;
            for (int i = 0; i < Colors.Count; i++)
                Colors[i] = new FLinearColor(r, g, b, a);
        }

        public void SetColor(int index, float r, float g, float b, float a)
        {
            if (Colors != null && index >= 0 && index < Colors.Count)
                Colors[index] = new FLinearColor(r, g, b, a);
        }

        public FLinearColor? GetColor(int index)
        {
            if (Colors != null && index >= 0 && index < Colors.Count)
                return Colors[index];
            return null;
        }
    }

    /// <summary>
    /// Export type for NiagaraDataInterfaceArrayFloat assets.
    /// Provides structured access to FloatData array for particle effects.
    /// Used for opacity, scale, intensity values.
    /// </summary>
    public class NiagaraDataInterfaceArrayFloatExport : NormalExport
    {
        public List<float> Values { get; set; }
        private int _floatDataPropertyIndex = -1;

        public NiagaraDataInterfaceArrayFloatExport(Export super) : base(super) { }
        public NiagaraDataInterfaceArrayFloatExport(UAsset asset, byte[] extras) : base(asset, extras) { }
        public NiagaraDataInterfaceArrayFloatExport() { }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);
            ParseFloatData();
        }

        private void ParseFloatData()
        {
            Values = new List<float>();
            if (Data == null) return;

            for (int i = 0; i < Data.Count; i++)
            {
                var prop = Data[i];
                if (prop.Name?.Value?.Value != "FloatData") continue;
                
                if (prop is ArrayPropertyData arrayProp)
                {
                    _floatDataPropertyIndex = i;
                    foreach (var elem in arrayProp.Value)
                    {
                        if (elem is FloatPropertyData floatProp)
                            Values.Add(floatProp.Value);
                    }
                    break;
                }
            }
        }

        private void SyncFloatDataToProperties()
        {
            if (Values == null || _floatDataPropertyIndex < 0 || Data == null) return;
            if (Data[_floatDataPropertyIndex] is not ArrayPropertyData arrayProp) return;

            for (int i = 0; i < Values.Count && i < arrayProp.Value.Length; i++)
            {
                if (arrayProp.Value[i] is FloatPropertyData floatProp)
                    floatProp.Value = Values[i];
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            SyncFloatDataToProperties();
            base.Write(writer);
        }

        public int ValueCount => Values?.Count ?? 0;

        public void SetAllValues(float value)
        {
            if (Values == null) return;
            for (int i = 0; i < Values.Count; i++)
                Values[i] = value;
        }

        public void SetValue(int index, float value)
        {
            if (Values != null && index >= 0 && index < Values.Count)
                Values[index] = value;
        }

        public float? GetValue(int index)
        {
            if (Values != null && index >= 0 && index < Values.Count)
                return Values[index];
            return null;
        }
    }

    /// <summary>
    /// Export type for NiagaraDataInterfaceArrayFloat3 assets.
    /// Provides structured access to VectorData array for particle effects.
    /// Can be used for RGB colors (without alpha) or 3D positions.
    /// </summary>
    public class NiagaraDataInterfaceArrayFloat3Export : NormalExport
    {
        public List<FShaderLUTVector3> Values { get; set; }
        private int _vectorDataPropertyIndex = -1;

        public NiagaraDataInterfaceArrayFloat3Export(Export super) : base(super) { }
        public NiagaraDataInterfaceArrayFloat3Export(UAsset asset, byte[] extras) : base(asset, extras) { }
        public NiagaraDataInterfaceArrayFloat3Export() { }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);
            ParseVectorData();
        }

        private void ParseVectorData()
        {
            Values = new List<FShaderLUTVector3>();
            if (Data == null) return;

            for (int i = 0; i < Data.Count; i++)
            {
                var prop = Data[i];
                if (prop.Name?.Value?.Value != "VectorData" && prop.Name?.Value?.Value != "InternalVectorData") continue;
                
                if (prop is ArrayPropertyData arrayProp)
                {
                    _vectorDataPropertyIndex = i;
                    foreach (var elem in arrayProp.Value)
                    {
                        if (elem is StructPropertyData structProp && structProp.Value != null)
                        {
                            float x = 0, y = 0, z = 0;
                            foreach (var field in structProp.Value)
                            {
                                if (field is FloatPropertyData floatProp)
                                {
                                    string fieldName = field.Name?.Value?.Value ?? "";
                                    switch (fieldName)
                                    {
                                        case "X": x = floatProp.Value; break;
                                        case "Y": y = floatProp.Value; break;
                                        case "Z": z = floatProp.Value; break;
                                    }
                                }
                            }
                            Values.Add(new FShaderLUTVector3(x, y, z));
                        }
                        else if (elem is VectorPropertyData vecProp)
                        {
                            Values.Add(new FShaderLUTVector3((float)vecProp.Value.X, (float)vecProp.Value.Y, (float)vecProp.Value.Z));
                        }
                    }
                    break;
                }
            }
        }

        private void SyncVectorDataToProperties()
        {
            if (Values == null || _vectorDataPropertyIndex < 0 || Data == null) return;
            if (Data[_vectorDataPropertyIndex] is not ArrayPropertyData arrayProp) return;

            for (int i = 0; i < Values.Count && i < arrayProp.Value.Length; i++)
            {
                var vec = Values[i];
                if (arrayProp.Value[i] is StructPropertyData structProp && structProp.Value != null)
                {
                    foreach (var field in structProp.Value)
                    {
                        if (field is FloatPropertyData floatProp)
                        {
                            string fieldName = field.Name?.Value?.Value ?? "";
                            switch (fieldName)
                            {
                                case "X": floatProp.Value = vec.X; break;
                                case "Y": floatProp.Value = vec.Y; break;
                                case "Z": floatProp.Value = vec.Z; break;
                            }
                        }
                    }
                }
                else if (arrayProp.Value[i] is VectorPropertyData vecProp)
                {
                    vecProp.Value = new FVector(vec.X, vec.Y, vec.Z);
                }
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            SyncVectorDataToProperties();
            base.Write(writer);
        }

        public int ValueCount => Values?.Count ?? 0;

        public void SetAllValues(float x, float y, float z)
        {
            if (Values == null) return;
            for (int i = 0; i < Values.Count; i++)
                Values[i] = new FShaderLUTVector3(x, y, z);
        }

        public void SetValue(int index, float x, float y, float z)
        {
            if (Values != null && index >= 0 && index < Values.Count)
                Values[index] = new FShaderLUTVector3(x, y, z);
        }

        public FShaderLUTVector3? GetValue(int index)
        {
            if (Values != null && index >= 0 && index < Values.Count)
                return Values[index];
            return null;
        }
    }

    /// <summary>
    /// Export type for NiagaraDataInterfaceArrayInt32 assets.
    /// Provides structured access to IntData array for particle effects.
    /// Used for indices, counts, flags.
    /// </summary>
    public class NiagaraDataInterfaceArrayInt32Export : NormalExport
    {
        public List<int> Values { get; set; }
        private int _intDataPropertyIndex = -1;

        public NiagaraDataInterfaceArrayInt32Export(Export super) : base(super) { }
        public NiagaraDataInterfaceArrayInt32Export(UAsset asset, byte[] extras) : base(asset, extras) { }
        public NiagaraDataInterfaceArrayInt32Export() { }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);
            ParseIntData();
        }

        private void ParseIntData()
        {
            Values = new List<int>();
            if (Data == null) return;

            for (int i = 0; i < Data.Count; i++)
            {
                var prop = Data[i];
                if (prop.Name?.Value?.Value != "IntData") continue;
                
                if (prop is ArrayPropertyData arrayProp)
                {
                    _intDataPropertyIndex = i;
                    foreach (var elem in arrayProp.Value)
                    {
                        if (elem is IntPropertyData intProp)
                            Values.Add(intProp.Value);
                    }
                    break;
                }
            }
        }

        private void SyncIntDataToProperties()
        {
            if (Values == null || _intDataPropertyIndex < 0 || Data == null) return;
            if (Data[_intDataPropertyIndex] is not ArrayPropertyData arrayProp) return;

            for (int i = 0; i < Values.Count && i < arrayProp.Value.Length; i++)
            {
                if (arrayProp.Value[i] is IntPropertyData intProp)
                    intProp.Value = Values[i];
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            SyncIntDataToProperties();
            base.Write(writer);
        }

        public int ValueCount => Values?.Count ?? 0;

        public void SetAllValues(int value)
        {
            if (Values == null) return;
            for (int i = 0; i < Values.Count; i++)
                Values[i] = value;
        }

        public void SetValue(int index, int value)
        {
            if (Values != null && index >= 0 && index < Values.Count)
                Values[index] = value;
        }

        public int? GetValue(int index)
        {
            if (Values != null && index >= 0 && index < Values.Count)
                return Values[index];
            return null;
        }
    }
}
