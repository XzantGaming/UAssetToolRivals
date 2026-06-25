#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UAssetTool;

/// <summary>
/// NativeAOT FFI surface for in-process Rust -> C# calls. One JSON-in / JSON-out entry
/// point (<c>uat_invoke</c>) that reuses the exact <see cref="UAssetRequest"/> /
/// <see cref="UAssetResponse"/> contract as the stdin interactive mode — only the
/// transport changes (process pipe -> direct call).
///
/// AOT-safety: the request is deserialized with a source-generated context
/// (<see cref="FfiJsonContext"/>) and the response is written by hand with
/// <see cref="Utf8JsonWriter"/>. Neither needs runtime codegen, so both survive
/// NativeAOT (System.Text.Json's reflection serializer does not).
///
/// Memory: the returned UTF-8 buffer is allocated with <see cref="Marshal.AllocHGlobal"/>
/// and MUST be released by the caller via <c>uat_free</c>.
/// </summary>
public static class NativeExports
{
    [UnmanagedCallersOnly(EntryPoint = "uat_invoke")]
    public static IntPtr Invoke(IntPtr requestUtf8)
    {
        try
        {
            string requestJson = Marshal.PtrToStringUTF8(requestUtf8) ?? string.Empty;

            UAssetResponse response;
            try
            {
                var request = JsonSerializer.Deserialize(requestJson, FfiJsonContext.Default.UAssetRequest);
                response = request == null
                    ? new UAssetResponse { Success = false, Message = "Invalid JSON request" }
                    : Program.ProcessRequest(request);
            }
            catch (JsonException ex)
            {
                response = new UAssetResponse { Success = false, Message = $"Invalid JSON request: {ex.Message}" };
            }

            return StringToUtf8(SerializeResponse(response));
        }
        catch (Exception ex)
        {
            try
            {
                return StringToUtf8(SerializeResponse(
                    new UAssetResponse { Success = false, Message = $"FFI error: {ex.Message}" }));
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "uat_free")]
    public static void Free(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
            Marshal.FreeHGlobal(ptr);
    }

    /// <summary>Serialize a response to the same JSON shape the stdin path produces, without dynamic code.</summary>
    private static string SerializeResponse(UAssetResponse response)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("success", response.Success);
            writer.WriteString("message", response.Message ?? string.Empty);
            writer.WritePropertyName("data");
            WriteValue(writer, response.Data);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Recursively write an arbitrary Data value (dicts, lists, primitives, JsonElement, POCOs).</summary>
    private static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case bool b: w.WriteBooleanValue(b); break;
            case string s: w.WriteStringValue(s); break;
            case JsonElement je: je.WriteTo(w); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case ulong ul: w.WriteNumberValue(ul); break;
            case uint ui: w.WriteNumberValue(ui); break;
            case short sh: w.WriteNumberValue(sh); break;
            case ushort ush: w.WriteNumberValue(ush); break;
            case byte by: w.WriteNumberValue(by); break;
            case sbyte sb: w.WriteNumberValue(sb); break;
            case float f: w.WriteNumberValue(f); break;
            case double d: w.WriteNumberValue(d); break;
            case decimal dec: w.WriteNumberValue(dec); break;
            case byte[] bytes: w.WriteBase64StringValue(bytes); break;
            case IDictionary<string, object?> dict:
                w.WriteStartObject();
                foreach (var kv in dict) { w.WritePropertyName(kv.Key); WriteValue(w, kv.Value); }
                w.WriteEndObject();
                break;
            case IDictionary nonGenericDict:
                w.WriteStartObject();
                foreach (DictionaryEntry e in nonGenericDict) { w.WritePropertyName(e.Key?.ToString() ?? string.Empty); WriteValue(w, e.Value); }
                w.WriteEndObject();
                break;
            case IEnumerable enumerable:
                w.WriteStartArray();
                foreach (var item in enumerable) WriteValue(w, item);
                w.WriteEndArray();
                break;
            default:
                w.WriteStartObject();
                foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                    w.WritePropertyName(prop.Name);
                    WriteValue(w, prop.GetValue(value));
                }
                w.WriteEndObject();
                break;
        }
    }

    private static IntPtr StringToUtf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        IntPtr mem = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, mem, bytes.Length);
        Marshal.WriteByte(mem, bytes.Length, 0);
        return mem;
    }
}

/// <summary>Source-generated JSON context for the FFI request type (AOT-safe deserialization).</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(UAssetRequest))]
internal partial class FfiJsonContext : JsonSerializerContext
{
}
