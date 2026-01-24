using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// Export type for NiagaraDataInterfaceCurve assets.
    /// Provides structured access to ShaderLUT float data for particle effects.
    /// 
    /// The ShaderLUT contains pre-baked float values sampled from FRichCurve data.
    /// Values are stored as a flat float array for GPU efficiency.
    /// Used for single-channel curves like scale, opacity, speed, lifetime, etc.
    /// </summary>
    public class NiagaraDataInterfaceCurveExport : NormalExport
    {
        /// <summary>
        /// The parsed ShaderLUT containing float values.
        /// Null if ShaderLUT property wasn't found.
        /// </summary>
        public FShaderLUTFloats ShaderLUT { get; set; }

        /// <summary>
        /// Index of the ShaderLUT property in Data list (for reconstruction).
        /// </summary>
        private int _shaderLUTPropertyIndex = -1;

        public NiagaraDataInterfaceCurveExport(Export super) : base(super)
        {
        }

        public NiagaraDataInterfaceCurveExport(UAsset asset, byte[] extras) : base(asset, extras)
        {
        }

        public NiagaraDataInterfaceCurveExport()
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
                    ShaderLUT = new FShaderLUTFloats();

                    // Parse float array - each float is a single value
                    foreach (var valueProp in arrayProp.Value)
                    {
                        if (valueProp is FloatPropertyData floatProp)
                        {
                            ShaderLUT.Values.Add(new FShaderLUTFloat(floatProp.Value));
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
            for (int i = 0; i < ShaderLUT.Values.Count && i < arrayProp.Value.Length; i++)
            {
                if (arrayProp.Value[i] is FloatPropertyData floatProp)
                {
                    floatProp.Value = ShaderLUT.Values[i].Value;
                }
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            // Sync structured ShaderLUT back to property data
            SyncShaderLUTToProperties();
            
            base.Write(writer);
        }

        /// <summary>
        /// Set all values in the ShaderLUT to a specific value.
        /// </summary>
        public void SetAllValues(float value)
        {
            if (ShaderLUT != null)
            {
                ShaderLUT.SetAllValues(value);
            }
        }

        /// <summary>
        /// Set a specific value in the ShaderLUT by index.
        /// </summary>
        public void SetValue(int index, float value)
        {
            if (ShaderLUT != null)
            {
                ShaderLUT.SetValue(index, value);
            }
        }

        /// <summary>
        /// Get the number of values in the ShaderLUT.
        /// </summary>
        public int ValueCount => ShaderLUT?.Values.Count ?? 0;

        /// <summary>
        /// Get a value by index.
        /// </summary>
        public float? GetValue(int index)
        {
            if (ShaderLUT != null && index >= 0 && index < ShaderLUT.Values.Count)
            {
                return ShaderLUT.Values[index].Value;
            }
            return null;
        }
    }
}
