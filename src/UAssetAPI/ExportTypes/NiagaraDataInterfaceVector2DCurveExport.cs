using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// Export type for NiagaraDataInterfaceVector2DCurve assets.
    /// Provides structured access to ShaderLUT Vector2D data for particle effects.
    /// 
    /// The ShaderLUT contains pre-baked Vector2D values sampled from FRichCurve data.
    /// Values are stored as a flat float array (X, Y pairs) for GPU efficiency.
    /// Used for 2D curves like UV offsets, 2D positions, etc.
    /// </summary>
    public class NiagaraDataInterfaceVector2DCurveExport : NormalExport
    {
        /// <summary>
        /// The parsed ShaderLUT containing Vector2D values.
        /// Null if ShaderLUT property wasn't found.
        /// </summary>
        public FShaderLUTVector2Ds ShaderLUT { get; set; }

        /// <summary>
        /// Index of the ShaderLUT property in Data list (for reconstruction).
        /// </summary>
        private int _shaderLUTPropertyIndex = -1;

        public NiagaraDataInterfaceVector2DCurveExport(Export super) : base(super)
        {
        }

        public NiagaraDataInterfaceVector2DCurveExport(UAsset asset, byte[] extras) : base(asset, extras)
        {
        }

        public NiagaraDataInterfaceVector2DCurveExport()
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
                    ShaderLUT = new FShaderLUTVector2Ds();

                    // Parse float array - every 2 floats = 1 Vector2D (X, Y)
                    for (int j = 0; j + 1 < arrayProp.Value.Length; j += 2)
                    {
                        if (arrayProp.Value[j] is FloatPropertyData xProp &&
                            arrayProp.Value[j + 1] is FloatPropertyData yProp)
                        {
                            ShaderLUT.Values.Add(new FShaderLUTVector2D(xProp.Value, yProp.Value));
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
                if (floatIndex + 1 >= arrayProp.Value.Length) break;

                if (arrayProp.Value[floatIndex] is FloatPropertyData xProp)
                    xProp.Value = vec.X;
                if (arrayProp.Value[floatIndex + 1] is FloatPropertyData yProp)
                    yProp.Value = vec.Y;

                floatIndex += 2;
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            // Sync structured ShaderLUT back to property data
            SyncShaderLUTToProperties();
            
            base.Write(writer);
        }

        /// <summary>
        /// Set all values in the ShaderLUT to a specific Vector2D.
        /// </summary>
        public void SetAllValues(float x, float y)
        {
            if (ShaderLUT != null)
            {
                ShaderLUT.SetAllValues(x, y);
            }
        }

        /// <summary>
        /// Set a specific value in the ShaderLUT by index.
        /// </summary>
        public void SetValue(int index, float x, float y)
        {
            if (ShaderLUT != null)
            {
                ShaderLUT.SetValue(index, x, y);
            }
        }

        /// <summary>
        /// Get the number of Vector2D values in the ShaderLUT.
        /// </summary>
        public int ValueCount => ShaderLUT?.Values.Count ?? 0;

        /// <summary>
        /// Get a value by index.
        /// </summary>
        public FShaderLUTVector2D? GetValue(int index)
        {
            if (ShaderLUT != null && index >= 0 && index < ShaderLUT.Values.Count)
            {
                return ShaderLUT.Values[index];
            }
            return null;
        }
    }
}
