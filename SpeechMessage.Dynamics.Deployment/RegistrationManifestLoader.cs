/*
 * 檔案整體說明
 *
 * 目的與定位：讀取受版控的第二版 Plugin／活動多組件清單；舊格式、未知／重複欄位、重複身分及不合法設定均在組織讀寫前拒絕。
 *
 * 主要功能與流程：
 * 本檔宣告 RegistrationManifestLoader。
 * - 從 repo 的固定清單位置讀取並驗證可解析的 JSON 結構。
 * 主要操作入口包含 Load。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 讀取最多 1 MiB 的第二版清單，檔案與 JSON document 只活在一次呼叫。
/// 不解析秘密；所有失敗回固定摘要，不攜帶 JSON 原文或內層例外。
/// </summary>
internal static class RegistrationManifestLoader
{
    private const long MaximumManifestBytes = 1024 * 1024;
    /// <summary>固定 schema 名稱與深度；序列化測試清單亦沿用此規約，未知欄位不得靜默丟棄。</summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        WriteIndented = true
    };

    /// <summary>
    /// 既有單組件命令的入口；多組件文件需走 LoadDocument，不能攤平或忽略後續組件。
    /// </summary>
    public static RegistrationManifest Load(string repositoryRoot)
    {
        var document = LoadDocument(repositoryRoot);
        if (document.Assemblies.Count != 1) throw InvalidManifest();
        return document.Assemblies[0];
    }

    /// <summary>
    /// 從 repo 固定位置完整載入與驗證多組件清單；取得 handle 後再驗長度，
    /// 避免初次 FileInfo 檢查與開檔之間的成長繞過容量上限。
    /// </summary>
    /// <param name="repositoryRoot">CLI 核定的本工作樹根目錄。</param>
    /// <returns>本次操作的純結構候選；不持有 stream、document 或 SDK。</returns>
    internal static RegistrationDocument LoadDocument(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var path = Path.Combine(repositoryRoot, "d365", "registration", "plugins.registration.json");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumManifestBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0) throw InvalidManifest();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaximumManifestBytes) throw InvalidManifest();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            Schema.SchemaFiles.RejectDuplicateProperties(document.RootElement);
            var registration = document.RootElement.Deserialize<RegistrationDocument>(Json) ?? throw InvalidManifest();
            if (registration.SchemaVersion != 2 || registration.Assemblies is null ||
                registration.Assemblies.Count is < 1 or > 2) throw InvalidManifest();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in registration.Assemblies)
            {
                if (assembly is null || !names.Add(assembly.AssemblyName)) throw InvalidManifest();
                Validate(assembly);
            }
            return registration;
        }
        catch (JsonException)
        {
            throw InvalidManifest();
        }
    }

    /// <summary>核心亦呼叫此驗證，防止程式內自行建立的候選繞過檔案格式關卡。</summary>
    internal static void Validate(RegistrationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 2 || manifest.AssemblyName is not
            ("SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows") ||
            manifest.Steps is null || manifest.Activities is null ||
            manifest.Steps.Count + manifest.Activities.Count is < 1 or > 256) throw InvalidManifest();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var legacyIds = new HashSet<Guid>();
        foreach (var step in manifest.Steps)
        {
            if (step is null || !ValidModule(step.Module) || string.IsNullOrWhiteSpace(step.Type) ||
                !ValidName(step.Entity) || step.Message is not ("Create" or "Update" or "Delete") ||
                step.Stage is not ("PreValidation" or "PreOperation" or "PostOperation") ||
                step.Mode is not ("Synchronous" or "Asynchronous") || step.Rank <= 0 ||
                (step.Mode == "Asynchronous" && step.Stage != "PostOperation") ||
                step.FilteringAttributes is null || step.Images is null || step.Legacy is null ||
                step.EnabledByOrganization is null || step.ShadowReport is null ||
                step.SecureConfig is null || step.UnsecureConfig is null ||
                (step.Message == "Update" && step.FilteringAttributes.Count == 0) ||
                (step.Message != "Update" && step.FilteringAttributes.Count != 0) ||
                !ValidNames(step.FilteringAttributes) ||
                !keys.Add(StepKey(step))) throw InvalidManifest();
            var imageNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var image in step.Images)
            {
                if (image is null || !ValidName(image.Name) || !imageNames.Add(image.Name) ||
                    image.Type is not ("PreImage" or "PostImage") || image.Attributes is null ||
                    image.Attributes.Count == 0 || !ValidNames(image.Attributes) ||
                    (step.Message == "Create" && image.Type == "PreImage") ||
                    (step.Message == "Delete" && image.Type == "PostImage") ||
                    (image.Type == "PostImage" && step.Stage != "PostOperation")) throw InvalidManifest();
            }
            if (step.EnabledByOrganization.Keys.Any(organization => organization != "goodshepherdback")) throw InvalidManifest();
            ValidateReport(step.ShadowReport);
            foreach (var legacy in step.Legacy)
                if (legacy is null || string.IsNullOrWhiteSpace(legacy.Assembly) || string.IsNullOrWhiteSpace(legacy.Type) ||
                    !Guid.TryParseExact(legacy.StepId, "D", out var id) || id == Guid.Empty || !legacyIds.Add(id)) throw InvalidManifest();
            ValidateConfiguration(step);
        }
        var activityTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in manifest.Activities)
        {
            if (activity is null || !ValidModule(activity.Module) || string.IsNullOrWhiteSpace(activity.Type) ||
                string.IsNullOrWhiteSpace(activity.DisplayName) || string.IsNullOrWhiteSpace(manifest.ActivityGroup) ||
                !activityTypes.Add(activity.Type) || activity.Legacy is null || activity.ShadowReport is null) throw InvalidManifest();
            ValidateReport(activity.ShadowReport);
            foreach (var legacy in activity.Legacy)
                if (legacy is null || string.IsNullOrWhiteSpace(legacy.Assembly) || string.IsNullOrWhiteSpace(legacy.Type)) throw InvalidManifest();
        }
    }

    /// <summary>diagnostics 保留舊設定語法，其餘外殼只接受 Shadow／Live 及 scalar options。</summary>
    private static void ValidateConfiguration(StepRegistration step)
    {
        if (step.UnsecureConfig.Length is < 1 or > 4096 || step.SecureConfig.Length > 4096) throw InvalidManifest();
        try
        {
            using var document = JsonDocument.Parse(step.UnsecureConfig, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw InvalidManifest();
            Schema.SchemaFiles.RejectDuplicateProperties(root);
            if (step.Module == "diagnostics")
            {
                DeploymentEngine.ValidateConfigurationTemplate(step);
                return;
            }
            if (!root.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String ||
                mode.GetString() is not ("Shadow" or "Live") ||
                root.EnumerateObject().Any(property => property.Name is not ("mode" or "options"))) throw InvalidManifest();
            if (mode.GetString() == "Shadow" && step.SecureConfig.Length != 0) throw InvalidManifest();
            if (mode.GetString() == "Live" && step.SecureConfig is not ("" or "${secret:TokenKey}")) throw InvalidManifest();
            if (root.TryGetProperty("options", out var options) &&
                (options.ValueKind != JsonValueKind.Object || options.EnumerateObject().Any(property =>
                    property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)))) throw InvalidManifest();
        }
        catch (JsonException)
        {
            throw InvalidManifest();
        }
    }

    /// <summary>穩定鍵涵蓋所有註冊語意；同型別不同順位仍為不同步驟。</summary>
    internal static string StepKey(StepRegistration step) => string.Join("|", step.Type, step.Message, step.Entity,
        step.Stage, step.Mode, step.Rank.ToString(CultureInfo.InvariantCulture));

    /// <summary>欄位或映像名使用有長度上限的 ASCII schema 識別字，不允許任意字串。</summary>
    private static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    /// <summary>欄位清單必須有界、合法且逐筆唯一，不以 Distinct 隱藏衝突。</summary>
    private static bool ValidNames(IReadOnlyList<string> values) => values.Count <= 128 &&
        values.All(ValidName) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    /// <summary>模組代號使用 05A 核定集合，不允許任意輸入取得其他步驟控制範圍。</summary>
    private static bool ValidModule(string value) => value is "diagnostics" or "localization" or "weeklyreports" or
        "attendance" or "lessons" or "appointments" or "qrcodes" or "care" or "fees" or "messaging";

    /// <summary>報告基準保持未驗收與實機、離線、閱讀對照的區別，不自行推導證據。</summary>
    private static void ValidateReport(ShadowReport report)
    {
        if (report.Differences < 0 || report.Basis is not ("" or "legacy-live" or "legacy-golden" or
            "legacy-live-only" or "reading" or "deferred-to-P7" or "new")) throw InvalidManifest();
    }

    /// <summary>固定摘要不攜帶 JSON、secret、檔案內容或內層解析例外。</summary>
    private static InvalidDataException InvalidManifest() => new("註冊清單版本、身分或設定無效。");
}
