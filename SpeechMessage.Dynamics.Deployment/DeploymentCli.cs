/*
 * 檔案整體說明
 *
 * 目的與定位：本檔是部署工具的共同命令入口，讓P0 Plugin註冊與P1結構操作共用既有connector、
 * 組織白名單、固定artifacts邊界、寫鎖及終端例外回報；P2 Channel命令在清單路由前分流。
 *
 * 主要功能與流程：先解析命令／選項、拒絕非goodshepherdback與外部plan/export路徑；
 * 明確--schema或export-metadata轉交SchemaDeploymentCli，該路徑不需Plugin DLL。
 * export-legacy 以有界唯讀查詢匯出安全 metadata 並回報啟用筆數，不需要清單或 DLL；
 * 舊plan/apply/verify/export/rollback/smoke則載入固定registration manifest及必要Plugin PE，
 * 單組件委派DeploymentEngine；多組件由 partial 的文件路由完整交給DocumentDeploymentEngine，
 * 使用組織共用備份根及已 flush 的固定名稱索引，保留原P0公開語法與設定佔位安全驗證。
 * verify有metadata差異回1，只有秘密佔位尚未比對時回3，不因零操作假稱秘密或推播已驗證。
 * smoke 將本次操作的 output.WriteLine 借給核心，只發布已驗證的唯一 PluginFileVersion 行；
 * 版本缺失或格式歧義回失敗，不輸出原始 trace，也不在靜態欄位保留版本或 writer。
 *
 * 擁有權與隔離：一次命令最多一個OrganizationLease，寫入命令另持本機同組織OS檔案鎖；
 * using先歸還client再釋放寫鎖。所有命令資料與輸出目錄均為本次操作，沒有跨命令SDK、
 * credential、request、Session或可變結果快取；secretReader僅在真正寫入設定瞬間解析。
 *
 * 失敗與限制：正常caller取消回130且不通知；真正例外在lease已歸還後交注入的共用report，
 * 只輸出固定摘要、例外型別及最多四層SDK數字錯碼，不讀Message、TraceText或設定本文。
 * parser／白名單與hash不代表實機寫入授權；VM隔離、備份、復原及本次明確操作範圍須另外證明。
 *
 * 維護驗證：保留DeploymentContractTests原62案與SmokePollingTests；修改路由須另跑SchemaCliTests，
 * 驗新模式不讀Plugin DLL、拒絕非法輸入、lease/drain、敏感輸出、取消、真共用診斷及並行寫鎖。
 */
using System;
using System.Globalization;
using System.IO;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>
/// 以固定命令表執行一次佈署操作。此 parser 不回顯參數值、不接受密碼或任意輸出路徑；
/// 每個有效命令最多取得一個 OrganizationLease，並在同步或非同步結束時立即釋放。
/// </summary>
internal static partial class DeploymentCli
{
    private const string AllowedOrganization = "goodshepherdback";
    private const string OrganizationOption = "--org";
    private const string PlanOption = "--plan";
    private const string FromOption = "--from";
    private const string ScenarioOption = "--scenario";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 解析並執行單一命令。輸出目錄固定位於 artifacts/d365，測試可注入獨立暫存根；
    /// connector 與共用錯誤回報皆為注入邊界，離線測試不會建立真實連線。
    /// </summary>
    /// <param name="args">命令列參數；不會記錄或回顯未知參數內容。</param>
    /// <param name="connector">每次 Connect 建立獨立服務的組織 connector。</param>
    /// <param name="output">安全摘要輸出 writer。</param>
    /// <param name="report">接收原始例外的共用診斷回報委派。</param>
    /// <param name="artifactRoot">僅供離線測試隔離輸出的根目錄；產品執行使用 repo 的 artifacts/d365。</param>
    /// <param name="secretReader">只在 apply 寫入設定時讀取 User Secrets 的委派；不保留或輸出秘密值。</param>
    /// <param name="writeLockTimeout">寫入命令等待同組織互斥鎖的上限；預設五分鐘。</param>
    /// <param name="cancellationToken">由命令呼叫端提供的取消訊號；正常取消不送例外告警。</param>
    /// <returns>成功為 0、caller 取消為 130，拒絕或失敗為其他非零值。</returns>
    public static int Run(
        string[] args,
        IOrganizationConnector connector,
        TextWriter output,
        Action<Exception> report,
        string? artifactRoot = null,
        Func<string, string>? secretReader = null,
        TimeSpan? writeLockTimeout = null,
        CancellationToken cancellationToken = default) =>
        RunWithInput(args, connector, output, report, null, artifactRoot, secretReader, writeLockTimeout, cancellationToken);

    /// <summary>
    /// 借入秘密輸入的命令入口。原八參數入口保留唯一 Run 簽章並委派此處；取消訊號保持最後一個參數，
    /// 不為新 Channel 指令改變既有呼叫的參數繫結。reader 的生命週期由程序組合根持有。
    /// </summary>
    /// <param name="args">原始命令列，parser 不回顯其值。</param>
    /// <param name="connector">每命令建立獨占租約的連線來源。</param>
    /// <param name="output">呼叫端擁有的安全摘要 writer。</param>
    /// <param name="report">資源釋放後接收原例外的共用入口。</param>
    /// <param name="secretInput">僅 Channel 寫入借用的標準輸入；不由本入口關閉。</param>
    /// <param name="artifactRoot">測試注入的獨立輸出根；產品使用固定 artifacts。</param>
    /// <param name="secretReader">程序擁有的 User Secrets 讀取委派。</param>
    /// <param name="writeLockTimeout">同組織寫鎖等待上限。</param>
    /// <param name="cancellationToken">呼叫端取消訊號，保持參數列末端。</param>
    /// <returns>成功 0、拒絕 2、故障 1、正常取消 130。</returns>
    internal static int RunWithInput(
        string[] args,
        IOrganizationConnector connector,
        TextWriter output,
        Action<Exception> report,
        TextReader? secretInput,
        string? artifactRoot = null,
        Func<string, string>? secretReader = null,
        TimeSpan? writeLockTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h"))
        {
            WriteUsage(output);
            return 0;
        }

        // 新命令先交專屬 parser；不能把 --schema 等未知參數誤導向其他部署路由。
        if (args[0] is "protect-token" or "assign-channel")
            return Channels.ChannelProvisioningCli.Run(args, connector, output, report,
                secretInput, artifactRoot, secretReader, writeLockTimeout, cancellationToken);

        // P1結構命令不需要Plugin DLL；只在明確--schema或export-metadata時進新路由，舊P0語法與安全邊界保留。
        if (args[0] == "export-metadata" || Array.IndexOf(args, "--schema") >= 0)
            return Schema.SchemaDeploymentCli.Run(args, connector, output, report, artifactRoot, writeLockTimeout, cancellationToken);

        if (!TryParse(args, out var command))
        {
            output.WriteLine("Arguments are invalid or not supported.");
            return 2;
        }

        if (!string.Equals(command.Organization, AllowedOrganization, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("Target organization is not in the P0 allowlist.");
            return 2;
        }

        if (command.Name == "switch" && command.Target == "legacy")
        {
            output.WriteLine("Legacy activation requires a scoped shadow round with approved inventory and owned test records.");
            return 2;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Command cancelled.");
            return 130;
        }

        var repositoryRoot = FindRepositoryRoot();
        var effectiveArtifactRoot = Path.GetFullPath(artifactRoot ??
            Path.Combine(repositoryRoot, "artifacts", "d365"));
        var organizationArtifactRoot = Path.GetFullPath(Path.Combine(effectiveArtifactRoot, AllowedOrganization));
        try
        {
            if (command.InputPath is not null)
            {
                if (!IsContainedPath(organizationArtifactRoot, command.InputPath, out var fullInputPath))
                {
                    output.WriteLine("Plan/export path must be inside this organization's artifacts directory.");
                    return 2;
                }

                command = command with { InputPath = fullInputPath };
            }

            using var writeLock = command.Name is "apply" or "rollback" or "smoke" or "switch"
                ? OrganizationWriteLock.Acquire(
                    organizationArtifactRoot,
                    writeLockTimeout ?? TimeSpan.FromMinutes(5),
                    cancellationToken)
                : null;
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = connector.Connect(command.Organization);
            if (command.Name == "export-legacy")
                return ExportLegacy(lease.Service, organizationArtifactRoot, output, cancellationToken);
            var operationRoot = CreateOperationDirectory(organizationArtifactRoot);
            if (command.Name == "switch")
            {
                var document = RegistrationManifestLoader.LoadDocument(repositoryRoot);
                var switchEngine = new DeploymentEngine(lease.Service, operationRoot, TimeProvider.System,
                    name => ResolveRequiredSecret(command.Organization, name, secretReader));
                switchEngine.SwitchModule(document, command.Organization, command.Module!, command.Target!,
                    () => LegacyRegistrationExporter.Export(lease.Service, cancellationToken: cancellationToken),
                    cancellationToken: cancellationToken);
                output.WriteLine("Module switch completed.");
                return 0;
            }
            var registration = RegistrationManifestLoader.LoadDocument(repositoryRoot);
            if (registration.Assemblies.Count > 1 && command.Name != "smoke")
                return RunDocument(command, registration, repositoryRoot, organizationArtifactRoot,
                    operationRoot, lease.Service, output, secretReader);
            // smoke 的核心只處理固定診斷情境，不消費清單中的組件，也不讀 DLL。
            var manifest = registration.Assemblies[0];
            var assemblyPath = Path.Combine(repositoryRoot, "artifacts", "plugins", manifest.AssemblyName + ".dll");
            var engine = new DeploymentEngine(lease.Service, operationRoot, TimeProvider.System,
                name => ResolveRequiredSecret(command.Organization, name, secretReader));

            switch (command.Name)
            {
                case "plan":
                {
                    var image = AssemblyCatalog.Read(assemblyPath, manifest);
                    var plan = engine.Plan(manifest, image, command.Organization);
                    WritePlan(operationRoot, plan);
                    output.WriteLine("Plan created.");
                    if (plan.SecretPlaceholders.Count != 0)
                    {
                        output.WriteLine("Secret values are not compared. This is not a secret-rotation plan.");
                    }

                    return 0;
                }
                case "apply":
                {
                    var inputPath = command.InputPath!;
                    var plan = ReadPlan(inputPath, organizationArtifactRoot);
                    if (plan.Organization != command.Organization)
                        throw new InvalidDataException("計畫組織與命令不一致。");
                    var image = AssemblyCatalog.Read(assemblyPath, manifest);
                    var export = engine.Apply(plan, manifest, image);
                    output.WriteLine("Plan applied. Current state export: " + Path.GetFileName(export));
                    return 0;
                }
                case "verify":
                {
                    var image = AssemblyCatalog.Read(assemblyPath, manifest);
                    var plan = engine.Plan(manifest, image, command.Organization);
                    return WriteVerificationResult(plan, output);
                }
                case "export":
                {
                    var path = Path.Combine(operationRoot, "export.json");
                    engine.WriteExport(command.Organization, path);
                    output.WriteLine("Metadata export created.");
                    return 0;
                }
                case "rollback":
                    engine.Rollback(command.InputPath!, manifest, command.Organization);
                    output.WriteLine("Rollback completed.");
                    return 0;
                case "smoke":
                {
                    var succeeded = engine.SmokeAsync(command.Scenario!, output.WriteLine, cancellationToken)
                        .GetAwaiter().GetResult();
                    output.WriteLine(succeeded ? "Smoke scenario passed." : "Smoke scenario failed.");
                    return succeeded ? 0 : 1;
                }
                default:
                    output.WriteLine("Command is not supported.");
                    return 2;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            try
            {
                report(exception);
            }
            catch
            {
                WriteFixedError(output);
            }

            WriteDataverseFaultCodes(exception, output);
            output.WriteLine("Command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
            return 1;
        }
    }

    /// <summary>
    /// 完整讀取安全 metadata 後才建立固定位置的唯一匯出檔；不讀 manifest、DLL 或秘密。
    /// 檔案以 CreateNew 防止覆寫，串流由本方法釋放；只有寫檔完整成功才輸出筆數。
    /// 仍有啟用中的舊步驟或流程即回傳非零，絕不從唯讀入口自行停用。
    /// </summary>
    /// <param name="service">本命令 lease 借出的 SDK，生命週期由外層 using 管理。</param>
    /// <param name="organizationRoot">已核定組織的 artifacts 目錄。</param>
    /// <param name="output">呼叫端擁有的摘要 writer，不輸出名稱、XAML 或設定。</param>
    /// <param name="cancellationToken">呼叫端取消，在 SDK 查詢及發布前檢查。</param>
    /// <returns>零表示無啟用舊註冊；一表示必須停止回報。</returns>
    private static int ExportLegacy(IOrganizationService service, string organizationRoot, TextWriter output,
        CancellationToken cancellationToken)
    {
        var snapshot = LegacyRegistrationExporter.Export(service, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(organizationRoot);
        var filename = "legacy-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".json";
        var path = Path.Combine(organizationRoot, filename);
        var created = false;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            // 不發布部分盤點；本方法只刪除本次命名且已建立的單檔，並保留原始寫入失敗。
            if (created) { try { File.Delete(path); } catch { } }
            throw;
        }
        output.WriteLine("Enabled legacy steps: " + snapshot.EnabledStepCount.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("Enabled legacy workflows: " + snapshot.EnabledWorkflowCount.ToString(CultureInfo.InvariantCulture));
        var enabled = snapshot.EnabledStepCount != 0 || snapshot.EnabledWorkflowCount != 0;
        output.WriteLine(enabled ? "Enabled legacy registrations found. Stop and report before continuing." : "Legacy metadata export created.");
        return enabled ? 1 : 0;
    }

    /// <summary>
    /// 原例外交給共用回報入口後，僅補充 SDK fault 的數字碼；不讀取任何訊息、Reason、
    /// TraceText、ErrorDetails 或 Data。最多迭代四個節點，循環鏈也會停止，不跨命令保留 fault。
    /// 此補充輸出失敗不覆蓋原例外、不遞迴回報，也不改變命令的失敗結果或既有資源釋放順序。
    /// </summary>
    /// <param name="exception">已交給共用回報入口的原始例外；只辨識 SDK 的強型別 fault。</param>
    /// <param name="output">由呼叫端持有的摘要 writer，本方法不取得其所有權。</param>
    private static void WriteDataverseFaultCodes(Exception exception, TextWriter output)
    {
        if (exception is not FaultException<OrganizationServiceFault> fault)
        {
            return;
        }

        try
        {
            var current = fault.Detail;
            for (var index = 0; index < 4 && current is not null; index++)
            {
                // 在 Int32 本身格式化，負數保持八位而不因提升成 Int64 產生符號延伸。
                output.WriteLine("Dataverse fault code[" + index.ToString(CultureInfo.InvariantCulture) +
                    "]: 0x" + current.ErrorCode.ToString("X8", CultureInfo.InvariantCulture));
                current = current.InnerFault;
            }
        }
        catch
        {
            // 原始失敗已先上報；補充診斷不可另起告警或中斷既有固定失敗摘要。
        }
    }

    /// <summary>
    /// 將規劃結果轉成 verify 的固定輸出與程序結束碼；純輸出步驟不持有服務、秘密或可變跨命令狀態。
    /// 與 CLI 共用同一實作，離線測試可以直接用記憶體組織的實際計畫檢查輸出契約。
    /// </summary>
    /// <param name="plan">本次命令的不可變計畫，不含秘密實值。</param>
    /// <param name="output">由呼叫端持有並釋放的固定摘要 writer。</param>
    /// <returns>公開設定一致且沒有秘密佔位時為 0；metadata 有差異為 1；秘密未比對而不能完整驗證為 3。</returns>
    internal static int WriteVerificationResult(DeploymentPlan plan, TextWriter output) =>
        WriteVerificationStatus(plan.Operations.Count != 0 ? 1 : plan.SecretPlaceholders.Count != 0 ? 3 : 0, output);

    /// <summary>單組件與完整文件共用固定安全文字，不因組件數改變零／一／三的公開驗證語意。</summary>
    private static int WriteVerificationStatus(int status, TextWriter output)
    {
        if (status == 1)
        {
            output.WriteLine("Deployment state differs from the registration manifest.");
            return 1;
        }

        if (status == 3)
        {
            // 不為了取得綠燈讀取 secrets，也不能把部分驗證冒充完整一致；此為預期未完成，不送例外告警。
            output.WriteLine("Non-secret metadata matches. Secret values were not compared; verification is incomplete (exit 3).");
            output.WriteLine("This is not a secret-rotation plan. Validate test delivery separately; secret changes are not detected or applied by this plan.");
            return 3;
        }

        output.WriteLine("Deployment state matches the registration manifest.");
        return 0;
    }

    /// <summary>解析唯一命令、允許選項與其值；未知參數的實際內容不會進入回應。</summary>
    /// <param name="args">原始命令列參數。</param>
    /// <param name="command">驗證後固定命令資料。</param>
    /// <returns>語法、值及命令互斥條件皆正確時為 true。</returns>
    private static bool TryParse(string[] args, out ParsedCommand command)
    {
        command = default;
        var name = args[0];
        if (name is not ("plan" or "apply" or "verify" or "export" or "export-legacy" or "rollback" or "smoke" or "switch"))
        {
            return false;
        }

        string? organization = null;
        string? inputPath = null;
        string? scenario = null;
        string? module = null;
        string? target = null;
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--org" or "--plan" or "--from" or "--scenario" or "--module" or "--to") ||
                !seen.Add(option) ||
                index + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            var value = args[++index];
            switch (option)
            {
                case OrganizationOption:
                    organization = value;
                    break;
                case PlanOption:
                    inputPath = value;
                    break;
                case FromOption:
                    inputPath = value;
                    break;
                case ScenarioOption:
                    scenario = value;
                    break;
                case "--module": module = value; break;
                case "--to": target = value; break;
            }
        }

        if (name != "switch" && (module is not null || target is not null)) return false;
        var valid = name switch
        {
            "plan" or "verify" or "export" or "export-legacy" => organization is not null && inputPath is null && scenario is null,
            "apply" => organization is not null && HasExactlyTwoOptions(seen, OrganizationOption, PlanOption) && inputPath is not null && scenario is null,
            "rollback" => organization is not null && HasExactlyTwoOptions(seen, OrganizationOption, FromOption) && inputPath is not null && scenario is null,
            "switch" => organization is not null && module is not null && target is "enabled" or "legacy" or "off" &&
                seen.Count == 3 && seen.Contains(OrganizationOption) && seen.Contains("--module") && seen.Contains("--to"),
            "smoke" => organization is not null && HasExactlyTwoOptions(seen, OrganizationOption, ScenarioOption) && scenario is not null && inputPath is null,
            _ => false
        };
        if (!valid)
        {
            return false;
        }

        command = new ParsedCommand(name, organization!, inputPath, scenario) { Module = module, Target = target };
        return true;
    }

    /// <summary>以固定選項常值驗證集合，避免建立每次解析都配置的暫存陣列。</summary>
    /// <param name="seen">本次命令已出現的選項集合。</param>
    /// <param name="first">命令所需的第一個選項。</param>
    /// <param name="second">命令所需的第二個選項。</param>
    /// <returns>集合恰好包含兩個預期選項時為 true。</returns>
    private static bool HasExactlyTwoOptions(System.Collections.Generic.HashSet<string> seen, string first, string second) =>
        seen.Count == 2 && seen.Contains(first) && seen.Contains(second);

    /// <summary>確認輸入 plan/export 完整位於指定組織的 artifacts 子樹；拒絕路徑穿越與現存祖先 link／junction。</summary>
    /// <param name="root">本組織輸出根。</param>
    /// <param name="path">命令列輸入的檔案位置。</param>
    /// <param name="fullPath">正規化後的絕對位置。</param>
    /// <returns>正規化路徑是根目錄內的檔案時為 true。</returns>
    internal static bool IsContainedPath(string root, string path, out string fullPath)
    {
        fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, fullPath);
        var contained = !Path.IsPathRooted(relative) &&
            !string.Equals(relative, ".", StringComparison.Ordinal) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        if (!contained) return false;
        for (var current = fullPath; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    /// <summary>建立唯一 UTC 操作目錄；同時執行的命令取得不同目錄且不互相覆寫。</summary>
    /// <param name="organizationRoot">白名單組織的 artifacts 根目錄。</param>
    /// <returns>此命令獨占的輸出目錄。</returns>
    internal static string CreateOperationDirectory(string organizationRoot)
    {
        Directory.CreateDirectory(organizationRoot);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
        var path = Path.Combine(organizationRoot, timestamp + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>以共用嚴格 JSON 入口讀取單組件計畫；未知／重複欄位及超出預算一律拒絕，不回顯內容。</summary>
    /// <param name="path">已經過 artifacts 根目錄驗證的 JSON 路徑。</param>
    /// <param name="organizationRoot">命令核定的組織根，開檔前再次核對路徑邊界。</param>
    /// <returns>待由引擎重新驗證雜湊與即時組織指紋的 plan。</returns>
    private static DeploymentPlan ReadPlan(string path, string organizationRoot) =>
        ReadDocumentFile<DeploymentPlan>(path, organizationRoot);

    /// <summary>以 UTF-8 無 BOM／CRLF 與固定檔名發布 JSON 及 Markdown；每檔 flush 關閉後才可讀，不覆寫證據。</summary>
    /// <param name="directory">本次 plan 專用輸出目錄。</param>
    /// <param name="plan">只含摘要、hash 與秘密佔位名稱的 plan。</param>
    private static void WritePlan(string directory, DeploymentPlan plan)
    {
        WriteDocumentFile(directory, "plan.json", JsonSerializer.Serialize(plan, JsonOptions));
        WriteDocumentFile(directory, "plan.md", DeploymentEngine.Markdown(plan));
    }

    /// <summary>只在 apply 寫入設定時解析 placeholder；錯誤不包含要求的 secret 名稱或值。</summary>
    /// <param name="organization">經白名單固定為 goodshepherdback 的組織別名。</param>
    /// <param name="name">清單佔位符名稱。</param>
    /// <param name="secretReader">由目前程序組態提供的 User Secrets 讀取委派。</param>
    /// <returns>存在的 User Secrets 值。</returns>
    private static string ResolveRequiredSecret(
        string organization,
        string name,
        Func<string, string>? secretReader)
    {
        if (secretReader is null)
        {
            throw new InvalidOperationException("部署 User Secrets 缺少必要的安全設定。");
        }

        var value = secretReader(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("部署 User Secrets 缺少必要的安全設定。")
            : value;
    }

    /// <summary>從目前工作目錄向上找唯一 SpeechMessageProducts.sln 根目錄。</summary>
    /// <returns>方案根目錄的絕對路徑。</returns>
    internal static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("無法找到 SpeechMessageProducts.sln。");
    }

    /// <summary>輸出不帶動態路徑、參數或例外內容的固定錯誤，供回報回呼本身故障時使用。</summary>
    /// <param name="output">命令摘要 writer。</param>
    private static void WriteFixedError(TextWriter output)
    {
        try
        {
            output.WriteLine("Command failed. Diagnostic reporting was unavailable.");
        }
        catch
        {
        }
    }

    /// <summary>只列出命令與參數名稱，不含帳密、路徑或 secrets 值。</summary>
    /// <param name="output">命令摘要 writer。</param>
    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Commands: plan --org <name> | apply --org <name> --plan <file> | verify --org <name>");
        output.WriteLine("          switch --org goodshepherdback --module <module> --to enabled|off");
        output.WriteLine("          export-legacy --org goodshepherdback (read-only; stops if legacy registrations are enabled)");
        output.WriteLine("          export --org <name> | rollback --org <name> --from <file>");
        output.WriteLine("          smoke --org <name> --scenario <p0-ping-trace|p0-ping-push>");
        output.WriteLine("          export-metadata --org <name> | plan/verify --org <name> --schema <file>");
        output.WriteLine("          apply --org <name> --schema <file> --plan <file>");
        output.WriteLine("          protect-token --org goodshepherdback --channel-id <id> (two secret lines from stdin)");
        output.WriteLine("          assign-channel --org goodshepherdback --channel-id <id> --account <guid>");
    }

    /// <summary>只包含已驗證的命令名稱與參數；不保留其他命令列文字。</summary>
    private readonly record struct ParsedCommand(string Name, string Organization, string? InputPath, string? Scenario)
    {
        /// <summary>切換的核定模組，不作資料存取授權。</summary>
        public string? Module { get; init; }
        /// <summary>切換目標；舊輪由程序內 runner 帶完整安全範圍才可執行。</summary>
        public string? Target { get; init; }
    }
}
