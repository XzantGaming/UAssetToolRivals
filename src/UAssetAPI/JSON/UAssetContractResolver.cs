using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.JSON
{
    public class UAssetContractResolver : DefaultContractResolver
    {
        public Dictionary<FName, string> ToBeFilled;

        protected override JsonConverter ResolveContractConverter(Type objectType)
        {
            if (typeof(FName).IsAssignableFrom(objectType))
            {
                return new FNameJsonConverter(ToBeFilled);
            }
            return base.ResolveContractConverter(objectType);
        }

        /// <summary>
        /// NativeAOT fix. Under AOT the collection-interface metadata of generic instantiations
        /// such as <c>List&lt;FString&gt;</c> can be trimmed, so the base resolver mis-detects them
        /// as plain objects and then fails with "unable to find a constructor". Force the correct
        /// array/dictionary contract by inspecting the generic type definition, and supply an
        /// Activator-based creator (which works under AOT for statically-referenced instantiations,
        /// unlike the reflection <c>GetConstructor</c> the base relies on). No-op under the JIT.
        /// </summary>
        // Direct (compile-time `new`) creators for the generic collection instantiations used in
        // the asset graph. Under NativeAOT, Activator.CreateInstance / reflection GetConstructor
        // cannot build a List<T>/Dictionary<K,V> instantiation, so each must be created directly.
        // Referencing them here also roots them for AOT. Extend as needed for other asset types.
        private static readonly Dictionary<Type, Func<object>> AotCollectionCreators = new()
        {
            { typeof(List<FString>), () => new List<FString>() },
            { typeof(List<FName>), () => new List<FName>() },
            { typeof(List<PropertyData>), () => new List<PropertyData>() },
            { typeof(List<FPackageIndex>), () => new List<FPackageIndex>() },
            { typeof(List<Import>), () => new List<Import>() },
            { typeof(List<Export>), () => new List<Export>() },
            { typeof(List<CustomVersion>), () => new List<CustomVersion>() },
            { typeof(List<FSoftObjectPath>), () => new List<FSoftObjectPath>() },
            { typeof(List<int>), () => new List<int>() },
            { typeof(List<int[]>), () => new List<int[]>() },
            { typeof(Dictionary<string, FString>), () => new Dictionary<string, FString>() },
            { typeof(Dictionary<FString, uint>), () => new Dictionary<FString, uint>() },
            { typeof(Dictionary<FName, string>), () => new Dictionary<FName, string>() },
        };

        static UAssetContractResolver()
        {
            // NativeAOT: force the runtime to keep the IList / IDictionary interface implementations
            // of every collection instantiation the JSON deserializer touches. Without these explicit
            // casts the interface metadata is trimmed, so Newtonsoft's IsReadOnlyOrFixedSize check
            // mis-classifies the list as read-only and builds a temporary collection via reflection
            // (which then fails on the generic ctor). With the interface kept, IsReadOnlyOrFixedSize is
            // correctly false and our DefaultCreator above is used. Must mirror AotCollectionCreators.
            // No-op behavior under the JIT.
            var keep = new List<object>
            {
                (System.Collections.IList)new List<FString>(),
                (System.Collections.IList)new List<FName>(),
                (System.Collections.IList)new List<PropertyData>(),
                (System.Collections.IList)new List<FPackageIndex>(),
                (System.Collections.IList)new List<Import>(),
                (System.Collections.IList)new List<Export>(),
                (System.Collections.IList)new List<CustomVersion>(),
                (System.Collections.IList)new List<FSoftObjectPath>(),
                (System.Collections.IList)new List<int>(),
                (System.Collections.IList)new List<int[]>(),
                (System.Collections.IDictionary)new Dictionary<string, FString>(),
                (System.Collections.IDictionary)new Dictionary<FString, uint>(),
                (System.Collections.IDictionary)new Dictionary<FName, string>(),
            };
            GC.KeepAlive(keep);
        }

        protected override JsonContract CreateContract(Type objectType)
        {
            if (objectType.IsGenericType && !objectType.IsGenericTypeDefinition
                && AotCollectionCreators.TryGetValue(objectType, out Func<object> creator))
            {
                Type def = objectType.GetGenericTypeDefinition();
                if (def == typeof(Dictionary<,>))
                {
                    JsonDictionaryContract dc = base.CreateDictionaryContract(objectType);
                    // OverrideCreator is consulted before the read-only/temporary-collection path,
                    // which under AOT fails (trimmed IDictionary metadata -> reflection ctor). Use
                    // our direct `new` instead. HasParameterizedCreator=false => called with no args.
                    dc.OverrideCreator = _ => creator();
                    dc.HasParameterizedCreator = false;
                    dc.DefaultCreator = creator;
                    return dc;
                }
                JsonArrayContract ac = base.CreateArrayContract(objectType);
                // Same as above: under AOT the IList interface metadata of e.g. List<PropertyData>
                // can be trimmed, so Newtonsoft mis-detects it as read-only and builds a temporary
                // collection via reflection (CreateTemporaryCollection -> CreateDefaultConstructor),
                // ignoring DefaultCreator. OverrideCreator is checked first and bypasses that path.
                ac.OverrideCreator = _ => creator();
                ac.HasParameterizedCreator = false;
                ac.DefaultCreator = creator;
                return ac;
            }
            return base.CreateContract(objectType);
        }

        public UAssetContractResolver(Dictionary<FName, string> toBeFilled) : base()
        {
            ToBeFilled = toBeFilled;
        }
    }
}
