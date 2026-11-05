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
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 單一 Dataverse 服務租約上的佈署操作核心。物件不跨命令共用；它不保存 user／tenant 狀態、
/// 已解析的秘密或外部 client。計畫與匯出只包含非敏感資料，所有檔案 writer 都由呼叫期間擁有。
/// </summary>
internal sealed class DeploymentEngine
{
    private const string AllowedOrganization = "goodshepherdback";
    private const string AssemblyLogicalName = "pluginassembly";
    private const string TypeLogicalName = "plugintype";
    private const string StepLogicalName = "sdkmessageprocessingstep";
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
    /// <exception cref="ArgumentNullException">必要依賴為 null。</exception>
    /// <exception cref="ArgumentException">輸出目錄為空白。</exception>
    public DeploymentEngine(IOrganizationService service, string directory, TimeProvider clock, Func<string, string> secret)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.secret = secret ?? throw new ArgumentNullException(nameof(secret));
    }

    /// <summary>
    /// 將清單、組件 PE 與組織中的受管理 metadata 作離線式比對，建立沒有憑證值的 plan。
    /// 此方法只讀取組件、類型、訊息、篩選器與步驟，不讀取業務記錄，也不解析秘密。
    /// </summary>
    /// <param name="manifest">經過格式與佔位驗證的註冊清單。</param>
    /// <param name="image">已以 MetadataReader 驗證的 Plugin 組件內容。</param>
    /// <param name="organization">目前唯一允許的開發測試組織。</param>
    /// <returns>雜湊固定且僅含非敏感資訊的人工審核計畫。</returns>
    /// <exception cref="ArgumentNullException">清單或組件為 null。</exception>
    /// <exception cref="InvalidDataException">清單不符合 P0 範圍或既有組件身份衝突。</exception>
    public DeploymentPlan Plan(RegistrationManifest manifest, AssemblyImage image, string organization)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(image);
        ValidateOrganization(organization);
        ValidateManifest(manifest, image);

        var inspection = Inspect(manifest, image);
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
            SecretPlaceholders = Array.AsReadOnly(placeholders)
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
    /// 驗證 plan hash、來源清單、DLL 與即時組織指紋後，先完成安全 export，再依核准操作寫入。
    /// 寫入前任何檢查失敗都不呼叫 Dataverse 的 Create／Update。
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
        ValidatePlanHash(plan);

        if (!string.Equals(plan.ManifestHash, HashText(CanonicalManifest(manifest)), StringComparison.Ordinal) ||
            !string.Equals(plan.AssemblyHash, image.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plan 的來源清單或組件已變更。");
        }

        var inspection = Inspect(manifest, image);
        if (!string.Equals(plan.Fingerprint, inspection.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("組織現況已改變，必須重新產生 plan。");
        }

        var exportPath = Path.Combine(directory, "export.json");
        WriteExport(plan.Organization, exportPath);

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
                case "CreateStep":
                    CreateStep(operation.Key, manifest);
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
    /// </summary>
    /// <param name="organization">唯一核准的測試組織名稱。</param>
    /// <param name="path">輸出 JSON 檔案路徑。</param>
    /// <returns>完成原子寫入後的絕對路徑。</returns>
    public string WriteExport(string organization, string path)
    {
        ValidateOrganization(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var export = CaptureExport(organization);
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
        var export = ReadExport(path);
        ValidateExport(export, organization);

        var currentAssembly = FindAssembly(manifest.AssemblyName);
        if (export.Assembly is null)
        {
            if (currentAssembly is not null)
            {
                DeleteManagedRows(currentAssembly.Id);
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
            service.Update(ToAssemblyEntity(export.Assembly, currentAssembly.Id));
        }

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
    /// 建立本次專用 task，有限等待對應 asyncoperation 與 Plugin trace；成功、斷言失敗、
    /// 逾時或 caller cancellation 都會在 finally 刪除該 task。只查詢此 task 關聯的記錄。
    /// 首次立即查詢，後續每兩秒重試，總期限維持三分鐘；避免對與正式組織共用的地端主機施加高頻輪詢。
    /// timeout／linked token 及延遲 timer 都由此操作擁有並確定性釋放，不保留連線或跨命令狀態。
    /// </summary>
    /// <param name="scenario">P0 核准的 trace 或 push 情境。</param>
    /// <param name="ct">呼叫端取消 token。</param>
    /// <returns>追蹤內容符合固定情境時為 true；未到達或不符合時為 false。</returns>
    /// <exception cref="OperationCanceledException">呼叫端主動取消等待。</exception>
    public async Task<bool> SmokeAsync(string scenario, CancellationToken ct)
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
                    return IsExpectedSmokeTrace(trace, scenario);
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
    private Inspection Inspect(RegistrationManifest manifest, AssemblyImage image)
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

            var pluginType = pluginTypes.SingleOrDefault(type =>
                string.Equals(type.GetAttributeValue<string>("typename"), step.Type, StringComparison.Ordinal));
            if (pluginType is null)
            {
                operations.Add(new DeploymentOperation("CreateType", step.Type));
                operations.Add(new DeploymentOperation("CreateStep", StepKey(step)));
                continue;
            }

            var stage = ReadStage(step.Stage);
            var mode = ReadMode(step.Mode);
            var existingStep = FindStep(pluginType.Id, message.Id, filter.Id, stage, mode, step.Rank);
            if (existingStep is null)
            {
                operations.Add(new DeploymentOperation("CreateStep", StepKey(step)));
                continue;
            }

            if (!NonSecretConfigurationMatches(existingStep, step))
            {
                operations.Add(new DeploymentOperation("UpdateStepConfiguration", StepKey(step)));
            }

            var shouldEnable = IsEnabled(step, AllowedOrganization);
            if ((ReadOption(existingStep, "statecode") == 0) != shouldEnable)
            {
                operations.Add(new DeploymentOperation("UpdateStepState", StepKey(step)));
            }
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

    /// <summary>以服務生成的穩定 ID 建立步驟；秘密只在寫入框架內解析，secure 建立後若步驟失敗則確定清理。</summary>
    private void CreateStep(string stepKey, RegistrationManifest manifest)
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
        var secureConfiguration = ResolvePlaceholders(registration.SecureConfig);
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
            var enabled = IsEnabled(registration, AllowedOrganization);
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
                ["statecode"] = new OptionSetValue(enabled ? 0 : 1),
                ["statuscode"] = new OptionSetValue(enabled ? 1 : 2)
            };
            if (secureConfigurationReference is not null)
            {
                step[StepSecureConfigurationLookup] = secureConfigurationReference;
            }

            service.Create(step);
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
                ["secureconfig"] = ResolvePlaceholders(registration.SecureConfig)
            });
        }

        try
        {
            service.Update(new Entity(StepLogicalName, step.Id)
            {
                ["configuration"] = configuration.ToJsonString(),
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
    private DeploymentExport CaptureExport(string organization)
    {
        var assembly = FindAssembly("SpeechMessage.Dynamics.Plugins");
        if (assembly is null)
        {
            return SealExport(new DeploymentExport(1, organization, clock.GetUtcNow().ToUniversalTime(),
                CalculateFingerprint("SpeechMessage.Dynamics.Plugins"), null,
                Array.Empty<PluginTypeExport>(), Array.Empty<PluginStepExport>(), string.Empty));
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
                type.GetAttributeValue<string>("typename") ?? string.Empty))
            .OrderBy(type => type.TypeName, StringComparer.Ordinal)
            .ToArray();

        return SealExport(new DeploymentExport(1, organization, clock.GetUtcNow().ToUniversalTime(),
            CalculateFingerprint("SpeechMessage.Dynamics.Plugins"), assemblyExport,
            Array.AsReadOnly(types), Array.AsReadOnly(steps), string.Empty));
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
        if (export.SchemaVersion != 1 ||
            !string.Equals(export.Organization, organization, StringComparison.Ordinal) ||
            !string.Equals(export.Sha256, HashText(JsonSerializer.Serialize(export with { Sha256 = string.Empty })), StringComparison.Ordinal))
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
    private void DeleteManagedRows(Guid assemblyId)
    {
        var types = ReadPluginTypes(assemblyId);
        foreach (var type in types)
        {
            foreach (var step in ReadSteps(new HashSet<Guid> { type.Id }))
            {
                service.Delete(StepLogicalName, step.Id);
            }
        }

        foreach (var type in types)
        {
            service.Delete(TypeLogicalName, type.Id);
        }

        var assembly = FindAssembly("SpeechMessage.Dynamics.Plugins");
        if (assembly is not null && assembly.Id == assemblyId)
        {
            service.Delete(AssemblyLogicalName, assemblyId);
        }
    }

    /// <summary>
    /// 只依 smoke task 找到的 async operation correlation id 查 Plugin trace。
    /// 尚無對應記錄時回傳 null，讓外層依有限輪詢期限繼續等待。
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
        foreach (var operation in asyncOperations)
        {
            var correlationId = operation.GetAttributeValue<Guid>("correlationid");
            var traces = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(TraceLogicalName)
            {
                ColumnSet = new ColumnSet("messageblock"),
                Criteria = Equal("correlationid", correlationId)
            }).Entities, "smoke Plugin 追蹤");
            var trace = traces
                .Select(row => row.GetAttributeValue<string>("messageblock"))
                .FirstOrDefault(message => !string.IsNullOrEmpty(message));
            if (trace is not null)
            {
                return trace;
            }
        }

        return null;
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
            ColumnSet = new ColumnSet("name", "typename", "pluginassemblyid"),
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
                    Name = type.GetAttributeValue<string>("name")
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
            Steps = manifest.Steps
                .OrderBy(step => StepKey(step), StringComparer.Ordinal)
                .Select(step => new
                {
                    step.Type,
                    step.Message,
                    step.Entity,
                    step.Stage,
                    step.Mode,
                    step.Rank,
                    FilteringAttributes = step.FilteringAttributes.OrderBy(value => value, StringComparer.Ordinal),
                    Images = step.Images.OrderBy(value => value, StringComparer.Ordinal),
                    step.UnsecureConfig,
                    step.SecureConfig,
                    EnabledByOrganization = step.EnabledByOrganization.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                })
                .ToArray()
        };
        return JsonSerializer.Serialize(normalized);
    }

    /// <summary>計算計畫完整性時排除雜湊欄位本身，其餘核准欄位全部納入。</summary>
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

    /// <summary>驗證 P0 唯一支援的範圍及穩定身份唯一性；不支援欄位不可靜默忽略。</summary>
    private static void ValidateManifest(RegistrationManifest manifest, AssemblyImage image)
    {
        if (manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.AssemblyName, image.Name, StringComparison.Ordinal) ||
            manifest.Steps.Count == 0)
        {
            throw new InvalidDataException("註冊清單格式或組件身份不符。");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in manifest.Steps)
        {
            if (step.Rank <= 0 ||
                step.Message != "Create" ||
                step.Entity != "task" ||
                step.Stage != "PostOperation" ||
                step.Mode != "Asynchronous" ||
                step.FilteringAttributes.Count != 0 ||
                step.Images.Count != 0 ||
                !image.PluginTypes.Contains(step.Type, StringComparer.Ordinal) ||
                !keys.Add(StepKey(step)))
            {
                throw new InvalidDataException("註冊清單包含 P0 範圍外或重複的步驟。");
            }

            ValidateConfigurationTemplate(step);
        }
    }

    /// <summary>只允許固定模式、語系與秘密佔位；拒絕原始收件者及 token，使 plan 和版控安全。</summary>
    private static void ValidateConfigurationTemplate(StepRegistration step)
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
            mode.GetString() is not ("trace" or "push") ||
            culture.GetString() is not ("zh-TW" or "qps-ploc"))
        {
            throw new InvalidDataException("Plugin 設定結構不符合 P0 契約。");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("mode" or "culture" or "lineUserId") ||
                property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Plugin 設定含不支援欄位。");
            }
        }

        var pushMode = string.Equals(mode.GetString(), "push", StringComparison.Ordinal);
        if (pushMode)
        {
            if (!root.TryGetProperty("lineUserId", out var lineUserId) ||
                !IsSingleSecretPlaceholder(lineUserId.GetString()) ||
                !IsSingleSecretPlaceholder(step.SecureConfig))
            {
                throw new InvalidDataException("推播設定必須只使用 User Secrets 佔位。");
            }
        }
        else if (root.TryGetProperty("lineUserId", out _) || step.SecureConfig.Length != 0)
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

    /// <summary>只轉換 P0 已核定的 PostOperation；未知值必須明確拒絕。</summary>
    private static int ReadStage(string stage) => stage switch
    {
        "PostOperation" => 40,
        _ => throw new InvalidDataException("Plugin 註冊階段不在 P0 白名單中。")
    };

    /// <summary>只轉換 P0 已核定的非同步模式；禁止默認為其他執行模式。</summary>
    private static int ReadMode(string mode) => mode switch
    {
        "Asynchronous" => 1,
        _ => throw new InvalidDataException("Plugin 執行模式不在 P0 白名單中。")
    };

    /// <summary>只有該組織明確 true 才啟用；缺少設定一律停用以避免意外推播。</summary>
    private static bool IsEnabled(StepRegistration step, string organization) =>
        step.EnabledByOrganization.TryGetValue(organization, out var enabled) && enabled;

    /// <summary>所有核心入口再驗唯一開發組織，防止繞過 CLI 後跨組織使用。</summary>
    private static void ValidateOrganization(string organization)
    {
        if (!string.Equals(organization, AllowedOrganization, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("目標組織未列入 P0 唯一白名單。");
        }
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

    /// <summary>在同目錄唯一暫存檔寫入並 flush 後原子發布；finally 刪除殘留暫存，避免讀者看到半份備份。</summary>
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

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
    string Sha256);

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
internal sealed record PluginTypeExport(Guid Id, string Name, string TypeName);

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
