/*
 * 檔案整體說明
 *
 * 目的與定位：單一 Dataverse 服務租約上的佈署操作核心。物件不跨命令共用；它不保存 user／tenant 狀態、 已解析的秘密或外部 client。計畫與匯出只包含非敏感資料，所有檔案 writer
 * 都由呼叫期間擁有。
 *
 * 主要功能與流程：
 * 本檔宣告 DeploymentEngine、Inspection、DeploymentExport、PluginAssemblyExport、PluginTypeExport、PluginStepExport。
 * - 步驟安全設定查閱的官方邏輯名稱；不可縮寫成 secureconfigid，否則已註冊步驟的查詢會被拒絕。 所有讀寫、存在性比對、指紋與匯出共用此欄位；只持有本次查閱 ID，不讀取安全設定內容。
 * - 組合一次性的服務、輸出根、時鐘與秘密來源。秘密委派只在實際建立或更新步驟設定時呼叫。
 * - 將清單、組件 PE、組織身分及受管理 metadata 比對，建立沒有憑證值的 plan；不讀取業務記錄，也不解析秘密。
 * - WriteExport 精確支援舊 Plugin、Workflow 與 F3 平台 Plugin 組件名稱；平台只限三核定測試組織。
 *   plan／apply／rollback 委派共用清單關卡，export 亦依相同組件用途映射拒絕跨產品來源；
 *   construction／association 不接受教會舊組件，其他既有 catalog 組織保留原相容性。
 *   回復在首筆寫入前重驗 manifest、雜湊及平台組件／步驟／型別身份，且只清理快照外的精確平台列。
 * - 新三測試組織在 plan／apply 各自核對精確伺服器名稱與非空正式 ID，計畫 hash 綁定 ID，
 *   apply 在 metadata、export 及寫入前重驗；legacy／production 的 null 欄位省略，保留原計畫 bytes 與外層正式授權。
 * - 新測試組織只為 enabledByOrganization 明確為 true 的步驟規劃新增；缺漏或 false 不註冊。
 *   goodshepherdback 保留歷史相容：旗標缺漏時仍建立停用步驟，明確 false 則不建立。
 * - 建立可由人工審核的 Markdown 摘要。輸出只列固定欄位、operation、hash 與佔位名稱， 不列出 unsecure／secure 設定實值、帳密、LINE token 或 userId。
 * - member 安全設定僅在 apply 寫入時組成單欄 tokenKey JSON，建立與更新共用轉換；trace／push 保持原契約，秘密不進 plan、export 或快取。
 *   F3 平台設定已由精確清單驗證且禁止 secure 值，直接使用空安全設定，不套用教會 mode 解析或讀秘密。
 * - 新停用步驟以當次 GUID 在固定兩項交易內建立並停用；交易失敗回滾，不發布暫時啟用列。
 * - smoke 版本輸出只接受本次 task 關聯的唯一診斷追蹤，驗證完整 PluginFileVersion 行後才透過操作區域回呼發布；
 *   原始追蹤與回呼不存入欄位，拒絕缺失、重複或非正規版本，task／timer 仍由原 finally／using 清理。
 * - JSON 備份先在同目錄唯一暫存檔完整寫入、flush 並關閉，再以一次覆寫搬移原子發布。
 *   Windows 不分享 Delete 的讀取 handle 可回報 access denied；僅對精確拒絕／共享／鎖定碼
 *   在首次受阻後等待最多一秒、每次最多二十五毫秒，不改 ACL、不先刪舊檔、不重寫候選。
 *   到期保留第一次原例外，finally 清理本次暫存；沒有背景重試或跨命令共享的可變狀態。
 * 主要操作入口包含 Plan、Markdown、Apply、WriteExport、Rollback、SmokeAsync、Inspect、CreateAssembly；其餘入口見下方成員契約。
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 單一 Dataverse 服務租約上的佈署操作核心。物件不跨命令共用；它不保存 user／tenant 狀態、
/// 已解析的秘密或外部 client。計畫與匯出只包含非敏感資料，所有檔案 writer 都由呼叫期間擁有。
/// </summary>
internal sealed partial class DeploymentEngine
{
    private readonly OrganizationCatalog catalog;
    private const string AssemblyLogicalName = "pluginassembly";
    private const string PlatformAssemblyName = "SpeechMessage.Dynamics.Platform.Plugins";
    private const string SlotRequestPluginType =
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin";
    private const string ChannelCreatedPluginType =
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin";
    private const string TypeLogicalName = "plugintype";
    private const string StepLogicalName = "sdkmessageprocessingstep";
    private const string ImageLogicalName = "sdkmessageprocessingstepimage";
    /// <summary>
    /// 步驟安全設定查閱的官方邏輯名稱；不可縮寫成 secureconfigid，否則已註冊步驟的查詢會被拒絕。
    /// 所有讀寫、存在性比對、指紋與匯出共用此欄位；只持有本次查閱 ID，不讀取安全設定內容。
    /// </summary>
    private const string StepSecureConfigurationLookup = "sdkmessageprocessingstepsecureconfigid";
    private const string TraceLogicalName = "plugintracelog";
    private const string AsyncOperationLogicalName = "asyncoperation";
    private const string TaskLogicalName = "task";
    private const long MaximumExportBytes = 100L * 1024 * 1024;
    private static readonly Regex SecretPlaceholderRegex =
        new(@"\$\{secret:([A-Za-z0-9_.-]{1,100})\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IOrganizationService service;
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly Func<string, string> secret;

    /// <summary>
    /// 組合一次性的服務、輸出根、時鐘與秘密來源。秘密委派只在實際建立或更新步驟設定時呼叫。
    /// </summary>
    /// <param name="service">本命令唯一的 Dataverse 服務。</param>
    /// <param name="directory">受信任的本機 plan／export 輸出根。</param>
    /// <param name="clock">UTC 與 smoke timeout 使用的可控時鐘。</param>
    /// <param name="secret">依佔位名稱取得 User Secrets 值的委派。</param>
    /// <param name="catalog">命令核定白名單；省略僅載入repo清單，不允許任意組織。</param>
    /// <exception cref="ArgumentNullException">必要依賴為 null。</exception>
    /// <exception cref="ArgumentException">輸出目錄為空白。</exception>
    public DeploymentEngine(IOrganizationService service, string directory, TimeProvider clock, Func<string, string> secret, OrganizationCatalog? catalog = null)
    {
        this.catalog = catalog ?? OrganizationCatalog.LoadDefault();
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.secret = secret ?? throw new ArgumentNullException(nameof(secret));
    }

    /// <summary>
    /// 將清單、組件 PE 與組織中的受管理 metadata 作離線式比對，建立沒有憑證值的 plan。
    /// 新三測試組織先讀伺服器身分並綁定正式 ID，再讀組件、類型、訊息、篩選器與步驟；
    /// 不讀業務記錄或解析秘密，身分回應不跨本次查核保留，連線仍由外層 lease 擁有。
    /// </summary>
    /// <param name="manifest">經過格式與佔位驗證的註冊清單。</param>
    /// <param name="image">已以 MetadataReader 驗證的 Plugin 組件內容。</param>
    /// <param name="organization">命令catalog核定的組織；正式旗標及確認仍由共同CLI入口負責。</param>
    /// <returns>雜湊固定且僅含非敏感資訊的人工審核計畫。</returns>
    /// <exception cref="ArgumentNullException">清單或組件為 null。</exception>
    /// <exception cref="InvalidDataException">清單不符合 P0 範圍、伺服器身分不符或既有組件身份衝突。</exception>
    public DeploymentPlan Plan(RegistrationManifest manifest, AssemblyImage image, string organization)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(image);
        ValidateOrganization(organization);
        ValidateManifest(manifest, image);
        ValidatePlatformOrganization(manifest, organization);
        var organizationId = ReadRegistrationOrganizationId(organization);
        ValidateRegistrationAttributes(manifest);

        var inspection = Inspect(manifest, image, organization);
        var placeholders = manifest.Steps
            .SelectMany(step => ExtractSecretNames(step.UnsecureConfig).Concat(ExtractSecretNames(step.SecureConfig)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var unsignedPlan = new DeploymentPlan(
            organization,
            clock.GetUtcNow().ToUniversalTime(),
            inspection.Fingerprint,
            HashText(CanonicalManifest(manifest)),
            image.Sha256,
            inspection.Operations.AsReadOnly(),
            string.Empty)
        {
            SecretPlaceholders = Array.AsReadOnly(placeholders),
            OrganizationId = organizationId
        };

        return unsignedPlan with { Sha256 = HashPlan(unsignedPlan) };
    }

    /// <summary>
    /// 建立可由人工審核的 Markdown 摘要。輸出只列固定欄位、operation、hash 與佔位名稱，
    /// 不列出 unsecure／secure 設定實值、帳密、LINE token 或 userId。
    /// </summary>
    /// <param name="plan">已建立的 plan。</param>
    /// <returns>不含秘密值的 UTF-8 文字內容。</returns>
    public static string Markdown(DeploymentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var builder = new StringBuilder();
        builder.AppendLine("# Dynamics Plugin 佈署計畫");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 組織：{plan.Organization}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 格式版本：{plan.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- UTC：{plan.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 組織指紋：`{plan.Fingerprint}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 清單 SHA-256：`{plan.ManifestHash}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 組件 SHA-256：`{plan.AssemblyHash}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Plan SHA-256：`{plan.Sha256}`");
        builder.AppendLine();
        builder.AppendLine("## 操作");
        if (plan.Operations.Count == 0)
        {
            builder.AppendLine("- 無差異");
        }
        else
        {
            foreach (var operation in plan.Operations)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {operation.Kind}: `{operation.Key}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## User Secrets 佔位名稱");
        if (plan.SecretPlaceholders.Count != 0)
        {
            builder.AppendLine("Secret values are not compared. This is not a secret-rotation plan.");
            builder.AppendLine("Verify cannot establish full equality for these values; unchanged metadata yields incomplete exit 3.");
        }

        if (plan.SecretPlaceholders.Count == 0)
        {
            builder.AppendLine("- 無");
        }
        else
        {
            foreach (var name in plan.SecretPlaceholders)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- `${{secret:{name}}}`");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 驗證 plan hash 後重讀新三測試組織的名稱及正式 ID，必須與 plan 綁定相同；
    /// 缺少或空 ID 不能降級成 legacy。接著驗來源清單、DLL 與即時 metadata 指紋，
    /// 再完成安全 export 並依核准操作寫入。身分失敗早於 metadata、export 及任何 SDK 寫入；
    /// 不保存身分回應、不解析秘密，連線失敗清理由外層唯一 lease 擁有者負責。
    /// </summary>
    /// <param name="plan">經人工審核的 plan。</param>
    /// <param name="manifest">產生 plan 時相同的清單範本。</param>
    /// <param name="image">產生 plan 時相同的 Plugin DLL。</param>
    /// <returns>已寫入並關閉的目前狀態 export 檔案位置。</returns>
    /// <exception cref="InvalidDataException">plan 被竄改、來源變更或組織現況已改變。</exception>
    public string Apply(DeploymentPlan plan, RegistrationManifest manifest, AssemblyImage image)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(image);
        ValidateOrganization(plan.Organization);
        ValidateManifest(manifest, image);
        ValidatePlatformOrganization(manifest, plan.Organization);
        ValidatePlanHash(plan);
        if (plan.OrganizationId != ReadRegistrationOrganizationId(plan.Organization))
            throw new InvalidDataException("Registration plan organization identity has changed or is missing.");
        ValidateRegistrationAttributes(manifest);

        if (!string.Equals(plan.ManifestHash, HashText(CanonicalManifest(manifest)), StringComparison.Ordinal) ||
            !string.Equals(plan.AssemblyHash, image.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plan 的來源清單或組件已變更。");
        }

        var inspection = Inspect(manifest, image, plan.Organization);
        if (!string.Equals(plan.Fingerprint, inspection.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("組織現況已改變，必須重新產生 plan。");
        }

        var exportPath = Path.Combine(directory, "export.json");
        WriteExport(plan.Organization, exportPath, manifest.AssemblyName);

        foreach (var operation in plan.Operations)
        {
            switch (operation.Kind)
            {
                case "CreateAssembly":
                    CreateAssembly(image);
                    break;
                case "UpdateAssembly":
                    UpdateAssembly(image);
                    break;
                case "CreateType":
                    CreatePluginType(operation.Key, manifest, image);
                    break;
                case "CreateActivity":
                case "UpdateActivity":
                    WriteActivity(operation.Key, manifest);
                    break;
                case "ReconcileImages":
                    ReconcileImages(operation.Key, manifest);
                    break;
                case "CreateStep":
                    CreateStep(operation.Key, manifest, plan.Organization);
                    break;
                case "UpdateStepConfiguration":
                    UpdateStepConfiguration(operation.Key, manifest);
                    break;
                case "UpdateStepState":
                    UpdateStepState(operation.Key, manifest, plan.Organization);
                    break;
                default:
                    throw new InvalidDataException("Plan 包含不支援的操作種類。");
            }
        }

        return exportPath;
    }

    /// <summary>
    /// 將受管理組件、Plugin 型別及步驟安全匯出到指定本機路徑。secure 設定只記錄有無，
    /// unsecure 設定只進指紋雜湊；原始 userId 與 token 不會序列化。
    /// 先由 catalog 核定組織，再委派共用用途映射驗證組件產品邊界；不適用時在 SDK 查詢及
    /// 檔案候選建立前拒絕，不因只讀匯出而容許教會與平台組件跨產品混用。
    /// </summary>
    /// <param name="organization">catalog核定的canonical組織名稱。</param>
    /// <param name="path">輸出 JSON 檔案路徑。</param>
    /// <param name="assemblyName">核定組件簡名，預設維持既有 Plugin 匯出範圍。</param>
    /// <returns>完成原子寫入後的絕對路徑。</returns>
    public string WriteExport(string organization, string path, string assemblyName = "SpeechMessage.Dynamics.Plugins")
    {
        ValidateOrganization(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsSupportedManagedAssemblyName(assemblyName))
            throw new InvalidDataException("Managed assembly name is invalid.");
        if (!RegistrationManifestLoader.IsAssemblyAllowed(assemblyName, organization))
            throw new InvalidDataException("The managed assembly is not allowed for the selected organization.");
        var export = CaptureExport(organization, assemblyName);
        WriteJsonAtomically(path, export);
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// 根據 SHA-256 驗證過的 export 還原組件內容與既有步驟啟用狀態。
    /// 秘密設定不落入 export；若待還原的步驟列已被刪除，會 fail closed 而不建立不完整替代品。
    /// </summary>
    /// <param name="path">先前由本工具產生的 export JSON。</param>
    /// <param name="manifest">用來限定受管理步驟範圍的清單。</param>
    /// <param name="organization">必須與 export 中的組織相同。</param>
    /// <exception cref="InvalidDataException">export 竄改、組織不符或無法安全還原。</exception>
    public void Rollback(string path, RegistrationManifest manifest, string organization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateOrganization(organization);
        RegistrationManifestLoader.Validate(manifest);
        ValidatePlatformOrganization(manifest, organization);
        var export = ReadExport(path);
        ValidateExport(export, organization);
        if (export.ManagedAssemblyName != manifest.AssemblyName ||
            (export.Assembly is not null && export.Assembly.Name != manifest.AssemblyName) || export.Images is null ||
            export.Images.Any(image => image.Id == Guid.Empty || !export.Steps.Any(step => step.Id == image.StepId)) ||
            export.Images.Select(image => image.Id).Distinct().Count() != export.Images.Count)
            throw new InvalidDataException("Export does not match the target assembly or image parent.");
        (Entity[] Types, Entity[] Steps)? platformRollbackRows =
            string.Equals(manifest.AssemblyName, PlatformAssemblyName, StringComparison.Ordinal)
            ? ValidatePlatformRollbackScope(manifest, export)
            : null;
        // 所有原步驟必須存在且仍屬原型別；缺失時在組件更新前拒絕，避免部分回復。
        foreach (var previous in export.Steps)
        {
            var current = FindStepById(previous.Id) ?? throw new InvalidDataException("Original step no longer exists.");
            if (!export.Types.Any(type => type.Id == ReadReferenceId(current, "eventhandler")))
                throw new InvalidDataException("Original step type identity has changed.");
        }

        var currentAssembly = FindAssembly(manifest.AssemblyName);
        if (export.Assembly is null)
        {
            if (currentAssembly is not null)
            {
                DeleteManagedRows(currentAssembly.Id, manifest.AssemblyName);
            }

            return;
        }

        if (currentAssembly is null)
        {
            var assembly = ToAssemblyEntity(export.Assembly);
            service.Create(assembly);
        }
        else
        {
            if (platformRollbackRows is not null)
                RemovePlatformRowsAbsentFromExport(export, platformRollbackRows.Value);
            service.Update(ToAssemblyEntity(export.Assembly, currentAssembly.Id));
        }

        var restoredAssembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("Rollback assembly does not exist.");
        if (export.SchemaVersion == 2) RestoreRegistrationMetadata(export, restoredAssembly.Id);

        foreach (var previousStep in export.Steps)
        {
            var currentStep = FindStepById(previousStep.Id);
            if (currentStep is null)
            {
                throw new InvalidDataException("Export 所需步驟已不存在，且不含可安全還原的設定。");
            }

            var previousState = previousStep.Enabled ? 0 : 1;
            var previousStatus = previousStep.Enabled ? 1 : 2;
            if (ReadOption(currentStep, "statecode") != previousState ||
                ReadOption(currentStep, "statuscode") != previousStatus)
            {
                service.Update(new Entity(StepLogicalName, currentStep.Id)
                {
                    ["statecode"] = new OptionSetValue(previousState),
                    ["statuscode"] = new OptionSetValue(previousStatus)
                });
            }
        }
    }

    /// <summary>
    /// 回復平台組件前，以核定三步驟清單驗證快照及目前 Dataverse 列；只接受精確型別、
    /// 自然步驟身份與 ProductBefore 映像，並先物化完整列集合。任何未知、重複、歸屬變更
    /// 或 secure config 都在第一筆寫入前拒絕，避免 hash 可重算的手造 JSON 擴大刪除範圍。
    /// </summary>
    /// <param name="manifest">已通過 Loader 精確三步驟驗證的本次平台清單。</param>
    /// <param name="export">已完成格式、組織與雜湊核對的本機回復快照。</param>
    /// <returns>通過完整預檢的目前型別與步驟列；僅在平台 Assembly 分支使用。</returns>
    /// <exception cref="InvalidDataException">快照或目前列含非 F3 身份、重複身份或無法安全回復的設定。</exception>
    private (Entity[] Types, Entity[] Steps) ValidatePlatformRollbackScope(
        RegistrationManifest manifest,
        DeploymentExport export)
    {
        if (!string.Equals(export.ManagedAssemblyName, PlatformAssemblyName, StringComparison.Ordinal) ||
            (export.Assembly is not null &&
             !string.Equals(export.Assembly.Name, PlatformAssemblyName, StringComparison.Ordinal)))
            throw new InvalidDataException("Platform rollback export assembly identity is invalid.");

        var expectedTypes = manifest.Steps.Select(step => step.Type).ToHashSet(StringComparer.Ordinal);
        var exportTypeIds = new HashSet<Guid>();
        var exportTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in export.Types)
        {
            if (type.Id == Guid.Empty || !exportTypeIds.Add(type.Id) ||
                !expectedTypes.Contains(type.TypeName) || !exportTypeNames.Add(type.TypeName))
                throw new InvalidDataException("Platform rollback export contains an unknown or duplicate Plugin type.");
        }

        var exportStepIds = new HashSet<Guid>();
        var exportStepKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in export.Steps)
        {
            var registration = FindPlatformRegistration(manifest, step);
            if (step.Id == Guid.Empty || !exportStepIds.Add(step.Id) ||
                !exportTypeNames.Contains(step.TypeName) ||
                !exportStepKeys.Add(StepKey(registration)))
                throw new InvalidDataException("Platform rollback export contains an unknown or duplicate step.");
        }

        var exportImagesByStep = export.Steps.ToDictionary(step => step.Id, step => new HashSet<string>(StringComparer.Ordinal));
        foreach (var image in export.Images)
        {
            var parent = export.Steps.SingleOrDefault(step => step.Id == image.StepId);
            if (parent is null ||
                !exportImagesByStep.TryGetValue(image.StepId, out var names) ||
                !names.Add(image.Name) ||
                !IsAllowedPlatformImage(FindPlatformRegistration(manifest, parent), image))
                throw new InvalidDataException("Platform rollback export contains an unknown or duplicate image.");
        }

        var assembly = FindAssembly(PlatformAssemblyName);
        if (assembly is null)
        {
            if (export.Types.Count != 0 || export.Steps.Count != 0 || export.Images.Count != 0)
                throw new InvalidDataException("Platform rollback rows are missing from their owning assembly.");
            return (Array.Empty<Entity>(), Array.Empty<Entity>());
        }

        var currentTypes = ReadPluginTypes(assembly.Id);
        var currentTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in currentTypes)
        {
            var typeName = type.GetAttributeValue<string>("typename");
            if (!expectedTypes.Contains(typeName ?? string.Empty) ||
                !currentTypeNames.Add(typeName!))
                throw new InvalidDataException("Current platform assembly contains an unknown or duplicate Plugin type.");
        }

        var currentTypeIds = currentTypes.Select(type => type.Id).ToHashSet();
        var currentSteps = ReadSteps(currentTypeIds);
        var currentStepIds = new HashSet<Guid>();
        var currentStepKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in currentSteps)
        {
            var snapshot = ToStepExport(step, currentTypes);
            var registration = FindPlatformRegistration(manifest, snapshot);
            if (!currentStepIds.Add(step.Id) ||
                !currentStepKeys.Add(StepKey(registration)) ||
                snapshot.HasSecureConfig ||
                !string.Equals(step.GetAttributeValue<string>("filteringattributes") ?? string.Empty,
                    string.Join(",", registration.FilteringAttributes), StringComparison.Ordinal))
                throw new InvalidDataException("Current platform assembly contains an unknown, duplicate or unsafe step.");

            foreach (var image in ReadImages(step.Id))
            {
                if (!IsAllowedPlatformImage(registration, ToImageExport(image)))
                    throw new InvalidDataException("Current platform step contains an unknown image.");
            }
        }

        foreach (var previousType in export.Types)
        {
            if (!currentTypes.Any(type => type.Id == previousType.Id &&
                string.Equals(type.GetAttributeValue<string>("typename"), previousType.TypeName, StringComparison.Ordinal)))
                throw new InvalidDataException("Original platform Plugin type no longer exists.");
        }

        return (currentTypes, currentSteps);
    }

    /// <summary>依完整自然身份尋找唯一核定平台步驟；export 欄位不可替代 manifest 白名單。</summary>
    private static StepRegistration FindPlatformRegistration(RegistrationManifest manifest, PluginStepExport step)
    {
        return manifest.Steps.SingleOrDefault(registration =>
                   string.Equals(registration.Type, step.TypeName, StringComparison.Ordinal) &&
                   string.Equals(registration.Message, step.Message, StringComparison.Ordinal) &&
                   string.Equals(registration.Entity, step.Entity, StringComparison.Ordinal) &&
                   ReadStage(registration.Stage) == step.Stage &&
                   ReadMode(registration.Mode) == step.Mode &&
                   registration.Rank == step.Rank)
               ?? throw new InvalidDataException("Platform rollback step is not present in the exact registration manifest.");
    }

    /// <summary>平台步驟只允許快照／清單明列的具名映像，避免偽造匯出刪除或還原其他影像。</summary>
    private static bool IsAllowedPlatformImage(StepRegistration registration, PluginImageExport image) =>
        registration.Images.Any(expected =>
            string.Equals(image.Name, expected.Name, StringComparison.Ordinal) &&
            string.Equals(image.Alias, expected.Name, StringComparison.Ordinal) &&
            image.Type == (expected.Type == "PreImage" ? 0 : 1) &&
            string.Equals(image.Attributes,
                string.Join(",", expected.Attributes.OrderBy(attribute => attribute, StringComparer.Ordinal)),
                StringComparison.Ordinal) &&
            string.Equals(image.MessageProperty,
                registration.Message == "Create" ? "Id" : "Target", StringComparison.Ordinal));

    /// <summary>
    /// 回復快照既有的列狀態後，刪除只在後續部署新增的 F3 步驟及 Plugin 型別。
    /// 預檢已證明候選全屬精確平台組件；刪除順序先影像、步驟、型別，保留快照原有資料庫 ID。
    /// </summary>
    private void RemovePlatformRowsAbsentFromExport(
        DeploymentExport export,
        (Entity[] Types, Entity[] Steps) current)
    {
        foreach (var step in current.Steps)
        {
            if (export.Steps.Any(previous => previous.Id == step.Id)) continue;
            foreach (var image in ReadImages(step.Id)) service.Delete(ImageLogicalName, image.Id);
            service.Delete(StepLogicalName, step.Id);
        }

        foreach (var type in current.Types)
        {
            if (!export.Types.Any(previous => previous.Id == type.Id))
                service.Delete(TypeLogicalName, type.Id);
        }
    }

    /// <summary>
    /// 建立本次專用 task，有限等待對應 asyncoperation 與 Plugin trace；成功、斷言失敗、
    /// 逾時或 caller cancellation 都會在 finally 刪除該 task。只查詢此 task 關聯的記錄。
    /// 首次立即查詢，後續每兩秒重試，總期限維持三分鐘；避免對與正式組織共用的地端主機施加高頻輪詢。
    /// timeout／linked token 及延遲 timer 都由此操作擁有並確定性釋放，不保留連線或跨命令狀態。
    /// </summary>
    /// <param name="scenario">P0 核准的 trace 或 push 情境。</param>
    /// <param name="ct">呼叫端取消 token。</param>
    /// <returns>追蹤內容符合固定情境時為 true；未到達或不符合時為 false。</returns>
    /// <exception cref="OperationCanceledException">呼叫端主動取消等待。</exception>
    public Task<bool> SmokeAsync(string scenario, CancellationToken ct) =>
        SmokeAsync(scenario, null, ct);

    /// <summary>
    /// 執行相同有界 smoke，並選擇性發布唯一且正規的 PluginFileVersion 行。
    /// 提供回呼時必須有四段 0～65535 的 ASCII 正規版本及成功標記，否則回 false 且不發布。
    /// 不提供回呼時保留舊版追蹤尚無版本欄位的 bool 行為，供既有呼叫端相容使用。
    /// 回呼僅借用至本次操作完成，不存入欄位；原始 trace 不傳出，回呼失敗仍經 finally 刪除 task，
    /// 真實例外由 CLI 共用入口處理。三分鐘期限、兩秒輪詢及 caller 取消分類與原入口相同。
    /// </summary>
    /// <param name="scenario">只允許 P0 trace 或 push 情境。</param>
    /// <param name="publishVersion">只接收完整安全版本行的操作區域回呼；null 保留舊 bool 驗證。</param>
    /// <param name="ct">本次操作呼叫端的取消訊號。</param>
    /// <returns>成功標記及所需版本驗證皆通過為 true；逾時或驗證失敗為 false。</returns>
    /// <exception cref="OperationCanceledException">呼叫端取消，仍完成 task 清理。</exception>
    public async Task<bool> SmokeAsync(string scenario, Action<string>? publishVersion, CancellationToken ct)
    {
        if (scenario is not ("p0-ping-trace" or "p0-ping-push"))
        {
            throw new ArgumentException("Smoke 情境不在 P0 白名單中。", nameof(scenario));
        }

        ct.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var runId = Guid.NewGuid();
        var taskId = service.Create(new Entity(TaskLogicalName)
        {
            ["subject"] = $"[smoke] P0-{runId:N}",
            ["description"] = "佈署工具自動化追蹤驗證"
        });

        try
        {
            while (!linked.IsCancellationRequested)
            {
                var trace = ReadSmokeTrace(taskId);
                if (trace is not null)
                {
                    if (!IsExpectedSmokeTrace(trace, scenario))
                    {
                        return false;
                    }

                    if (publishVersion is not null)
                    {
                        var versionLine = ReadSmokeVersionLine(trace);
                        if (versionLine is null)
                        {
                            return false;
                        }

                        ct.ThrowIfCancellationRequested();
                        if (timeout.IsCancellationRequested)
                        {
                            return false;
                        }

                        publishVersion(versionLine);
                    }

                    return true;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), clock, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    return false;
                }
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }
        finally
        {
            service.Delete(TaskLogicalName, taskId);
        }
    }

    /// <summary>只查受管理組件與清單中的穩定步驟身份，建立操作候選；不解析秘密、不寫入資料，完成後才發布計畫。</summary>
    private Inspection Inspect(RegistrationManifest manifest, AssemblyImage image, string organization)
    {
        var assembly = FindAssembly(manifest.AssemblyName);
        var operations = new List<DeploymentOperation>();
        IReadOnlyList<Entity> pluginTypes = Array.Empty<Entity>();

        if (assembly is null)
        {
            operations.Add(new DeploymentOperation("CreateAssembly", "assembly"));
        }
        else
        {
            ValidateAssemblyCompatibility(assembly, image);
            pluginTypes = ReadPluginTypes(assembly.Id);
            var currentContent = assembly.GetAttributeValue<string>("content") ?? string.Empty;
            if (!string.Equals(HashBase64(currentContent), image.Sha256, StringComparison.Ordinal) ||
                !string.Equals(assembly.GetAttributeValue<string>("version"), image.Version, StringComparison.Ordinal))
            {
                operations.Add(new DeploymentOperation("UpdateAssembly", "assembly"));
            }
        }

        var messageByName = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var filterByIdentity = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var plannedTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in manifest.Steps)
        {
            if (!messageByName.TryGetValue(step.Message, out var message))
            {
                message = FindMessage(step.Message) ?? throw new InvalidDataException("Plugin 訊息不存在於組織 metadata。");
                messageByName.Add(step.Message, message);
            }

            var filterKey = step.Message + "|" + step.Entity;
            if (!filterByIdentity.TryGetValue(filterKey, out var filter))
            {
                filter = FindMessageFilter(message.Id, step.Entity) ??
                    throw new InvalidDataException("Plugin 訊息與實體沒有可註冊的篩選器。");
                filterByIdentity.Add(filterKey, filter);
            }

            var shouldEnable = IsEnabled(step, organization);
            var pluginType = pluginTypes.SingleOrDefault(type =>
                string.Equals(type.GetAttributeValue<string>("typename"), step.Type, StringComparison.Ordinal));
            if (pluginType is null)
            {
                if (plannedTypes.Add(step.Type)) operations.Add(new DeploymentOperation("CreateType", step.Type));
                if (ShouldCreateStep(step, organization))
                {
                    operations.Add(new DeploymentOperation("CreateStep", StepKey(step)));
                    if (step.Images.Count != 0) operations.Add(new DeploymentOperation("ReconcileImages", StepKey(step)));
                }

                continue;
            }

            var stage = ReadStage(step.Stage);
            var mode = ReadMode(step.Mode);
            var existingStep = FindStep(pluginType.Id, message.Id, filter.Id, stage, mode, step.Rank);
            if (existingStep is null)
            {
                if (ShouldCreateStep(step, organization))
                {
                    operations.Add(new DeploymentOperation("CreateStep", StepKey(step)));
                    if (step.Images.Count != 0) operations.Add(new DeploymentOperation("ReconcileImages", StepKey(step)));
                }

                continue;
            }

            if (!ImagesMatch(ReadImages(existingStep.Id), step))
                operations.Add(new DeploymentOperation("ReconcileImages", StepKey(step)));

            if (!string.Equals(existingStep.GetAttributeValue<string>("filteringattributes") ?? "", string.Join(",", step.FilteringAttributes), StringComparison.Ordinal))
                operations.Add(new DeploymentOperation("UpdateStepConfiguration", StepKey(step)));
            else if (!NonSecretConfigurationMatches(existingStep, step))
            {
                operations.Add(new DeploymentOperation("UpdateStepConfiguration", StepKey(step)));
            }

            if ((ReadOption(existingStep, "statecode") == 0) != shouldEnable)
            {
                operations.Add(new DeploymentOperation("UpdateStepState", StepKey(step)));
            }
        }

        foreach (var activity in manifest.Activities)
        {
            var existing = pluginTypes.SingleOrDefault(type => type.GetAttributeValue<string>("typename") == activity.Type);
            if (existing is null) operations.Add(new DeploymentOperation("CreateActivity", activity.Type));
            else if (existing.GetAttributeValue<string>("friendlyname") != activity.DisplayName ||
                existing.GetAttributeValue<string>("workflowactivitygroupname") != manifest.ActivityGroup)
                operations.Add(new DeploymentOperation("UpdateActivity", activity.Type));
        }

        return new Inspection(CalculateFingerprint(manifest.AssemblyName), operations);
    }

    /// <summary>建立 Sandbox／Database 組件；輸入內容只屬本次經雜湊驗證的 DLL，不接受任意外部部署模式。</summary>
    private void CreateAssembly(AssemblyImage image)
    {
        var assembly = new Entity(AssemblyLogicalName)
        {
            ["name"] = image.Name,
            ["version"] = image.Version,
            ["publickeytoken"] = image.PublicKeyToken,
            ["content"] = image.Content,
            ["isolationmode"] = new OptionSetValue(2),
            ["sourcetype"] = new OptionSetValue(0)
        };
        service.Create(assembly);
    }

    /// <summary>更新內容前再次確認公鑰及主次版本相容；保留組件 ID，避免破壞既有步驟關聯。</summary>
    private void UpdateAssembly(AssemblyImage image)
    {
        var existing = FindAssembly(image.Name) ?? throw new InvalidDataException("Plan 所需組件已不存在。");
        ValidateAssemblyCompatibility(existing, image);
        service.Update(new Entity(AssemblyLogicalName, existing.Id)
        {
            ["version"] = image.Version,
            ["content"] = image.Content
        });
    }

    /// <summary>只為已通過 PE 與清單一致性驗證的型別建立組件關聯；不載入執行待部署程式。</summary>
    private void CreatePluginType(string typeName, RegistrationManifest manifest, AssemblyImage image)
    {
        var assembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("Plan 所需組件不存在。");
        if (!image.PluginTypes.Contains(typeName, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Plan 指定的 Plugin 型別不在組件中。");
        }

        var simpleName = typeName[(typeName.LastIndexOf('.') + 1)..];
        service.Create(new Entity(TypeLogicalName)
        {
            ["name"] = simpleName,
            ["typename"] = typeName,
            ["friendlyname"] = simpleName,
            ["pluginassemblyid"] = assembly.ToEntityReference()
        });
    }

    /// <summary>
    /// 建立步驟並維持清單核定狀態。CRM 9.1 建立時使用初始啟用狀態，
    /// 因此停用步驟須以當次預配 GUID，在同一交易內建立合法初始列並立即停用；
    /// 任一項失敗即回滾，交易外不能看到啟用列。明確啟用仍直接建立。
    /// 固定兩項同步請求只借用本命令 SDK 租約，不建立背景工作或跨命令狀態；
    /// 秘密僅在寫入框架解析，交易失敗沿既有路徑清除事先建立的 secure 設定。
    /// </summary>
    private void CreateStep(string stepKey, RegistrationManifest manifest, string organization)
    {
        var registration = FindStepRegistration(manifest, stepKey);
        var assembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("Plan 所需組件不存在。");
        var pluginType = ReadPluginTypes(assembly.Id).SingleOrDefault(type =>
            string.Equals(type.GetAttributeValue<string>("typename"), registration.Type, StringComparison.Ordinal))
            ?? throw new InvalidDataException("Plan 所需 Plugin 型別不存在。");
        var message = FindMessage(registration.Message) ?? throw new InvalidDataException("Plan 所需訊息不存在。");
        var filter = FindMessageFilter(message.Id, registration.Entity) ??
            throw new InvalidDataException("Plan 所需訊息篩選器不存在。");

        var unsecureConfiguration = ResolvePlaceholders(registration.UnsecureConfig);
        var secureConfiguration = ResolveSecureConfiguration(registration);
        EntityReference? secureConfigurationReference = null;
        Guid? secureConfigurationId = null;
        if (secureConfiguration.Length > 0)
        {
            var secureEntity = new Entity("sdkmessageprocessingstepsecureconfig")
            {
                ["secureconfig"] = secureConfiguration
            };
            secureConfigurationId = service.Create(secureEntity);
            secureConfigurationReference = new EntityReference(secureEntity.LogicalName, secureConfigurationId.Value);
        }

        try
        {
            var enabled = IsEnabled(registration, organization);
            var step = new Entity(StepLogicalName)
            {
                ["name"] = StepDisplayName(registration),
                ["eventhandler"] = pluginType.ToEntityReference(),
                ["sdkmessageid"] = message.ToEntityReference(),
                ["sdkmessagefilterid"] = filter.ToEntityReference(),
                ["stage"] = new OptionSetValue(ReadStage(registration.Stage)),
                ["mode"] = new OptionSetValue(ReadMode(registration.Mode)),
                ["rank"] = registration.Rank,
                ["filteringattributes"] = string.Join(",", registration.FilteringAttributes),
                ["configuration"] = unsecureConfiguration,
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1)
            };
            if (secureConfigurationReference is not null)
            {
                step[StepSecureConfigurationLookup] = secureConfigurationReference;
            }

            if (enabled)
            {
                service.Create(step);
            }
            else
            {
                step.Id = Guid.NewGuid();
                var disabled = new Entity(StepLogicalName, step.Id)
                {
                    ["statecode"] = new OptionSetValue(1),
                    ["statuscode"] = new OptionSetValue(2)
                };
                service.Execute(new ExecuteTransactionRequest
                {
                    ReturnResponses = false,
                    Requests = new OrganizationRequestCollection
                    {
                        new CreateRequest { Target = step },
                        new UpdateRequest { Target = disabled }
                    }
                });
            }
        }
        catch
        {
            if (secureConfigurationId.HasValue)
            {
                service.Delete("sdkmessageprocessingstepsecureconfig", secureConfigurationId.Value);
            }

            throw;
        }
    }

    /// <summary>
    /// 比對既有步驟的公開模式與文化欄位，不讀取秘密來源或 secureconfig 值。
    /// lineUserId 僅檢查已解析且非空後，從比較候選移除；true 只代表公開 metadata 相同，不是秘密值相同。
    /// CLI 對任何秘密佔位回報驗證未完成（非零），計畫摘要也明列不支援秘密輪替，避免守門假綠燈。
    /// 所有 JSON 與值只屬本次呼叫，回傳值及 plan 不保存該值，避免個資落檔。
    /// </summary>
    private static bool NonSecretConfigurationMatches(Entity existing, StepRegistration desired)
    {
        var hasSecure = ReadReferenceId(existing, StepSecureConfigurationLookup).HasValue;
        if (hasSecure != (desired.SecureConfig.Length > 0))
        {
            return false;
        }

        try
        {
            var current = JsonNode.Parse(existing.GetAttributeValue<string>("configuration") ?? "") as JsonObject;
            var expected = JsonNode.Parse(desired.UnsecureConfig)!.AsObject();
            if (current is null)
            {
                return false;
            }

            if (expected.ContainsKey("lineUserId"))
            {
                if (current["lineUserId"] is not JsonValue value ||
                    !value.TryGetValue<string>(out var userId) || string.IsNullOrWhiteSpace(userId))
                {
                    return false;
                }

                // 明確排除未驗證的秘密欄位，不能用既有值回填 expected 來暗示相等。
                expected.Remove("lineUserId");
                current.Remove("lineUserId");
            }

            return JsonNode.DeepEquals(current, expected);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 以原資料庫 ID 更新設定。秘密僅在本次寫入解析，secure 列先建後連結；
    /// 連結失敗清理新列，成功切回 trace 後才刪除舊 secure 列，不留下無主秘密。
    /// </summary>
    private void UpdateStepConfiguration(string stepKey, RegistrationManifest manifest)
    {
        var registration = FindStepRegistration(manifest, stepKey);
        var assembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("Plan 所需組件不存在。");
        var pluginType = ReadPluginTypes(assembly.Id).Single(type =>
            string.Equals(type.GetAttributeValue<string>("typename"), registration.Type, StringComparison.Ordinal));
        var message = FindMessage(registration.Message) ?? throw new InvalidDataException("Plan 所需訊息不存在。");
        var filter = FindMessageFilter(message.Id, registration.Entity) ?? throw new InvalidDataException("Plan 所需篩選器不存在。");
        var step = FindStep(pluginType.Id, message.Id, filter.Id, ReadStage(registration.Stage), ReadMode(registration.Mode), registration.Rank)
            ?? throw new InvalidDataException("Plan 所需步驟不存在。");
        var oldSecureId = ReadReferenceId(step, StepSecureConfigurationLookup);
        var configuration = JsonNode.Parse(registration.UnsecureConfig)!.AsObject();
        if (configuration.ContainsKey("lineUserId"))
        {
            // 以 JSON API 寫入字串，避免 secrets 中的引號或反斜線破壞 JSON 結構。
            configuration["lineUserId"] = ResolvePlaceholders(configuration["lineUserId"]!.GetValue<string>());
        }

        Guid? newSecureId = null;
        if (registration.SecureConfig.Length > 0)
        {
            newSecureId = service.Create(new Entity("sdkmessageprocessingstepsecureconfig")
            {
                ["secureconfig"] = ResolveSecureConfiguration(registration)
            });
        }

        try
        {
            service.Update(new Entity(StepLogicalName, step.Id)
            {
                ["configuration"] = configuration.ToJsonString(),
                ["filteringattributes"] = string.Join(",", registration.FilteringAttributes),
                [StepSecureConfigurationLookup] = newSecureId.HasValue
                    ? new EntityReference("sdkmessageprocessingstepsecureconfig", newSecureId.Value) : null
            });
        }
        catch
        {
            if (newSecureId.HasValue)
            {
                service.Delete("sdkmessageprocessingstepsecureconfig", newSecureId.Value);
            }

            throw;
        }

        if (oldSecureId.HasValue)
        {
            service.Delete("sdkmessageprocessingstepsecureconfig", oldSecureId.Value);
        }
    }

    /// <summary>依完整穩定身份重新找步驟，只更新啟用狀態；不以顯示名稱合併或重建資料列。</summary>
    private void UpdateStepState(string stepKey, RegistrationManifest manifest, string organization)
    {
        var registration = FindStepRegistration(manifest, stepKey);
        var assembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("Plan 所需組件不存在。");
        var pluginType = ReadPluginTypes(assembly.Id).SingleOrDefault(type =>
            string.Equals(type.GetAttributeValue<string>("typename"), registration.Type, StringComparison.Ordinal))
            ?? throw new InvalidDataException("Plan 所需 Plugin 型別不存在。");
        var message = FindMessage(registration.Message) ?? throw new InvalidDataException("Plan 所需訊息不存在。");
        var filter = FindMessageFilter(message.Id, registration.Entity) ??
            throw new InvalidDataException("Plan 所需訊息篩選器不存在。");
        var step = FindStep(pluginType.Id, message.Id, filter.Id, ReadStage(registration.Stage), ReadMode(registration.Mode), registration.Rank)
            ?? throw new InvalidDataException("Plan 所需步驟不存在。");
        var enabled = IsEnabled(registration, organization);
        service.Update(new Entity(StepLogicalName, step.Id)
        {
            ["statecode"] = new OptionSetValue(enabled ? 0 : 1),
            ["statuscode"] = new OptionSetValue(enabled ? 1 : 2)
        });
    }

    /// <summary>以受管理組件為查詢邊界建立獨立匯出候選；組件內容供還原，設定只保留雜湊與存在性，絕不匯出原始 token 或 userId。</summary>
    private DeploymentExport CaptureExport(string organization, string assemblyName)
    {
        var assembly = FindAssembly(assemblyName);
        if (assembly is null)
        {
            return SealExport(new DeploymentExport(2, organization, clock.GetUtcNow().ToUniversalTime(),
                CalculateFingerprint(assemblyName), null,
                Array.Empty<PluginTypeExport>(), Array.Empty<PluginStepExport>(), string.Empty) { ManagedAssemblyName = assemblyName });
        }

        var typeRows = ReadPluginTypes(assembly.Id);
        var typeIds = typeRows.Select(type => type.Id).ToHashSet();
        var steps = ReadSteps(typeIds)
            .Select(step => ToStepExport(step, typeRows))
            .OrderBy(step => step.Id)
            .ToArray();
        var assemblyExport = new PluginAssemblyExport(
            assembly.Id,
            assembly.GetAttributeValue<string>("name") ?? string.Empty,
            assembly.GetAttributeValue<string>("version") ?? string.Empty,
            assembly.GetAttributeValue<string>("publickeytoken") ?? string.Empty,
            assembly.GetAttributeValue<string>("content") ?? string.Empty,
            ReadOption(assembly, "isolationmode"),
            ReadOption(assembly, "sourcetype"));
        var types = typeRows
            .Select(type => new PluginTypeExport(
                type.Id,
                type.GetAttributeValue<string>("name") ?? string.Empty,
                type.GetAttributeValue<string>("typename") ?? string.Empty)
                { FriendlyName = type.GetAttributeValue<string>("friendlyname") ?? "", ActivityGroup = type.GetAttributeValue<string>("workflowactivitygroupname") ?? "" })
            .OrderBy(type => type.TypeName, StringComparer.Ordinal)
            .ToArray();

        return SealExport(new DeploymentExport(2, organization, clock.GetUtcNow().ToUniversalTime(),
            CalculateFingerprint(assemblyName), assemblyExport,
            Array.AsReadOnly(types), Array.AsReadOnly(steps), string.Empty)
            { ManagedAssemblyName = assemblyName, Images = Array.AsReadOnly(steps.SelectMany(step => ReadImages(step.Id)).Select(ToImageExport).OrderBy(image => image.Id).ToArray()) });
    }

    /// <summary>把單一步驟轉成脫離 SDK Entity 的純值摘要；secure 只記有無，unsecure 只記雜湊。</summary>
    private PluginStepExport ToStepExport(Entity step, IReadOnlyList<Entity> types)
    {
        var typeId = ReadReferenceId(step, "eventhandler");
        var typeName = types.SingleOrDefault(type => type.Id == typeId)?.GetAttributeValue<string>("typename") ?? string.Empty;
        var messageId = ReadReferenceId(step, "sdkmessageid");
        var filterId = ReadReferenceId(step, "sdkmessagefilterid");
        var message = ReadById("sdkmessage", messageId);
        var filter = ReadById("sdkmessagefilter", filterId);
        var secureConfigurationId = ReadReferenceId(step, StepSecureConfigurationLookup);
        return new PluginStepExport(
            step.Id,
            typeName,
            message?.GetAttributeValue<string>("name") ?? string.Empty,
            filter?.GetAttributeValue<string>("primaryobjecttypecode") ?? string.Empty,
            ReadOption(step, "stage"),
            ReadOption(step, "mode"),
            step.GetAttributeValue<int>("rank"),
            ReadOption(step, "statecode") == 0,
            secureConfigurationId.HasValue,
            !string.IsNullOrEmpty(step.GetAttributeValue<string>("configuration")));
    }

    /// <summary>對全部可序列化欄位計算完整性雜湊；雜湊欄位本身先清空，避免自我參照。</summary>
    private static DeploymentExport SealExport(DeploymentExport export)
    {
        var hash = HashText(JsonSerializer.Serialize(export with { Sha256 = string.Empty }));
        return export with { Sha256 = hash };
    }

    /// <summary>只讀有大小上限的本機匯出，解析失敗使用固定錯誤；串流在返回前關閉。</summary>
    private static DeploymentExport ReadExport(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumExportBytes)
        {
            throw new InvalidDataException("Export 不存在、為空或超過允許大小。");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var export = JsonSerializer.Deserialize<DeploymentExport>(stream);
        return export ?? throw new InvalidDataException("Export 格式無效。");
    }

    /// <summary>在任何還原寫入前確認格式、唯一組織與內容雜湊，拒絕跨組織或遭修改的備份。</summary>
    private static void ValidateExport(DeploymentExport export, string organization)
    {
        if (export.Types is null || export.Steps is null || export.Images is null ||
            !IsSupportedManagedAssemblyName(export.ManagedAssemblyName) ||
            (export.Assembly is not null &&
             !string.Equals(export.Assembly.Name, export.ManagedAssemblyName, StringComparison.Ordinal)))
            throw new InvalidDataException("Export managed assembly identity or collections are invalid.");

        var canonical = export.SchemaVersion == 1
            ? JsonSerializer.Serialize(new
            {
                export.SchemaVersion, export.Organization, export.ExportedUtc, export.Fingerprint, export.Assembly,
                Types = export.Types.Select(type => new { type.Id, type.Name, type.TypeName }).ToArray(),
                export.Steps, Sha256 = string.Empty
            })
            : JsonSerializer.Serialize(export with { Sha256 = string.Empty });
        if (export.SchemaVersion is not (1 or 2) ||
            (export.SchemaVersion == 1 && (export.ManagedAssemblyName != "SpeechMessage.Dynamics.Plugins" || export.Images.Count != 0)) ||
            (export.SchemaVersion == 2 && export.ManagedAssemblyName is not
                ("SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" or PlatformAssemblyName)) ||
            !string.Equals(export.Organization, organization, StringComparison.Ordinal) ||
            !string.Equals(export.Sha256, HashText(canonical), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Export 雜湊、版本或組織身份不符。");
        }
    }

    /// <summary>只以匯出中允許的組件欄位還原，資料庫 ID 由既有列或 SDK 決定，不混入步驟機密。</summary>
    private static Entity ToAssemblyEntity(PluginAssemblyExport snapshot, Guid? id = null)
    {
        var entity = id.HasValue
            ? new Entity(AssemblyLogicalName, id.Value)
            : new Entity(AssemblyLogicalName, snapshot.Id);
        entity["name"] = snapshot.Name;
        entity["version"] = snapshot.Version;
        entity["publickeytoken"] = snapshot.PublicKeyToken;
        entity["content"] = snapshot.Content;
        entity["isolationmode"] = new OptionSetValue(snapshot.IsolationMode);
        entity["sourcetype"] = new OptionSetValue(snapshot.SourceType);
        return entity;
    }

    /// <summary>只清理由本工具擁有的組件關聯資料；依步驟、secure、型別、組件順序刪除，避免孤兒設定。</summary>
    private void DeleteManagedRows(Guid assemblyId, string assemblyName)
    {
        var types = ReadPluginTypes(assemblyId);
        foreach (var type in types)
        {
            foreach (var step in ReadSteps(new HashSet<Guid> { type.Id }))
            {
                foreach (var image in ReadImages(step.Id)) service.Delete(ImageLogicalName, image.Id);
                service.Delete(StepLogicalName, step.Id);
            }
        }

        foreach (var type in types)
        {
            service.Delete(TypeLogicalName, type.Id);
        }

        var assembly = FindAssembly(assemblyName);
        if (assembly is not null && assembly.Id == assemblyId)
        {
            service.Delete(AssemblyLogicalName, assemblyId);
        }
    }

    /// <summary>
    /// 只依 smoke task 找到的非空 async operation correlation id 查固定診斷型別的 Plugin trace。
    /// 不同作業可共用 correlation；先驗證作業正式 ID，再讓每個 correlation 僅查一次，
    /// 不刪除或合併作業資料，也不按版本或訊息內容消除真正不同追蹤 ID 的衝突。
    /// 尚無對應記錄時回傳 null；多筆非空診斷追蹤拒絕任選，以免更新回復版本來源不明確。
    /// 所有文字只存於本次呼叫堆疊，不進快取、export 或日誌。
    /// </summary>
    /// <param name="taskId">本次建立且最後必定刪除的 task ID。</param>
    /// <returns>已出現的非空追蹤文字；等待中尚未出現時為 null。</returns>
    private string? ReadSmokeTrace(Guid taskId)
    {
        var asyncOperations = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(AsyncOperationLogicalName)
        {
            ColumnSet = new ColumnSet("correlationid"),
            Criteria = Equal("regardingobjectid", taskId)
        }).Entities, "smoke 非同步作業");
        string? result = null;
        var inspectedCorrelations = new HashSet<Guid>();
        foreach (var operation in asyncOperations)
        {
            var correlationId = operation.GetAttributeValue<Guid>("correlationid");
            if (correlationId == Guid.Empty || !inspectedCorrelations.Add(correlationId))
            {
                continue;
            }

            var traces = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(TraceLogicalName)
            {
                ColumnSet = new ColumnSet("messageblock"),
                Criteria = And(Equal("correlationid", correlationId),
                    Equal("typename", "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin"))
            }).Entities, "smoke Plugin 追蹤");
            foreach (var trace in traces.Select(row => row.GetAttributeValue<string>("messageblock")))
            {
                if (string.IsNullOrEmpty(trace))
                {
                    continue;
                }

                if (result is not null)
                {
                    throw new InvalidDataException("Smoke diagnostics are ambiguous; the current plugin version cannot be determined.");
                }

                result = trace;
            }
        }

        return result;
    }

    /// <summary>
    /// 將不可信追蹤縮減成唯一完整版本行；以 ordinal 搜尋欄位名稱並拒絕第二次出現，
    /// 禁止前綴、重複行或偽造欄位被忽略。只允許 LF／CRLF 行界，不能 trim 控制字元或秘密後綴。
    /// 值最多 23 字元，Version 解析後須與四段正規輸出逐字相等且每段不超過 UInt16；
    /// 因此符號、空白、前導零、非 ASCII 數字、額外段數及注入內容均無法發布。
    /// </summary>
    /// <param name="trace">只屬本次已關聯診斷執行的追蹤文字，不持有至呼叫之外。</param>
    /// <returns>新建的安全版本行；缺失或任何歧義皆回 null，不回傳原文。</returns>
    private static string? ReadSmokeVersionLine(string trace)
    {
        const string name = "PluginFileVersion";
        const string prefix = name + "=";
        var start = trace.IndexOf(name, StringComparison.Ordinal);
        if (start < 0 ||
            (start > 0 && trace[start - 1] != '\n') ||
            trace.IndexOf(name, start + name.Length, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        var newline = trace.IndexOf('\n', start);
        var end = newline < 0 ? trace.Length : newline;
        if (newline >= 0 && end > start && trace[end - 1] == '\r')
        {
            end--;
        }

        var line = trace.AsSpan(start, end - start);
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var value = line[prefix.Length..];
        if (value.Length is < 7 or > 23 ||
            !Version.TryParse(value, out var version) ||
            version.Revision < 0 ||
            version.Major > ushort.MaxValue || version.Minor > ushort.MaxValue ||
            version.Build > ushort.MaxValue || version.Revision > ushort.MaxValue)
        {
            return null;
        }

        var canonical = version.ToString(4);
        return value.SequenceEqual(canonical.AsSpan()) ? prefix + canonical : null;
    }

    /// <summary>只以固定成功標記判定本次 smoke；不輸出或保存追蹤原文，推播另須具備成功狀態。</summary>
    private static bool IsExpectedSmokeTrace(string trace, string scenario)
    {
        if (trace.Length == 0 ||
            !trace.Contains("P0-PING OK", StringComparison.Ordinal) ||
            !trace.Contains("zh-TW=", StringComparison.Ordinal) ||
            !trace.Contains("qps-ploc=", StringComparison.Ordinal))
        {
            return false;
        }

        return scenario == "p0-ping-trace" ||
            (trace.Contains("Outcome=Accepted", StringComparison.Ordinal) &&
             trace.Contains("Status=200", StringComparison.Ordinal));
    }

    /// <summary>只讀指定組件的型別集合並驗證資料庫 ID；重複或空 ID 必須失敗，不能任選一列。</summary>
    private Entity[] ReadPluginTypes(Guid assemblyId)
    {
        return RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(TypeLogicalName)
        {
            ColumnSet = new ColumnSet("name", "typename", "pluginassemblyid", "friendlyname", "workflowactivitygroupname"),
            Criteria = Equal("pluginassemblyid", assemblyId)
        }).Entities, "Plugin 型別");
    }

    /// <summary>只讀已驗證型別 ID 所屬步驟；結果物化後驗證唯一性，不將 SDK 可變集合發布給外部。</summary>
    private Entity[] ReadSteps(HashSet<Guid> typeIds)
    {
        if (typeIds.Count == 0)
        {
            return Array.Empty<Entity>();
        }

        var steps = typeIds
            .Select(typeId => service.RetrieveMultiple(new QueryExpression(StepLogicalName)
            {
                ColumnSet = new ColumnSet("name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
                    "stage", "mode", "rank", "filteringattributes", "configuration", StepSecureConfigurationLookup,
                    "statecode", "statuscode"),
                Criteria = Equal("eventhandler", typeId)
            }).Entities)
            .SelectMany(rows => rows)
            .ToArray();
        return RequireUniqueRowsById(steps, "Plugin 步驟");
    }

    /// <summary>
    /// 將查詢結果物化並驗證資料庫 ID 非空且唯一；重複列不可依回傳順序任選一筆，
    /// 否則 plan、export 或 smoke 會掩蓋組織資料衝突。
    /// </summary>
    /// <param name="entities">單一消費集合中的 Dataverse 記錄。</param>
    /// <param name="collectionName">由程式呼叫端提供的固定集合標籤，不接受使用者輸入。</param>
    /// <returns>保留原順序且每列 ID 唯一的陣列。</returns>
    /// <exception cref="InvalidDataException">任一列缺少資料庫 ID 或 ID 重複。</exception>
    private static Entity[] RequireUniqueRowsById(IEnumerable<Entity> entities, string collectionName)
    {
        var rows = entities.ToArray();
        var identities = new HashSet<Guid>();
        if (rows.Any(row => row.Id == Guid.Empty || !identities.Add(row.Id)))
        {
            throw new InvalidDataException(collectionName + "集合包含空白或重複資料庫 ID。");
        }

        return rows;
    }

    /// <summary>以精確組件名稱查詢且拒絕多筆或重複 ID；禁止同名衝突被任意排序掩蓋。</summary>
    private Entity? FindAssembly(string name)
    {
        var matches = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(AssemblyLogicalName)
        {
            ColumnSet = new ColumnSet("name", "version", "publickeytoken", "content", "isolationmode", "sourcetype"),
            Criteria = Equal("name", name)
        }).Entities, "Plugin 組件");
        if (matches.Length > 1)
        {
            throw new InvalidDataException("組織內同名 Plugin 組件不唯一。");
        }

        return matches.SingleOrDefault();
    }

    /// <summary>以精確 SDK 訊息查詢唯一中繼資料；不存在或衝突時由呼叫端拒絕後續寫入。</summary>
    private Entity? FindMessage(string messageName)
    {
        var matches = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression("sdkmessage")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = Equal("name", messageName)
        }).Entities, "Plugin 訊息");
        if (matches.Length > 1)
        {
            throw new InvalidDataException("Plugin 訊息 metadata 不唯一。");
        }

        return matches.SingleOrDefault();
    }

    /// <summary>以訊息 ID 與實體名共同限定唯一篩選器，避免將其他實體步驟誤當目前註冊。</summary>
    private Entity? FindMessageFilter(Guid messageId, string entityName)
    {
        var matches = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet("primaryobjecttypecode", "sdkmessageid"),
            Criteria = And(Equal("sdkmessageid", messageId), Equal("primaryobjecttypecode", entityName))
        }).Entities, "Plugin 訊息篩選器");
        if (matches.Length > 1)
        {
            throw new InvalidDataException("Plugin 訊息篩選器 metadata 不唯一。");
        }

        return matches.SingleOrDefault();
    }

    /// <summary>以型別、訊息、實體篩選器、階段、模式與順序共同查詢；衝突列直接失敗，保留資料庫身份。</summary>
    private Entity? FindStep(Guid typeId, Guid messageId, Guid filterId, int stage, int mode, int rank)
    {
        var matches = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(StepLogicalName)
        {
            ColumnSet = new ColumnSet("name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
                "stage", "mode", "rank", "filteringattributes", "configuration", StepSecureConfigurationLookup,
                "statecode", "statuscode"),
            Criteria = And(
                Equal("eventhandler", typeId),
                Equal("sdkmessageid", messageId),
                Equal("sdkmessagefilterid", filterId),
                Equal("stage", stage),
                Equal("mode", mode),
                Equal("rank", rank))
        }).Entities, "Plugin 步驟");
        if (matches.Length > 1)
        {
            throw new InvalidDataException("Plugin 步驟身份重複。");
        }

        return matches.SingleOrDefault();
    }

    /// <summary>還原只依精確資料庫 ID 讀取，不按名稱尋找替代列，避免回復到其他步驟。</summary>
    private Entity? FindStepById(Guid id)
    {
        return ReadById(StepLogicalName, id);
    }

    /// <summary>缺少有效資料庫 ID 時不送 SDK 查詢；存在時取得本次操作的獨立實體資料。</summary>
    private Entity? ReadById(string entityName, Guid? id)
    {
        if (!id.HasValue || id.Value == Guid.Empty)
        {
            return null;
        }

        return service.Retrieve(entityName, id.Value, new ColumnSet(true));
    }

    /// <summary>只把受管理組件及步驟的穩定排序摘要納入指紋；設定使用雜湊，原始秘密不落檔。</summary>
    private string CalculateFingerprint(string assemblyName)
    {
        var assembly = FindAssembly(assemblyName);
        var rows = new List<object>();
        if (assembly is not null)
        {
            rows.Add(new
            {
                Kind = "assembly",
                assembly.Id,
                Name = assembly.GetAttributeValue<string>("name"),
                Version = assembly.GetAttributeValue<string>("version"),
                PublicKeyToken = assembly.GetAttributeValue<string>("publickeytoken"),
                Content = HashBase64(assembly.GetAttributeValue<string>("content") ?? string.Empty),
                IsolationMode = ReadOption(assembly, "isolationmode"),
                SourceType = ReadOption(assembly, "sourcetype")
            });

            var types = ReadPluginTypes(assembly.Id);
            foreach (var type in types.OrderBy(row => row.Id))
            {
                rows.Add(new
                {
                    Kind = "type",
                    type.Id,
                    TypeName = type.GetAttributeValue<string>("typename"),
                    Name = type.GetAttributeValue<string>("name"),
                    FriendlyName = type.GetAttributeValue<string>("friendlyname"),
                    ActivityGroup = type.GetAttributeValue<string>("workflowactivitygroupname")
                });
            }

            foreach (var step in ReadSteps(types.Select(row => row.Id).ToHashSet()).OrderBy(row => row.Id))
            {
                rows.Add(new
                {
                    Kind = "step",
                    step.Id,
                    TypeId = ReadReferenceId(step, "eventhandler"),
                    MessageId = ReadReferenceId(step, "sdkmessageid"),
                    FilterId = ReadReferenceId(step, "sdkmessagefilterid"),
                    Stage = ReadOption(step, "stage"),
                    Mode = ReadOption(step, "mode"),
                    Rank = step.GetAttributeValue<int>("rank"),
                    FilteringAttributes = step.GetAttributeValue<string>("filteringattributes"),
                    ConfigurationHash = HashText(step.GetAttributeValue<string>("configuration") ?? string.Empty),
                    HasSecureConfig = ReadReferenceId(step, StepSecureConfigurationLookup).HasValue,
                    State = ReadOption(step, "statecode"),
                    Status = ReadOption(step, "statuscode")
                });
                foreach (var image in ReadImages(step.Id).OrderBy(row => row.Id)) rows.Add(ToImageExport(image));
            }
        }

        return HashText(JsonSerializer.Serialize(rows));
    }

    /// <summary>固定清單與字典排序，使同一有效清單產生相同雜湊；內容仍為佔位範本，從不解析秘密。</summary>
    private static string CanonicalManifest(RegistrationManifest manifest)
    {
        var normalized = new
        {
            manifest.SchemaVersion,
            manifest.AssemblyName,
            manifest.ActivityGroup,
            Activities = manifest.Activities.OrderBy(activity => activity.Type, StringComparer.Ordinal),
            Steps = manifest.Steps
                .OrderBy(step => StepKey(step), StringComparer.Ordinal)
                .Select(step => new
                {
                    step.Module,
                    step.Legacy,
                    step.ShadowReport,
                    step.Type,
                    step.Message,
                    step.Entity,
                    step.Stage,
                    step.Mode,
                    step.Rank,
                    FilteringAttributes = step.FilteringAttributes.OrderBy(value => value, StringComparer.Ordinal),
                    Images = step.Images.OrderBy(value => value.Name, StringComparer.Ordinal),
                    step.UnsecureConfig,
                    step.SecureConfig,
                    EnabledByOrganization = step.EnabledByOrganization.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                })
                .ToArray()
        };
        return JsonSerializer.Serialize(normalized);
    }

    /// <summary>
    /// 計畫完整性排除雜湊欄位本身，其餘核准欄位包含非空 OrganizationId 全部納入；
    /// null 由契約的省略屬性保留 legacy JSON bytes，不重新排序或更換原序列化設定。
    /// </summary>
    private static string HashPlan(DeploymentPlan plan)
    {
        var unsigned = plan with { Sha256 = string.Empty };
        return HashText(JsonSerializer.Serialize(unsigned));
    }

    /// <summary>套用前驗證支援的 schema 與完整計畫雜湊，任何不符均在寫入前拒絕。</summary>
    private static void ValidatePlanHash(DeploymentPlan plan)
    {
        if (plan.SchemaVersion != 1 ||
            !string.Equals(plan.Sha256, HashPlan(plan), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plan 雜湊或格式版本不符。");
        }
    }

    /// <summary>驗證第二版完整清單與 PE 型別集合；直接呼叫核心亦不能繞過 loader 或漏列公開入口。</summary>
    private static void ValidateManifest(RegistrationManifest manifest, AssemblyImage image)
    {
        RegistrationManifestLoader.Validate(manifest);
        if (manifest.AssemblyName != image.Name ||
            !manifest.Steps.Select(step => step.Type).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(image.PluginTypes.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal) ||
            !manifest.Activities.Select(activity => activity.Type).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(image.ActivityTypes.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Manifest and assembly type identities do not match.");
    }

    /// <summary>
    /// 只允許固定模式、語系、member 來源教會與秘密佔位；拒絕原始收件者及 token，
    /// 使 plan 和版控安全。member 的 sourceChurchId 是部署者核准的固定正式 ID，
    /// 不是由 task、Contact 或其他執行輸入推導的授權。
    /// </summary>
    internal static void ValidateConfigurationTemplate(StepRegistration step)
    {
        if (string.IsNullOrWhiteSpace(step.UnsecureConfig) ||
            step.UnsecureConfig.Length > 4096 ||
            step.SecureConfig.Length > 4096)
        {
            throw new InvalidDataException("Plugin 設定空白或超過允許長度。");
        }

        using var configuration = JsonDocument.Parse(step.UnsecureConfig);
        var root = configuration.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("mode", out var mode) ||
            !root.TryGetProperty("culture", out var culture) ||
            mode.ValueKind != JsonValueKind.String ||
            culture.ValueKind != JsonValueKind.String ||
            mode.GetString() is not ("trace" or "push" or "member") ||
            culture.GetString() is not ("zh-TW" or "qps-ploc"))
        {
            throw new InvalidDataException("Plugin 設定結構不符合 P0 契約。");
        }

        var memberMode = string.Equals(mode.GetString(), "member", StringComparison.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            var valid = property.Name switch
            {
                "mode" or "culture" or "lineUserId" or "sourceChurchId" =>
                    property.Value.ValueKind == JsonValueKind.String,
                "allowPseudoLocale" =>
                    property.Value.ValueKind == JsonValueKind.True ||
                    property.Value.ValueKind == JsonValueKind.False,
                _ => false
            };
            if (!valid)
            {
                throw new InvalidDataException("Plugin 設定含不支援欄位。");
            }
        }

        var pushMode = string.Equals(mode.GetString(), "push", StringComparison.Ordinal);
        if (memberMode)
        {
            if (!root.TryGetProperty("sourceChurchId", out var sourceChurchId) ||
                !Guid.TryParseExact(sourceChurchId.GetString(), "D", out var parsedChurchId) ||
                parsedChurchId == Guid.Empty ||
                !root.TryGetProperty("allowPseudoLocale", out _ ) ||
                !IsSingleSecretPlaceholder(step.SecureConfig) ||
                !string.Equals(step.SecureConfig, "${secret:TokenKey}", StringComparison.Ordinal) ||
                root.TryGetProperty("lineUserId", out _))
            {
                throw new InvalidDataException("Member configuration requires a valid source church, a boolean pseudo-locale switch and the TokenKey placeholder.");
            }
        }
        else if (pushMode)
        {
            if (!root.TryGetProperty("lineUserId", out var lineUserId) ||
                !IsSingleSecretPlaceholder(lineUserId.GetString()) ||
                !IsSingleSecretPlaceholder(step.SecureConfig))
            {
                throw new InvalidDataException("推播設定必須只使用 User Secrets 佔位。");
            }
        }
        else if (root.TryGetProperty("lineUserId", out _) ||
                 root.TryGetProperty("sourceChurchId", out _) ||
                 root.TryGetProperty("allowPseudoLocale", out _) ||
                 step.SecureConfig.Length != 0)
        {
            throw new InvalidDataException("追蹤模式不得包含推播識別或 token。");
        }

        _ = ExtractSecretNames(step.UnsecureConfig);
        _ = ExtractSecretNames(step.SecureConfig);
    }

    /// <summary>要求完整字串恰為一個受限秘密佔位，禁止額外前後綴造成未驗證內容。</summary>
    private static bool IsSingleSecretPlaceholder(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var match = SecretPlaceholderRegex.Match(value);
        return match.Success && match.Length == value.Length;
    }

    /// <summary>只抽取受限名稱供人工審核，不接觸秘密值；格式異常時以固定錯誤拒絕。</summary>
    private static string[] ExtractSecretNames(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<string>();
        }

        var matches = SecretPlaceholderRegex.Matches(value);
        if (value.Contains("${secret:", StringComparison.Ordinal) &&
            matches.Cast<Match>().Sum(match => match.Length) == 0)
        {
            throw new InvalidDataException("秘密佔位格式無效。");
        }

        return matches.Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 僅供 apply 建立或更新步驟時解析安全設定；member 建構式要求單欄 tokenKey JSON，
    /// 不能直接寫入原始金鑰。以 JSON API 跳脫值，保留 trace 空值及 push 原始 token 契約。
    /// 精確清單已驗證的 platform 模組只接受空 secure，故直接返回空字串，不讀取不存在的 mode；
    /// 非空仍拒絕，這條分流不替代上層對組件、兩個入口、三步驟與產品映射的完整驗證。
    /// 結果字串只借給本次 SDK 寫入，不快取或加入 plan／export／日誌；CLR 字串無法保證清零，
    /// 因此呼叫端不得延長其範圍。缺少秘密時在建立 secure 列之前失敗。
    /// </summary>
    /// <param name="registration">已通過格式、模式與秘密佔位驗證的註冊項目。</param>
    /// <returns>只供本次寫入使用的安全設定，不包含跨組織或跨命令狀態。</returns>
    private string ResolveSecureConfiguration(StepRegistration registration)
    {
        if (registration.Module == "platform")
        {
            if (registration.SecureConfig.Length != 0)
                throw new InvalidDataException("Platform registration must not contain secure configuration.");
            return string.Empty;
        }
        var value = ResolvePlaceholders(registration.SecureConfig);
        using var configuration = JsonDocument.Parse(registration.UnsecureConfig);
        return string.Equals(configuration.RootElement.GetProperty("mode").GetString(), "member", StringComparison.Ordinal)
            ? new JsonObject { ["tokenKey"] = value }.ToJsonString()
            : value;
    }

    /// <summary>僅由 apply 寫入框架呼叫；解析後值不快取、不進計畫或日誌，缺值則停止寫入。</summary>
    private string ResolvePlaceholders(string template)
    {
        return SecretPlaceholderRegex.Replace(template, match =>
        {
            var value = secret(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("缺少必要的部署 User Secrets 設定。");
            }

            return value;
        });
    }

    /// <summary>以完整穩定鍵找唯一受管理清單項目；不存在時不容許計畫注入額外操作。</summary>
    private static StepRegistration FindStepRegistration(RegistrationManifest manifest, string stepKey)
    {
        var step = manifest.Steps.SingleOrDefault(candidate =>
            string.Equals(StepKey(candidate), stepKey, StringComparison.Ordinal));
        return step ?? throw new InvalidDataException("Plan 指定步驟不在註冊清單中。");
    }

    /// <summary>組合型別與註冊語意成穩定鍵，不使用顯示名稱或清單順序當作身份。</summary>
    private static string StepKey(StepRegistration step) =>
        string.Join("|", step.Type, step.Message, step.Entity, step.Stage, step.Mode,
            step.Rank.ToString(CultureInfo.InvariantCulture));

    /// <summary>只從固定清單產生可辨識的顯示標籤；此標籤不作資料庫身份或授權依據。</summary>
    private static string StepDisplayName(StepRegistration step) =>
        "SpeechMessage " + step.Entity + " " + step.Message + " " + step.Type[(step.Type.LastIndexOf('.') + 1)..];

    /// <summary>只轉換公開管線的三種核定階段；未知值必須明確拒絕。</summary>
    private static int ReadStage(string stage) => stage switch
    {
        "PreValidation" => 10,
        "PreOperation" => 20,
        "PostOperation" => 40,
        _ => throw new InvalidDataException("Plugin registration stage is invalid.")
    };

    /// <summary>只轉換同步及非同步核定模式；禁止默認為其他執行模式。</summary>
    private static int ReadMode(string mode) => mode switch
    {
        "Synchronous" => 0,
        "Asynchronous" => 1,
        _ => throw new InvalidDataException("Plugin execution mode is invalid.")
    };

    /// <summary>只有該組織明確 true 才啟用；缺少設定一律停用以避免意外推播。</summary>
    private static bool IsEnabled(StepRegistration step, string organization) =>
        step.EnabledByOrganization.TryGetValue(organization, out var enabled) && enabled;

    /// <summary>
    /// 決定目前組織是否可在既有穩定身份不存在時新增 Plugin step。goodshepherd20、
    /// construction、association 及其他非 legacy 組織必須有 enabledByOrganization 的明確 true；
    /// 缺鍵或 false 都不產生 CreateStep／ReconcileImages，避免將其他組織的配置帶入新組織。
    /// goodshepherdback 保留既有 P0 相容語意：明確 false 不新增，缺鍵仍以停用狀態建立，
    /// 讓舊版「清單省略組織旗標即建立停用步驟」行為不因本次多組織擴充而改變。
    /// </summary>
    private static bool ShouldCreateStep(StepRegistration step, string organization)
    {
        if (string.Equals(organization, "goodshepherdback", StringComparison.Ordinal))
        {
            return !step.EnabledByOrganization.TryGetValue(organization, out var enabled) || enabled;
        }

        return IsEnabled(step, organization);
    }

    /// <summary>所有核心入口再驗本命令白名單；不以呼叫端字串擴權，正式操作確認仍由CLI負責。</summary>
    private void ValidateOrganization(string organization) => _ = catalog.Get(organization);

    /// <summary>
    /// 將本次單組件候選交共用清單關卡完整驗證，並在任何 SDK 查詢或寫入前核對組件產品範圍。
    /// 平台僅限三核定組織，construction／association 拒絕教會舊組件；其他既有 catalog 名稱
    /// 沿用共用映射的正式相容性。只建立本次同步呼叫的容器，不保存 SDK、秘密或跨命令狀態。
    /// </summary>
    /// <param name="manifest">本次完整註冊候選，不可透過過濾移除不適用組件來假稱驗證成功。</param>
    /// <param name="organization">已由核心 catalog 驗證的 canonical 組織名稱。</param>
    /// <exception cref="InvalidDataException">候選格式或組件產品邊界不符合共用關卡。</exception>
    private static void ValidatePlatformOrganization(RegistrationManifest manifest, string organization)
    {
        RegistrationManifestLoader.ValidateForOrganization(
            new RegistrationDocument { Assemblies = new[] { manifest } }, organization);
    }

    /// <summary>明列既有兩組件及 F3 平台組件，避免其他輸入以共通 package 名稱進入 export。</summary>
    private static bool IsSupportedManagedAssemblyName(string assemblyName) =>
        assemblyName is "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" or PlatformAssemblyName;

    /// <summary>舊模組切換／影子入口只允許catalog的test種類；一般部署的白名單檢查不能擴張此權限。</summary>
    internal void ValidateTestOrganization(string organization)
    {
        if (catalog.Get(organization).Kind != "test") throw new InvalidDataException("This operation requires a test organization.");
    }

    /// <summary>同公鑰及主次版本且為 Sandbox／Database 才容許更新；避免同名不同身份被覆寫。</summary>
    private static void ValidateAssemblyCompatibility(Entity assembly, AssemblyImage image)
    {
        var existingVersion = assembly.GetAttributeValue<string>("version");
        if (!Version.TryParse(existingVersion, out var oldVersion) ||
            !Version.TryParse(image.Version, out var newVersion) ||
            oldVersion.Major != newVersion.Major ||
            oldVersion.Minor != newVersion.Minor ||
            !string.Equals(assembly.GetAttributeValue<string>("publickeytoken"), image.PublicKeyToken,
                StringComparison.OrdinalIgnoreCase) ||
            ReadOption(assembly, "isolationmode") != 2 ||
            ReadOption(assembly, "sourcetype") != 0)
        {
            throw new InvalidDataException("既有 Plugin 組件版本、公鑰或部署模式不相容。");
        }
    }

    /// <summary>只以 Dataverse 已讀取的 statecode 判定啟用，不從顯示文字推測。</summary>
    private static bool IsEnabledFromState(Entity entity) => ReadOption(entity, "statecode") == 0;

    private static int ReadOption(Entity entity, string attributeName) =>
        entity.GetAttributeValue<OptionSetValue>(attributeName)?.Value ?? -1;

    /// <summary>只接受非空 SDK 參照 ID，避免空 GUID 進入跨列查詢或資料庫操作。</summary>
    private static Guid? ReadReferenceId(Entity entity, string attributeName)
    {
        var reference = entity.GetAttributeValue<EntityReference>(attributeName);
        return reference is null || reference.Id == Guid.Empty ? null : reference.Id;
    }

    /// <summary>建立單一精確等值的 SDK 查詢條件，不拼接 FetchXML 或使用者字串。</summary>
    private static FilterExpression Equal(string attributeName, object value)
    {
        var filter = new FilterExpression(LogicalOperator.And);
        filter.AddCondition(attributeName, ConditionOperator.Equal, value);
        return filter;
    }

    /// <summary>以 AND 合併固定身份條件，保持所有隔離條件同時成立。</summary>
    private static FilterExpression And(params FilterExpression[] conditions)
    {
        var filter = new FilterExpression(LogicalOperator.And);
        foreach (var condition in conditions)
        {
            foreach (var item in condition.Conditions)
            {
                filter.AddCondition(item);
            }
        }

        return filter;
    }

    /// <summary>以解碼後 DLL bytes 計算內容雜湊；無效 Base64 使用固定錯誤，不展開原文。</summary>
    private static string HashBase64(string content)
    {
        if (content.Length == 0)
        {
            return HashText(string.Empty);
        }

        try
        {
            return Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(content)));
        }
        catch (FormatException)
        {
            throw new InvalidDataException("組織中的 Plugin 組件內容不是有效 Base64。");
        }
    }

    /// <summary>以固定 UTF-8 與 SHA-256 建立可跨程序重現的摘要，不依目前文化格式化。</summary>
    private static string HashText(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// 同目錄候選完整寫入、flush 並關閉後，才以單次覆寫搬移原子發布；等待中保留舊完整快照。
    /// Windows 外部讀取 handle 即使沒有分享 Delete，也可能回報 UnauthorizedAccessException，
    /// 因此只對本機 Windows 的精確 access denied／sharing／lock violation HRESULT 有界等待。
    /// 首次受阻才啟動單調一秒期限，每次最多同步等待二十五毫秒，不重設期限、不新增背景工作；
    /// 正常路徑不等待。永久權限／唯讀／持續占用仍失敗，到期以外層 throw 保留首例外及堆疊。
    /// 本呼叫唯一擁有 writer、stream 及暫存路徑，finally 清除未發布候選；不放寬屬性或 ACL，
    /// 不先刪目的檔，也不把候選、SDK、身分或秘密保留至另一命令。最終失敗仍交呼叫端處理。
    /// </summary>
    /// <typeparam name="T">只屬本次呼叫、已由上層驗證可安全匯出的純值候選型別。</typeparam>
    /// <param name="path">部署者核定的本機輸出位置，不作為組織或授權依據。</param>
    /// <param name="value">本次完整匯出候選，重試只搬移已關閉的同一份檔案。</param>
    /// <exception cref="IOException">不可恢復的 I/O 故障或 Windows 共享／鎖定等待到期。</exception>
    /// <exception cref="UnauthorizedAccessException">持續拒絕或非 Windows 權限錯誤，保留原始例外。</exception>
    private static void WriteJsonAtomically<T>(string path, T value)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("輸出位置沒有父目錄。");
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.Combine(parent, "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
            {
                writer.Write(JsonSerializer.Serialize(value, JsonOptions));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            catch (Exception error) when (IsWindowsMoveContention(error))
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var budget = TimeSpan.FromSeconds(1);
                var poll = TimeSpan.FromMilliseconds(25);
                while (true)
                {
                    var remaining = budget - System.Diagnostics.Stopwatch.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero) throw;
                    Thread.Sleep(remaining < poll ? remaining : poll);
                    // 排程延遲不能延長期限，避免到期後才補一次搬移而誤報成功。
                    if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= budget) throw;
                    try
                    {
                        File.Move(temporaryPath, fullPath, overwrite: true);
                        return;
                    }
                    catch (Exception retry) when (IsWindowsMoveContention(retry))
                    {
                        // 繼續同一固定期限；外層 catch 保有第一次真例外，不包裝或改成最後一次故障。
                    }
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        // 完整 HRESULT 與實際 OS 同時核對；其他 I/O、路徑或序列化錯誤直接向外傳遞。
        static bool IsWindowsMoveContention(Exception error) => OperatingSystem.IsWindows() &&
            error is IOException or UnauthorizedAccessException &&
            error.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021);
    }

    private sealed record Inspection(string Fingerprint, List<DeploymentOperation> Operations);
}

/// <summary>安全 JSON export schema；所有設定內容以 presence bit 或雜湊表達，不保存 userId/token。</summary>
internal sealed record DeploymentExport(
    int SchemaVersion,
    string Organization,
    DateTimeOffset ExportedUtc,
    string Fingerprint,
    PluginAssemblyExport? Assembly,
    IReadOnlyList<PluginTypeExport> Types,
    IReadOnlyList<PluginStepExport> Steps,
    string Sha256)
{
    /// <summary>匯出所屬組件，即使原本不存在仍保存核定範圍，防止跨組件回復。</summary>
    public string ManagedAssemblyName { get; init; } = "SpeechMessage.Dynamics.Plugins";
    /// <summary>只含具名映像註冊 metadata，不含實體實際欄位值。</summary>
    public IReadOnlyList<PluginImageExport> Images { get; init; } = Array.Empty<PluginImageExport>();
}

/// <summary>可還原組件身份及 DLL 內容的安全快照；組件位元組不含執行期 credential。</summary>
internal sealed record PluginAssemblyExport(
    Guid Id,
    string Name,
    string Version,
    string PublicKeyToken,
    string Content,
    int IsolationMode,
    int SourceType);

/// <summary>Plugin 型別的穩定資料庫身份快照。</summary>
internal sealed record PluginTypeExport(Guid Id, string Name, string TypeName)
{
    /// <summary>活動顯示名稱，屬純註冊 metadata。</summary>
    public string FriendlyName { get; init; } = "";
    /// <summary>活動群組；非活動可為空，不含 workflow 定義或執行資料。</summary>
    public string ActivityGroup { get; init; } = "";
}

/// <summary>映像的安全註冊快照，只有 metadata，不持有 SDK 或業務記錄。</summary>
/// <param name="Id">正式映像 ID。</param><param name="StepId">父步驟 ID。</param><param name="Name">穩定名稱。</param>
/// <param name="Alias">Plugin 映像別名。</param><param name="Type">前後映像選項。</param>
/// <param name="Attributes">明列欄位名稱。</param><param name="MessageProperty">SDK 訊息屬性名稱。</param>
internal sealed record PluginImageExport(Guid Id, Guid StepId, string Name, string Alias, int Type, string Attributes, string MessageProperty);

/// <summary>
/// Plugin 步驟的非敏感快照。輸出記錄只存設定是否存在，不存 configuration 或 secureconfig 原文，
/// 因為前者可含 lineUserId、後者可含 token。
/// </summary>
internal sealed record PluginStepExport(
    Guid Id,
    string TypeName,
    string Message,
    string Entity,
    int Stage,
    int Mode,
    int Rank,
    bool Enabled,
    bool HasSecureConfig,
    bool HasUnsecureConfig);
