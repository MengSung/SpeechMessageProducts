/*
檔案整體說明
目的與定位：讀取打包後 Workflow 活動 DLL 的 PE 中繼資料，將活動及其繼承鏈上的 `[Input]` 屬性轉為可供樣板 planner 驗證的不可變契約。
主要流程：以唯讀 FileStream 與 PEReader 讀取有界組件；驗證組件名稱及固定版本 1.0.0.0，依完整型別名稱定位活動，沿同一組件的基底型別鏈收集 `[Input]`；從 CustomAttribute blob 解碼輸入標籤，從 property signature 取得 `InArgument<T>` 的 T 型別，再產生脫離 PE reader 的唯讀結果。
依賴與邊界：輸入路徑只能由 Deployment CLI 的受控 artifacts/plugins 位置提供；本類別不決定組織授權、不載入或執行 DLL、不反射、不呼叫 SDK，也不輸出檔案路徑、組件原始內容或 attribute blob。
資源與隔離：每次讀取上限 16 MiB；檔案、MemoryStream、PEReader 及 metadata handle 僅於同步呼叫內使用，離開方法即 Dispose；結果只含字串與 Version，不保留 reader、path、服務或組織／使用者狀態，沒有靜態快取或背景工作。
失敗與驗證：組件身份／版本不符、活動不存在、非 InputAttribute、損毀 metadata、重複／空白標籤、非 InArgument<T> 或無法解析的型別均 fail closed，錯誤不回顯原始中繼資料。Roslyn 合成 PE 測試只驗解析器，不代替正式打包 DLL 核對。
*/
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>單一 `[Input]` 標籤對應的底層 CLR 值型別。</summary>
internal sealed record WorkflowInputDefinition(string Name, string ValueType, bool Required);

/// <summary>一次 PE 讀取後脫離 metadata reader 的活動輸入契約。</summary>
internal sealed record WorkflowActivityContract(
    string AssemblyName,
    Version Version,
    string PublicKeyToken,
    string ActivityType,
    IReadOnlyDictionary<string, WorkflowInputDefinition> Inputs);

/// <summary>不載入組件程式碼，直接以 PE metadata 解析活動輸入宣告。</summary>
internal static class ActivityInputContractReader
{
    private const int MaximumAssemblyBytes = 16 * 1024 * 1024;
    private const string InputAttributeName = "Microsoft.Xrm.Sdk.Workflow.InputAttribute";
    private const string RequiredArgumentAttributeName = "System.Activities.RequiredArgumentAttribute";
    private const string InArgumentPrefix = "System.Activities.InArgument`1<";
    private static readonly Version RequiredVersion = new(1, 0, 0, 0);

    /// <summary>
    /// 讀取指定組件／活動的 `[Input]` 屬性。任何身份、版本或型別契約不完整都拒絕，不回傳部分輸入清單。
    /// 執行期只短暫保留受限組件位元組，所有 PE 資源於返回前釋放。
    /// </summary>
    /// <param name="path">Deployment 已核定之 artifacts/plugins DLL 路徑。</param>
    /// <param name="expectedAssemblyName">呼叫端由 manifest 固定取得的組件簡稱。</param>
    /// <param name="activityType">完整 CLR 活動型別名稱。</param>
    /// <returns>只包含組件識別、活動型別與標籤至 CLR 型別的唯讀契約。</returns>
    /// <exception cref="InvalidDataException">組件或活動身份不符、輸入 metadata 無效或檔案損毀。</exception>
    internal static WorkflowActivityContract Read(string path, string expectedAssemblyName, string activityType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);

        try
        {
            byte[] content;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > MaximumAssemblyBytes)
                    throw Invalid();
                content = new byte[checked((int)stream.Length)];
                stream.ReadExactly(content);
                if (stream.ReadByte() != -1)
                    throw Invalid();
            }

            using var metadataStream = new MemoryStream(content, writable: false);
            using var peReader = new PEReader(metadataStream);
            if (!peReader.HasMetadata)
                throw Invalid();

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                throw Invalid();
            var assembly = reader.GetAssemblyDefinition();
            var assemblyName = reader.GetString(assembly.Name);
            var version = assembly.Version;
            if (!string.Equals(assemblyName, expectedAssemblyName, StringComparison.Ordinal) || version != RequiredVersion)
                throw Invalid();

            var types = reader.TypeDefinitions.ToDictionary(handle => FullName(reader, handle), StringComparer.Ordinal);
            if (!types.TryGetValue(activityType, out var activityHandle))
                throw Invalid();

            var inputs = ReadInputs(reader, activityHandle, types);
            return new WorkflowActivityContract(
                assemblyName,
                version,
                PublicKeyToken(reader, assembly),
                activityType,
            new ReadOnlyDictionary<string, WorkflowInputDefinition>(inputs));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or
            ArgumentException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw Invalid();
        }
    }

    /// <summary>依實際 CLR 繼承鏈收集同組件型別的輸入，拒絕重複名稱與無法解析的 base type。</summary>
    private static Dictionary<string, WorkflowInputDefinition> ReadInputs(
        MetadataReader reader,
        TypeDefinitionHandle activityHandle,
        Dictionary<string, TypeDefinitionHandle> types)
    {
        var hierarchy = new List<TypeDefinitionHandle>();
        var visited = new HashSet<TypeDefinitionHandle>();
        var current = activityHandle;
        while (!current.IsNil)
        {
            if (!visited.Add(current) || hierarchy.Count >= 128)
                throw Invalid();
            hierarchy.Add(current);
            var baseType = reader.GetTypeDefinition(current).BaseType;
            if (baseType.Kind != HandleKind.TypeDefinition)
                break;
            var name = FullName(reader, (TypeDefinitionHandle)baseType);
            if (!types.TryGetValue(name, out current))
                throw Invalid();
        }

        var inputs = new Dictionary<string, WorkflowInputDefinition>(StringComparer.Ordinal);
        foreach (var typeHandle in hierarchy)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var inputNames = property.GetCustomAttributes()
                    .Select(handle => (Handle: handle, Name: AttributeTypeName(reader, reader.GetCustomAttribute(handle))))
                    .Where(item => item.Name == InputAttributeName)
                    .Select(item => ReadInputName(reader, reader.GetCustomAttribute(item.Handle)))
                    .ToArray();
                if (inputNames.Length == 0)
                    continue;
                if (inputNames.Length != 1)
                    throw Invalid();
                var requiredCount = property.GetCustomAttributes().Count(handle =>
                    AttributeTypeName(reader, reader.GetCustomAttribute(handle)) == RequiredArgumentAttributeName);
                if (requiredCount > 1)
                    throw Invalid();

                var propertyType = property.DecodeSignature(TypeNameProvider.Instance, genericContext: (object?)null).ReturnType;
                if (!propertyType.StartsWith(InArgumentPrefix, StringComparison.Ordinal) || !propertyType.EndsWith('>'))
                    throw Invalid();
                var valueType = propertyType[InArgumentPrefix.Length..^1];
                var name = inputNames[0];
                if (string.IsNullOrWhiteSpace(name) || name.Length > 128 ||
                    !inputs.TryAdd(name, new WorkflowInputDefinition(name, valueType, requiredCount == 1)))
                    throw Invalid();
            }
        }

        return inputs;
    }

    /// <summary>解碼 constructor 所屬型別，不載入自訂屬性或呼叫其 constructor。</summary>
    private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        var parent = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => DeclaringType(reader, (MethodDefinitionHandle)attribute.Constructor),
            _ => default
        };

        return parent.Kind switch
        {
            HandleKind.TypeDefinition => FullName(reader, (TypeDefinitionHandle)parent),
            HandleKind.TypeReference => FullName(reader, (TypeReferenceHandle)parent),
            _ => string.Empty
        };
    }

    /// <summary>找到同組件 MethodDefinition 的 declaring Type，否則回傳 nil 供上層拒絕。</summary>
    private static TypeDefinitionHandle DeclaringType(MetadataReader reader, MethodDefinitionHandle method)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
            if (reader.GetTypeDefinition(typeHandle).GetMethods().Contains(method))
                return typeHandle;
        return default;
    }

    /// <summary>只接受一個固定字串 constructor argument 且沒有 named arguments 的 InputAttribute。</summary>
    private static string ReadInputName(MetadataReader reader, CustomAttribute attribute)
    {
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() != 1)
            throw Invalid();
        var name = blob.ReadSerializedString();
        if (blob.ReadUInt16() != 0 || blob.RemainingBytes != 0 || string.IsNullOrWhiteSpace(name))
            throw Invalid();
        return name;
    }

    /// <summary>組合頂層與巢狀 TypeDefinition 的完整名稱。</summary>
    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var parent = type.GetDeclaringType();
        if (!parent.IsNil)
            return FullName(reader, parent) + "+" + name;
        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>組合 TypeReference 名稱，以 ordinal 完整名稱比對 metadata 參照型別。</summary>
    private static string FullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>以 CLR 強式名稱 API 從公鑰計算 token；不保存公鑰位元組。</summary>
    private static string PublicKeyToken(MetadataReader reader, AssemblyDefinition assembly)
    {
        if (assembly.PublicKey.IsNil)
            return string.Empty;
        var key = reader.GetBlobBytes(assembly.PublicKey);
        if (key.Length == 0)
            return string.Empty;
        var identity = new AssemblyName();
        identity.SetPublicKey(key);
        return Convert.ToHexString(identity.GetPublicKeyToken() ?? Array.Empty<byte>()).ToLowerInvariant();
    }

    /// <summary>固定錯誤不包含路徑、組件位元組或自訂 attribute 本文。</summary>
    private static InvalidDataException Invalid() => new("Workflow activity input metadata is invalid.");

    /// <summary>將 PE signature 型別轉為穩定 CLR 名稱，不解析或載入任何執行期型別。</summary>
    private sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        internal static TypeNameProvider Instance { get; } = new();

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "function-pointer";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Void => "System.Void",
            _ => throw Invalid()
        };
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => FullName(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
