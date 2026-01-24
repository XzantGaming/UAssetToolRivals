using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// Export type for NiagaraDataInterfaceVectorCurve assets.
    /// Provides structured access to ShaderLUT Vector3 data for particle effects.
    /// 
    /// The ShaderLUT contains pre-baked Vector3 values sampled from FRichCurve data.
    /// Values are stored as a flat float array (X, Y, Z triplets) for GPU efficiency.
    /// Can be used for RGB colors (without alpha) or 3D positions/directions.
    /// </summary>
    public class NiagaraDataInterfaceVectorCurveExport : NormalExport
    {
        /// <summary>
        /// The parsed ShaderLUT containing Vector3 values.
        /// Null if ShaderLUT property wasn't found.
        /// </summary>
        public FShaderLUTVector3s ShaderLUT { get; set; }

        /// <summary>
        /// Index of the ShaderLUT property in Data list (for reconstruction).
        /// </summary>
        private int _shaderLUTPropertyIndex = -1;

        public NiagaraDataInterfaceVectorCurveExport(Export super) : base(super)
        {
        }

        public NiagaraDataInterfaceVectorCurveExport(UAsset asset, byte[] extras) : base(asset, extras)
        {
        }

        public NiagaraDataInterfaceVectorCurveExport()
        {
        }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);
            
            // After base.Read(), parse ShaderLUT from Data properties
            ParseShaderLUT();
        }

        /// <summary>
        /// Parse ShaderLUT from the Data properties into structured form.
        /// </summary>
        private void ParseShaderLUT()
        {
            if (Data == null) return;

            for (int i = 0; i < Data.Count; i++)
            {
                var prop = Data[i];
                if (prop.Name?.Value?.Value != "ShaderLUT") continue;
                
                if (prop is ArrayPropertyData arrayProp)
                {
                    _shaderLUTPropertyIndex = i;
                    ShaderLUT = new FShaderLUTVector3s();

                    // Parse float array - every 3 floats = 1 Vector3 (X, Y, Z)
                    for (int j = 0; j + 2 < arrayProp.Value.Length; j += 3)
                    {
                        if (arrayProp.Value[j] is FloatPropertyData xProp &&
                            arrayProp.Value[j + 1] is FloatPropertyData yProp &&
                            arrayProp.Value[j + 2] is FloatPropertyData zProp)
                        {
                            ShaderLUT.Values.Add(new FShaderLUTVector3(xProp.Value, yProp.Value, zProp.Value));
                        }
                    }
                    
                    break;
                }
            }
        }

        /// <summary>
        /// Sync ShaderLUT back to Data properties before writing.
        /// </summary>
        private void SyncShaderLUTToProperties()
        {
            if (ShaderLUT == null || _shaderLUTPropertyIndex < 0 || Data == null)
                return;

            if (Data[_shaderLUTPropertyIndex] is not ArrayPropertyData arrayProp)
                return;

            // Update float values from structured ShaderLUT
            int floatIndex = 0;
            foreach (var vec in ShaderLUT.Values)
            {
                if (floatIndex + 2 >= arrayProp.Value.Length) break;

                if (arrayProp.Value[floatIndex] is FloatPropertyData xProp)
                    xProp.Value = vec.X;
                if (arrayProp.Value[floatIndex + 1] is FloatPropertyData yProp)
                    yProp.Value = vec.Y;
                if (arrayProp.Value[floatIndex + 2] is FloatPropertyData zProp)
                    zProp.Value = vec.Z;

                floatIndex += 3;
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            // Sync structured ShaderLUT back to property data
            SyncShaderLUTToProperties();
            
            base.Write(writer);
        }

        /// <summary>
        /// Set all values in the ShaderLUT to a specific Vector3.
        /// </summary>
        public void SetAllValues(float x, float y, float z)
        {
            if (ShaderLUT != null)
            {
                ShaderLUT.SetAllValues(x, y, z);
            }
        }

        /// <summary>
        /// Set a specific value in the ShaderLUT by index.
        /// </summary>
        public void SetValue(int index, float x, float y, float z)
        {
            if (ShaderLUT != null)
            {
                ShaderLUT.SetValue(index, x, y, z);
            }
        }

        /// <summary>
        /// Get the number of Vector3 values in the ShaderLUT.
        /// </summary>
        public int ValueCount => ShaderLUT?.Values.Count ?? 0;

        /// <summary>
        /// Get a value by index.
        /// </summary>
        public FShaderLUTVector3? GetValue(int index)
        {
            if (ShaderLUT != null && index >= 0 && index < ShaderLUT.Values.Count)
            {
                return ShaderLUT.Values[index];
            }
            return null;
        }
    }
}
