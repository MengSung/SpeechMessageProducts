/*
 * 檔案整體說明
 *
 * 目的與定位：讀取受版控的第二版 Plugin／活動多組件清單；舊格式、未知／重複欄位、重複身分及不合法設定均在組織讀寫前拒絕。
 *
 * 主要功能與流程：
 * 本檔宣告 RegistrationManifestLoader。
 * - 從 repo 的固定清單位置讀取並驗證可解析的 JSON 結構。
 * - 非diagnostics的live分級與完整pass摘要必須有效；活動不能攜帶tokenKey，未驗收不當成功。
 * - 精確平台組件只接受 F3 核定的兩個 IPlugin 型別、三個同步 Create／Update 步驟、
 *   Update 的 new_product filter 與 ProductBefore PreImage，以及三組織產品映射；舊組件語法維持原樣。
 * - 整份來源先驗證，再按已核定組織選取適用組件；好管家／好學會只取平台，
 *   好牧人取三組件，既有組織只取舊兩組件。模組切換另排除平台，不把產品池當 Shadow 模組。
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
    private const string PlatformAssemblyName = "SpeechMessage.Dynamics.Platform.Plugins";
    private const string PlatformSlotRequestType =
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin";
    private const string PlatformChannelCreatedType =
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin";
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
            ValidateDocument(registration);
            return registration;
        }
        catch (JsonException)
        {
            throw InvalidManifest();
        }
    }

    /// <summary>
    /// 完整來源全部驗證後才選取本組織的組件；不藉過濾掩蓋未知、重複或壞設定。
    /// 組織授權仍由 catalog／ProductionGuard 負責，本方法不連線或載入 DLL；
    /// 回傳集合是本次命令的新容器，候選只存活至同步命令結束。
    /// </summary>
    /// <param name="repositoryRoot">本命令核定的方案根。</param>
    /// <param name="organization">catalog 已核定的 canonical 名稱。</param>
    /// <param name="legacyOnly">舊 Shadow／切換只允許既有教會兩組件，不操作平台池。</param>
    internal static RegistrationDocument LoadForOrganization(string repositoryRoot, string organization, bool legacyOnly = false)
    {
        var document = LoadDocument(repositoryRoot);
        var selected = document.Assemblies.Where(assembly => IsAssemblyAllowed(assembly.AssemblyName, organization) &&
            (!legacyOnly || assembly.AssemblyName != PlatformAssemblyName)).ToArray();
        if (selected.Length == 0) throw InvalidManifest();
        return document with { Assemblies = selected };
    }

    /// <summary>
    /// 核心入口驗證完整候選及組件產品邊界，不會默默刪去呼叫端傳入的不適用組件。
    /// 既有正式組織的 catalog／人工確認規則由上層保留；此純值關卡不授予連線權。
    /// </summary>
    internal static void ValidateForOrganization(RegistrationDocument document, string organization)
    {
        ValidateDocument(document);
        if (document.Assemblies.Any(assembly => !IsAssemblyAllowed(assembly.AssemblyName, organization)))
            throw InvalidManifest();
    }

    /// <summary>
    /// 精確組件用途映射：平台僅三個新產品，教會組件不得進好管家與好學會。
    /// 其他既有組織只保留原教會組件相容性；未知組織仍須先由 catalog 拒絕。
    /// </summary>
    internal static bool IsAssemblyAllowed(string assemblyName, string organization) => assemblyName switch
    {
        PlatformAssemblyName => organization is "goodshepherd20" or "construction" or "association",
        "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" =>
            !string.IsNullOrWhiteSpace(organization) && organization is not ("construction" or "association"),
        _ => false
    };

    /// <summary>磁碟與程式內候選共用三組件上限及精確身分驗證，重複組件不得被投影隱藏。</summary>
    private static void ValidateDocument(RegistrationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 2 || document.Assemblies is null || document.Assemblies.Count is < 1 or > 3)
            throw InvalidManifest();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in document.Assemblies)
        {
            if (assembly is null || !names.Add(assembly.AssemblyName)) throw InvalidManifest();
            Validate(assembly);
        }
    }

    /// <summary>核心亦呼叫此驗證，防止程式內自行建立的候選繞過檔案格式關卡。</summary>
    internal static void Validate(RegistrationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 2 || manifest.AssemblyName is not
            ("SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" or PlatformAssemblyName) ||
            manifest.Steps is null || manifest.Activities is null ||
            manifest.Steps.Count + manifest.Activities.Count is < 1 or > 256) throw InvalidManifest();
        var isPlatformAssembly = string.Equals(manifest.AssemblyName, PlatformAssemblyName, StringComparison.Ordinal);
        if (isPlatformAssembly && (manifest.Steps.Count != 3 || manifest.Activities.Count != 0 ||
            !string.IsNullOrEmpty(manifest.ActivityGroup))) throw InvalidManifest();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var legacyIds = new HashSet<Guid>();
        foreach (var step in manifest.Steps)
        {
            if (step is null || !ValidModule(manifest.AssemblyName, step.Module) || string.IsNullOrWhiteSpace(step.Type) ||
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
            if (step.EnabledByOrganization.Keys.Any(organization => !IsKnownOrganization(organization))) throw InvalidManifest();
            ValidateReport(step.ShadowReport);
            if (step.Module == "diagnostics")
            {
                if (step.Live is not null) throw InvalidManifest();
            }
            else ValidateLive(step.Live);
            foreach (var legacy in step.Legacy)
                if (legacy is null || string.IsNullOrWhiteSpace(legacy.Assembly) || string.IsNullOrWhiteSpace(legacy.Type) ||
                    !Guid.TryParseExact(legacy.StepId, "D", out var id) || id == Guid.Empty || !legacyIds.Add(id)) throw InvalidManifest();
            ValidateConfiguration(manifest.AssemblyName, step);
        }
        if (isPlatformAssembly) ValidatePlatformSteps(manifest.Steps);
        var activityTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in manifest.Activities)
        {
            if (activity is null || !ValidModule(activity.Module) || string.IsNullOrWhiteSpace(activity.Type) ||
                string.IsNullOrWhiteSpace(activity.DisplayName) || string.IsNullOrWhiteSpace(manifest.ActivityGroup) ||
                !activityTypes.Add(activity.Type) || activity.Legacy is null || activity.ShadowReport is null) throw InvalidManifest();
            ValidateReport(activity.ShadowReport);
            ValidateLive(activity.Live);
            foreach (var legacy in activity.Legacy)
                if (legacy is null || string.IsNullOrWhiteSpace(legacy.Assembly) || string.IsNullOrWhiteSpace(legacy.Type)) throw InvalidManifest();
        }
    }

    /// <summary>diagnostics 保留舊設定語法，其餘外殼只接受 Shadow／Live 及 scalar options。</summary>
    private static void ValidateConfiguration(string assemblyName, StepRegistration step)
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
            if (string.Equals(assemblyName, PlatformAssemblyName, StringComparison.Ordinal))
            {
                ValidatePlatformConfiguration(root, step);
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

    /// <summary>活動只使用既有模組白名單；活動清單沒有組件專屬 platform 模組。</summary>
    private static bool ValidModule(string value) =>
        value is "diagnostics" or "localization" or "weeklyreports" or "attendance" or "lessons" or
            "appointments" or "qrcodes" or "care" or "fees" or "messaging";

    /// <summary>步驟沿用既有模組白名單；platform 僅在精確 F3 組件身分下開放。</summary>
    private static bool ValidModule(string assemblyName, string value) =>
        ValidModule(value) ||
        (string.Equals(assemblyName, PlatformAssemblyName, StringComparison.Ordinal) && value == "platform");

    /// <summary>
    /// 驗證平台組件恰有三個固定事件。型別允許重複出現在 Create 與 Update，
    /// 因此逐筆比對完整事件身份、filter、image 與所有組織旗標，不以型別集合或一般步驟預算代替。
    /// </summary>
    /// <param name="steps">已通過通用資料欄位、組織代碼及唯一步驟鍵檢查的本組件候選。</param>
    private static void ValidatePlatformSteps(IReadOnlyList<StepRegistration> steps)
    {
        foreach (var step in steps)
        {
            var validEvent = step.Type switch
            {
                PlatformSlotRequestType => step.Module == "platform" && step.Message == "Create" &&
                    step.Entity == "new_slotrequest" && step.Stage == "PreOperation" && step.Mode == "Synchronous" &&
                    step.Rank == 1 && step.FilteringAttributes.Count == 0 && step.Images.Count == 0,
                PlatformChannelCreatedType when step.Message == "Create" =>
                    step.Module == "platform" && step.Entity == "new_linechannel" &&
                    step.Stage == "PostOperation" && step.Mode == "Synchronous" && step.Rank == 1 &&
                    step.FilteringAttributes.Count == 0 && step.Images.Count == 0,
                PlatformChannelCreatedType when step.Message == "Update" =>
                    step.Module == "platform" && step.Entity == "new_linechannel" &&
                    step.Stage == "PreOperation" && step.Mode == "Synchronous" && step.Rank == 1 &&
                    step.FilteringAttributes.Count == 1 &&
                    string.Equals(step.FilteringAttributes[0], "new_product", StringComparison.Ordinal) &&
                    step.Images.Count == 1 && step.Images[0].Name == "ProductBefore" &&
                    step.Images[0].Type == "PreImage" && step.Images[0].Attributes.Count == 1 &&
                    string.Equals(step.Images[0].Attributes[0], "new_product", StringComparison.Ordinal),
                _ => false
            };
            if (!validEvent || step.Legacy.Count != 0 ||
                step.Live is not { Tier: "live", TokenKey: false, Reason: "" } ||
                step.Live.Evidence is not { File: "", Sha256: "", At: "", Result: "" } ||
                step.ShadowReport is not { File: "", Differences: null, Basis: "" } ||
                step.EnabledByOrganization.Count != 3 ||
                !step.EnabledByOrganization.TryGetValue("goodshepherd20", out var shepherdEnabled) || !shepherdEnabled ||
                !step.EnabledByOrganization.TryGetValue("construction", out var constructionEnabled) || !constructionEnabled ||
                !step.EnabledByOrganization.TryGetValue("association", out var associationEnabled) || !associationEnabled)
                throw InvalidManifest();
        }

        var eventKeys = steps.Select(step => string.Join("|", step.Type, step.Message, step.Entity,
            step.Stage, step.Mode, step.Rank.ToString(CultureInfo.InvariantCulture)))
            .ToHashSet(StringComparer.Ordinal);
        if (eventKeys.Count != 3 ||
            !eventKeys.Contains(string.Join("|", PlatformSlotRequestType, "Create", "new_slotrequest",
                "PreOperation", "Synchronous", "1")) ||
            !eventKeys.Contains(string.Join("|", PlatformChannelCreatedType, "Create", "new_linechannel",
                "PostOperation", "Synchronous", "1")) ||
            !eventKeys.Contains(string.Join("|", PlatformChannelCreatedType, "Update", "new_linechannel",
                "PreOperation", "Synchronous", "1")))
            throw InvalidManifest();
    }

    /// <summary>平台設定只接受完整三組織 products map；未知鍵、缺鍵、額外欄位與 secure config 均 fail closed。</summary>
    /// <param name="root">已解析且已拒絕遞迴重複屬性的 JSON 物件。</param>
    /// <param name="step">本次平台步驟純資料；不解析或保留任何秘密值。</param>
    private static void ValidatePlatformConfiguration(JsonElement root, StepRegistration step)
    {
        if (step.SecureConfig.Length != 0 || root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count() != 1 || !root.TryGetProperty("products", out var products) ||
            products.ValueKind != JsonValueKind.Object || products.EnumerateObject().Count() != 3 ||
            !HasProduct(products, "goodshepherd20", "GoodShepherd2") ||
            !HasProduct(products, "construction", "GoodButler") ||
            !HasProduct(products, "association", "GoodGuild")) throw InvalidManifest();
    }

    /// <summary>以精確 key 與字串值查驗單一產品，不接受大小寫變體、非字串或呼叫端新增名稱。</summary>
    private static bool HasProduct(JsonElement products, string organization, string product) =>
        products.TryGetProperty(organization, out var value) && value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), product, StringComparison.Ordinal);

    /// <summary>穩定鍵涵蓋所有註冊語意；同型別不同順位仍為不同步驟。</summary>
    internal static string StepKey(StepRegistration step) => string.Join("|", step.Type, step.Message, step.Entity,
        step.Stage, step.Mode, step.Rank.ToString(CultureInfo.InvariantCulture));

    /// <summary>欄位或映像名使用有長度上限的 ASCII schema 識別字，不允許任意字串。</summary>
    private static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    /// <summary>欄位清單必須有界、合法且逐筆唯一，不以 Distinct 隱藏衝突。</summary>
    private static bool ValidNames(IReadOnlyList<string> values) => values.Count <= 128 &&
        values.All(ValidName) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    /// <summary>註冊旗標只接受本階段四個核定測試組織，不以任意 caller key 擴張 Plugin 範圍。</summary>
    private static bool IsKnownOrganization(string value) =>
        value is "goodshepherdback" or "goodshepherd20" or "construction" or "association";

    /// <summary>報告基準保持未驗收與實機、離線、閱讀對照的區別，不自行推導證據。</summary>
    private static void ValidateReport(ShadowReport report)
    {
        if (report.Differences < 0 || report.Basis is not ("" or "legacy-golden" or
            "reading" or "deferred-to-P7" or "new")) throw InvalidManifest();
    }

    /// <summary>
    /// 分級及證據只在本次結構候選驗證；dormant必有理由，pass必有可定位的完整摘要。
    /// 不讀取證據檔、不解析金鑰，失敗在任何SDK連線或切換之前拒絕整份文件。
    /// </summary>
    /// <param name="live">步驟或活動的操作區域純資料；缺少一律拒絕。</param>
    private static void ValidateLive(LiveRegistration? live)
    {
        if (live is null || live.Tier is not ("live" or "dormant") || live.Reason is null ||
            (live.Tier == "dormant" && !HasProposalPrefix(live.Reason)) ||
            live.Evidence is not { } evidence || evidence.Result is not ("" or "pass") ||
            evidence.File is null || evidence.Sha256 is null || evidence.At is null) throw InvalidManifest();
        if (evidence.Result == "pass" &&
            (string.IsNullOrWhiteSpace(evidence.File) || evidence.Sha256.Length != 64 ||
             !evidence.Sha256.All(char.IsAsciiHexDigit) ||
             !DateTime.TryParseExact(evidence.At, "yyyy-MM-dd HH:mm 'Asia/Taipei (UTC+08:00)'",
                 CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) throw InvalidManifest();
    }

    /// <summary>線性檢查固定提案前綴，不配置正規運算式、保留理由或允許無編號的dormant決策。</summary>
    /// <param name="reason">本次候選理由；格式為P加階段數字、-PROP-及三位編號。</param>
    /// <returns>前綴完整且編號不是四位以上時為true，後續中文說明不受影響。</returns>
    private static bool HasProposalPrefix(string reason)
    {
        var separator = reason.IndexOf("-PROP-", StringComparison.Ordinal);
        if (separator < 2 || reason.Length < separator + 9 || reason[0] != 'P') return false;
        for (var index = 1; index < separator; index++)
            if (!char.IsAsciiDigit(reason[index])) return false;
        for (var index = separator + 6; index < separator + 9; index++)
            if (!char.IsAsciiDigit(reason[index])) return false;
        return reason.Length == separator + 9 || !char.IsAsciiDigit(reason[separator + 9]);
    }

    /// <summary>固定摘要不攜帶 JSON、secret、檔案內容或內層解析例外。</summary>
    private static InvalidDataException InvalidManifest() => new("註冊清單版本、身分或設定無效。");
}
