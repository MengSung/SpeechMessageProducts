/*
 * 檔案整體說明
 *
 * 目的與定位：以唯讀 PE 中繼資料檢查 Plugin 組件，不載入、反射執行或建立待部署程式碼。 每次只讀一個有大小上限的 DLL，並於方法結束釋放 PE reader 與檔案控制代碼。
 *
 * 主要功能與流程：
 * 本檔宣告 AssemblyCatalog。
 * - 驗證 DLL 的組件身份、雜湊與全部公開 Plugin 型別，並回傳供一次 plan/apply 使用的描述。 不把原始檔案路徑或 DLL 位元組寫入診斷訊息。
 * - 只回傳可註冊的公開、非 abstract 類別；介面與 abstract base class 不會被當作步驟候選。
 * - 直接辨識實作清單中的 IPlugin 介面，不使用 Assembly.Load 或執行 DLL 程式碼。
 * - 沿同一組件的基底類別追蹤 IPlugin，涵蓋繼承 PluginShellBase 的實際外掛。
 * 主要操作入口包含 Read、ReadPublicPluginTypes、ImplementsPluginInterface、GetTypeName、GetFullTypeName、IsPublicType、
 * ReadPublicKeyToken。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 以唯讀 PE 中繼資料檢查 Plugin 組件，不載入、反射執行或建立待部署程式碼。
/// 每次只讀一個有大小上限的 DLL，並於方法結束釋放 PE reader 與檔案控制代碼。
/// </summary>
internal static class AssemblyCatalog
{
    private const long MaximumAssemblyBytes = 16L * 1024 * 1024;
    private const string PluginInterfaceName = "Microsoft.Xrm.Sdk.IPlugin";

    /// <summary>
    /// 驗證 DLL 的組件身份、雜湊與全部公開 Plugin 型別，並回傳供一次 plan/apply 使用的描述。
    /// 不把原始檔案路徑或 DLL 位元組寫入診斷訊息。
    /// </summary>
    /// <param name="path">本機建置輸出或測試生成的 PE 檔案。</param>
    /// <param name="manifest">只含結構資料與秘密佔位的註冊清單。</param>
    /// <returns>包含組件身份、內容雜湊與 Plugin 型別的 detached 描述。</returns>
    /// <exception cref="ArgumentException">路徑空白或清單為 null。</exception>
    /// <exception cref="InvalidDataException">檔案不是有效且相符的 Plugin PE，或缺少/多出 Plugin 型別。</exception>
    public static AssemblyImage Read(string path, RegistrationManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.AssemblyName))
        {
            throw new InvalidDataException("註冊清單缺少組件名稱。");
        }

        byte[] content;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length <= 0 || stream.Length >= MaximumAssemblyBytes)
            {
                throw new InvalidDataException("Plugin 組件為空或超過允許大小。");
            }

            content = new byte[checked((int)stream.Length)];
            stream.ReadExactly(content);
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("Plugin 組件在讀取期間超過允許大小。");
            }
        }

        using var metadataStream = new MemoryStream(content, writable: false);
        using var peReader = new PEReader(metadataStream);
        if (!peReader.HasMetadata)
        {
            throw new InvalidDataException("Plugin 組件沒有可讀取的中繼資料。");
        }

        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
        {
            throw new InvalidDataException("指定 PE 不是 .NET 組件。");
        }

        var assembly = reader.GetAssemblyDefinition();
        var name = reader.GetString(assembly.Name);
        if (!string.Equals(name, manifest.AssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plugin 組件名稱與註冊清單不符。");
        }

        var pluginTypes = ReadPublicPluginTypes(reader);
        var declaredTypes = manifest.Steps
            .Select(step => step.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToArray();
        var actualTypes = pluginTypes.OrderBy(typeName => typeName, StringComparer.Ordinal).ToArray();
        if (declaredTypes.Length != manifest.Steps.Count ||
            !declaredTypes.SequenceEqual(actualTypes, StringComparer.Ordinal))
        {
            throw new InvalidDataException("註冊清單與 DLL 的 Plugin 型別集合不一致。");
        }

        var publicKeyToken = ReadPublicKeyToken(reader, assembly);
        var sha256 = Convert.ToHexString(SHA256.HashData(content));

        return new AssemblyImage(
            name,
            assembly.Version.ToString(),
            publicKeyToken,
            Convert.ToBase64String(content),
            sha256,
            Array.AsReadOnly(actualTypes));
    }

    /// <summary>
    /// 只回傳可註冊的公開、非 abstract 類別；介面與 abstract base class 不會被當作步驟候選。
    /// </summary>
    /// <param name="reader">由已驗證 PE 建立的唯讀中繼資料 reader。</param>
    /// <returns>排序前的完整型別名稱集合，呼叫端負責以 ordinal 順序固定化。</returns>
    private static List<string> ReadPublicPluginTypes(MetadataReader reader)
    {
        var result = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (!IsPublicType(reader, handle) ||
                (definition.Attributes & TypeAttributes.Interface) != 0 ||
                (definition.Attributes & TypeAttributes.Abstract) != 0 ||
                !ImplementsPluginInterface(reader, definition))
            {
                continue;
            }

            result.Add(GetFullTypeName(reader, handle));
        }

        return result;
    }

    /// <summary>直接辨識實作清單中的 IPlugin 介面，不使用 Assembly.Load 或執行 DLL 程式碼。</summary>
    /// <param name="reader">型別中繼資料來源。</param>
    /// <param name="definition">待檢查類別定義。</param>
    /// <returns>類別明確實作 Microsoft.Xrm.Sdk.IPlugin 時為 true。</returns>
    private static bool ImplementsPluginInterface(MetadataReader reader, TypeDefinition definition)
    {
        foreach (var implementationHandle in definition.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (GetTypeName(reader, implementation.Interface) == PluginInterfaceName)
            {
                return true;
            }
        }

        return ImplementsPluginInterface(reader, definition.BaseType, new HashSet<TypeDefinitionHandle>());
    }

    /// <summary>沿同一組件的基底類別追蹤 IPlugin，涵蓋繼承 PluginShellBase 的實際外掛。</summary>
    /// <param name="reader">唯讀 metadata reader。</param>
    /// <param name="baseType">目前類別的基底型別 handle。</param>
    /// <param name="visited">防止惡意或毀損 PE 形成循環。</param>
    /// <returns>基底鏈宣告或繼承 IPlugin 時為 true。</returns>
    private static bool ImplementsPluginInterface(
        MetadataReader reader,
        EntityHandle baseType,
        HashSet<TypeDefinitionHandle> visited)
    {
        if (baseType.Kind == HandleKind.TypeReference)
        {
            return GetTypeName(reader, baseType) == PluginInterfaceName;
        }

        if (baseType.Kind != HandleKind.TypeDefinition)
        {
            return false;
        }

        var handle = (TypeDefinitionHandle)baseType;
        if (!visited.Add(handle))
        {
            return false;
        }

        var definition = reader.GetTypeDefinition(handle);
        foreach (var implementationHandle in definition.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (GetTypeName(reader, implementation.Interface) == PluginInterfaceName)
            {
                return true;
            }
        }

        return ImplementsPluginInterface(reader, definition.BaseType, visited);
    }

    /// <summary>取得 TypeDefinition 或 TypeReference 的完整名稱；不解析外部組件或載入型別。</summary>
    /// <param name="reader">型別中繼資料來源。</param>
    /// <param name="handle">介面型別的中繼資料 handle。</param>
    /// <returns>命名空間與型別名稱。</returns>
    private static string GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetFullTypeName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetFullTypeName(reader, (TypeReferenceHandle)handle),
            _ => string.Empty
        };
    }

    /// <summary>組合 TypeDefinition 的巢狀名稱，不依賴執行期反射。</summary>
    /// <param name="reader">型別中繼資料來源。</param>
    /// <param name="handle">型別定義。</param>
    /// <returns>與 CLR 完整型別名稱相符的名稱。</returns>
    private static string GetFullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            return GetFullTypeName(reader, declaringType) + "+" + name;
        }

        var namespaceName = reader.GetString(definition.Namespace);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }

    /// <summary>組合 TypeReference 名稱以精確比對 SDK 的 IPlugin 身份。</summary>
    /// <param name="reader">型別中繼資料來源。</param>
    /// <param name="handle">外部型別參考。</param>
    /// <returns>命名空間與型別名稱。</returns>
    private static string GetFullTypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        var namespaceName = reader.GetString(reference.Namespace);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }

    /// <summary>支援公開頂層類別與其公開巢狀類別，且所有外層型別也必須公開。</summary>
    /// <param name="reader">型別中繼資料來源。</param>
    /// <param name="handle">待檢查型別。</param>
    /// <returns>型別可由 Plugin Registration API 以完整名稱解析時為 true。</returns>
    private static bool IsPublicType(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var visibility = definition.Attributes & TypeAttributes.VisibilityMask;
        var declaringType = definition.GetDeclaringType();
        return declaringType.IsNil
            ? visibility == TypeAttributes.Public
            : visibility == TypeAttributes.NestedPublic && IsPublicType(reader, declaringType);
    }

    /// <summary>透過 CLR 強式名稱 API 取得相容 PublicKeyToken；內容完整性另以 SHA-256 驗證。</summary>
    /// <param name="reader">組件中繼資料來源。</param>
    /// <param name="assembly">組件定義。</param>
    /// <returns>小寫十六進位 token；unsigned 測試樣本回傳空字串。</returns>
    private static string ReadPublicKeyToken(MetadataReader reader, AssemblyDefinition assembly)
    {
        if (assembly.PublicKey.IsNil)
        {
            return string.Empty;
        }

        var publicKey = reader.GetBlobBytes(assembly.PublicKey);
        if (publicKey.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var identity = new AssemblyName();
            identity.SetPublicKey(publicKey);
            return Convert.ToHexString(identity.GetPublicKeyToken() ?? Array.Empty<byte>()).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            throw new InvalidDataException("Plugin 組件的強式名稱公鑰格式無效。");
        }
    }
}
