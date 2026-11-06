/*
 * 檔案整體說明
 *
 * 目的與定位：由 PowerShell 啟動的離線中繼資料檢查器；只讀 PE，不載入或執行受檢 DLL。 每次程序只處理指定的檔案，串流與讀取器由同一操作確定性釋放，不保留到 static。
 *
 * 主要功能與流程：
 * 本檔宣告 Program。
 * - 部署沙箱目前核定的最高組件目標框架；更高版本即使本機可執行也不得送入合併。
 * - 以固定 Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3 的 Facades 清單加 netstandard 區分相容轉送組件，避免原有 System.* 
 * 允許規則讓 facade 參考假通過。 集合在程序啟動後不可變且大小固定，不讀取執行主機／呼叫者的目錄或設定，不保存任何操作狀態。 組件身分依 CLR 簡名以 OrdinalIgnoreCase 比對；只拒絕完整名稱，
 * 不誤拒合法的 System.Net.Http 等框架參考。
 * - 輸出固定 JSON 檢查結果；未完成檢查時不得以結束碼零假稱通過。
 * - 由呼叫者擁有輸出 writer，讓並行離線驗證不必改全域 Console 或共享診斷緩衝。
 * 主要操作入口包含 Main、Run、ReadFramework、ReadFrameworkName、ValidateMergeInputFramework、HasNoFrameworkFacade、
 * IsFrameworkAtOrBelowLimit、Reject。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck/Program.cs、tools/PluginPackaging.Tests/AssemblyInspectionTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace SpeechMessage.Tools.PluginPackaging;

/// <summary>
/// 由 PowerShell 啟動的離線中繼資料檢查器；只讀 PE，不載入或執行受檢 DLL。
/// 每次程序只處理指定的檔案，串流與讀取器由同一操作確定性釋放，不保留到 static。
/// </summary>
internal static class Program
{
    /// <summary>部署沙箱目前核定的最高組件目標框架；更高版本即使本機可執行也不得送入合併。</summary>
    private static readonly Version MaximumMergeInputFramework = new(4, 6, 2);

    /// <summary>
    /// 以固定 Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3 的 Facades 清單加 netstandard
    /// 區分相容轉送組件，避免原有 System.* 允許規則讓 facade 參考假通過。
    /// 集合在程序啟動後不可變且大小固定，不讀取執行主機／呼叫者的目錄或設定，不保存任何操作狀態。
    /// 組件身分依 CLR 簡名以 OrdinalIgnoreCase 比對；只拒絕完整名稱，不誤拒合法的 System.Net.Http 等框架參考。
    /// </summary>
    private static readonly FrozenSet<string> FrameworkFacadeNames = new[]
    {
        "System.Collections",
        "System.Collections.Concurrent",
        "System.ComponentModel",
        "System.ComponentModel.Annotations",
        "System.ComponentModel.EventBasedAsync",
        "System.Diagnostics.Contracts",
        "System.Diagnostics.Debug",
        "System.Diagnostics.Tools",
        "System.Diagnostics.Tracing",
        "System.Dynamic.Runtime",
        "System.Globalization",
        "System.IO",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Linq.Parallel",
        "System.Linq.Queryable",
        "System.Net.NetworkInformation",
        "System.Net.Primitives",
        "System.Net.Requests",
        "System.Net.WebHeaderCollection",
        "System.ObjectModel",
        "System.Reflection",
        "System.Reflection.Emit",
        "System.Reflection.Emit.ILGeneration",
        "System.Reflection.Emit.Lightweight",
        "System.Reflection.Extensions",
        "System.Reflection.Primitives",
        "System.Resources.ResourceManager",
        "System.Runtime",
        "System.Runtime.Extensions",
        "System.Runtime.Handles",
        "System.Runtime.InteropServices",
        "System.Runtime.InteropServices.WindowsRuntime",
        "System.Runtime.Numerics",
        "System.Runtime.Serialization.Json",
        "System.Runtime.Serialization.Primitives",
        "System.Runtime.Serialization.Xml",
        "System.Security.Principal",
        "System.ServiceModel.Duplex",
        "System.ServiceModel.Http",
        "System.ServiceModel.NetTcp",
        "System.ServiceModel.Primitives",
        "System.ServiceModel.Security",
        "System.Text.Encoding",
        "System.Text.Encoding.Extensions",
        "System.Text.RegularExpressions",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Threading.Tasks.Parallel",
        "System.Threading.Timer",
        "System.Xml.ReaderWriter",
        "System.Xml.XDocument",
        "System.Xml.XmlSerializer",
        "netstandard",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>輸出固定 JSON 檢查結果；未完成檢查時不得以結束碼零假稱通過。</summary>
    /// <param name="args">受檢 DLL 的本機路徑，不含憑證或連線資訊。</param>
    /// <returns>零表示所有契約通過，一表示驗證拒絕。</returns>
    public static int Main(string[] args)
        => Run(args, Console.Out);

    /// <summary>由呼叫者擁有輸出 writer，讓並行離線驗證不必改全域 Console 或共享診斷緩衝。</summary>
    /// <param name="args">單一本機 DLL 路徑。</param>
    /// <param name="output">僅存活於此次操作的輸出目的地，本工具不負責關閉。</param>
    /// <returns>所有中繼資料契約通過才回傳零。</returns>
    internal static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 2 && string.Equals(args[0], "--validate-net462-input", StringComparison.Ordinal))
            return ValidateMergeInputFramework(args[1], output);
        if (args.Length != 1) return Reject("InvalidArguments", output);
        try
        {
            // 每個受檢檔案最多十六 MiB；先以檔案長度拒絕超界，再建立唯讀 PE reader。
            using var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return Reject("InvalidImage", output);
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
            var assembly = metadata.GetAssemblyDefinition();
            var publicKey = metadata.GetBlobBytes(assembly.PublicKey);
            var identity = new AssemblyName("InspectionOnly");
            identity.SetPublicKey(publicKey);
            var strongName = publicKey.Length > 0 && identity.GetPublicKeyToken() is { Length: > 0 } &&
                (pe.PEHeaders.CorHeader!.Flags & CorFlags.StrongNameSigned) != 0;
            var targetFramework = ReadFramework(metadata, assembly);
            var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
            var allowed = references.All(name => name is "mscorlib" or "System" or "Microsoft.Xrm.Sdk" or "Microsoft.Crm.Sdk.Proxy" ||
                name.StartsWith("System.", StringComparison.Ordinal));
            var noDrawing = references.All(name => !string.Equals(name, "System.Drawing", StringComparison.Ordinal) &&
                !name.StartsWith("System.Drawing.", StringComparison.Ordinal));
            var plugin = metadata.TypeDefinitions.Any(handle =>
            {
                var type = metadata.GetTypeDefinition(handle);
                return (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public &&
                    string.Equals(metadata.GetString(type.Namespace), "SpeechMessage.Dynamics.Plugins.Diagnostics", StringComparison.Ordinal) &&
                    string.Equals(metadata.GetString(type.Name), "DiagnosticsPingPlugin", StringComparison.Ordinal);
            });
            var noNetStandardFacade = HasNoFrameworkFacade(metadata, assembly);
            var ok = strongName && targetFramework && allowed && noDrawing && plugin && noNetStandardFacade;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                ok,
                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noNetStandardFacade, noDrawing, diagnosticsPlugin = plugin, size = true },
                assemblyVersion = assembly.Version.ToString(),
                references,
                sizeBytes = stream.Length
            }));
            return ok ? 0 : 1;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 損壞影像、不可讀檔與不合法中繼資料都是預期輸入拒絕；不輸出原始路徑或例外訊息。
            return Reject("InvalidImage", output);
        }
    }

    /// <summary>只解析 assembly 的框架屬性 blob，不反射載入待測型別或執行屬性建構式。</summary>
    /// <param name="metadata">當次 PE reader 擁有的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>只有明確宣告 .NET Framework 4.6.2 的正式部署組件才通過。</returns>
    private static bool ReadFramework(MetadataReader metadata, AssemblyDefinition assembly)
        => string.Equals(ReadFrameworkName(metadata, assembly), ".NETFramework,Version=v4.6.2", StringComparison.Ordinal);

    /// <summary>只解析 assembly 的框架屬性字串，不載入待測型別或執行使用者程式碼。</summary>
    /// <param name="metadata">當次 PE reader 擁有的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>明確的框架名稱；缺少屬性或格式錯誤時回傳 null。</returns>
    private static string? ReadFrameworkName(MetadataReader metadata, AssemblyDefinition assembly)
    {
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (!string.Equals(metadata.GetString(type.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal) ||
                !string.Equals(metadata.GetString(type.Name), "TargetFrameworkAttribute", StringComparison.Ordinal)) continue;
            var blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) return null;
            return blob.ReadSerializedString();
        }
        return null;
    }

    /// <summary>
    /// 在 ILRepack 前逐一驗證輸入組件宣告了不高於 v4.6.2 的 .NET Framework 目標，且沒有 facade 身分或參考。
    /// 僅輸出固定 JSON，不回顯路徑；讀取仍由單一 PE reader 操作擁有並在返回時釋放。
    /// </summary>
    /// <param name="path">本次合併的單一輸入組件路徑。</param>
    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
    /// <returns>只有 PE 可讀、框架版本符合上限且未含 facade 時回傳零。</returns>
    private static int ValidateMergeInputFramework(string path, TextWriter output)
    {
        string? frameworkName;
        bool noNetStandardFacade;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return Reject("InvalidImage", output);
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
            var assembly = metadata.GetAssemblyDefinition();
            frameworkName = ReadFrameworkName(metadata, assembly);
            noNetStandardFacade = HasNoFrameworkFacade(metadata, assembly);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 預期的本機 PE 輸入錯誤以固定原因拒絕，不洩漏原路徑或例外文字。
            return Reject("InvalidImage", output);
        }

        var valid = IsFrameworkAtOrBelowLimit(frameworkName);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = valid && noNetStandardFacade,
            checks = new { targetFramework = valid, noNetStandardFacade },
            frameworkName,
            maximumTargetFramework = ".NETFramework,Version=v4.6.2"
        }));
        return valid && noNetStandardFacade ? 0 : 1;
    }

    /// <summary>
    /// 共用於部署 DLL 與合併前輸入檢查：組件自身及其全部參考都不得是固定清單中的 facade。
    /// 只讀同一操作擁有的 MetadataReader，不載入組件、不追蹤磁碟相依，也不持有 reader 或參考名稱快照。
    /// </summary>
    /// <param name="metadata">由呼叫端 PE reader 控制生命週期的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>自身與所有 assembly references 都不是 facade 時才為 true。</returns>
    private static bool HasNoFrameworkFacade(MetadataReader metadata, AssemblyDefinition assembly)
        => !FrameworkFacadeNames.Contains(metadata.GetString(assembly.Name)) &&
            metadata.AssemblyReferences.All(handle =>
                !FrameworkFacadeNames.Contains(metadata.GetString(metadata.GetAssemblyReference(handle).Name)));

    /// <summary>只接受明確 .NET Framework 版本，並以數值版本比較拒絕高於 v4.6.2 的輸入。</summary>
    /// <param name="frameworkName">PE 中繼資料的 TargetFrameworkAttribute 值。</param>
    /// <returns>可解析且不高於核定上限時為 true。</returns>
    private static bool IsFrameworkAtOrBelowLimit(string? frameworkName)
    {
        const string prefix = ".NETFramework,Version=v";
        if (frameworkName is null || !frameworkName.StartsWith(prefix, StringComparison.Ordinal) ||
            !Version.TryParse(frameworkName.AsSpan(prefix.Length), out var version))
            return false;
        return version.Major >= 0 && version <= MaximumMergeInputFramework;
    }

    /// <summary>驗證尚未完整成功時一律失敗；固定代碼與布林欄位不包含檔案原文或簽章金鑰資訊。</summary>
    /// <param name="reason">本工具擁有的固定原因代碼。</param>
    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
    /// <returns>供 PowerShell／CI 判定的拒絕結束碼。</returns>
    private static int Reject(string reason, TextWriter output)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = false,
            reason,
            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noNetStandardFacade = false, noDrawing = false, diagnosticsPlugin = false, size = false }
        }));
        return 1;
    }
}
