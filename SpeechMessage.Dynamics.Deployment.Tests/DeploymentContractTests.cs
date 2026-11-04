using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 用記憶體組織及獨立暫存檔保護 P0-12 的十七項原始契約。
/// 測試刻意注入版本／token 衝突、組織變動、竄改、連線機密與 smoke 失敗；
/// 斷言實際 SDK 寫入、備份順序及 finally 清理，不呼叫 D365、LINE 或正式金鑰。
/// </summary>
[Collection("Deployment working directory")]
public sealed class DeploymentContractTests
{
    /// <summary>保留 plan 操作的固定順序，避免每個案例重複配置相同期待陣列。</summary>
    private static readonly string[] ExpectedPlanOperations = { "CreateAssembly", "CreateType", "CreateStep" };

    /// <summary>只供 CLI 拒絕案例使用的合成密碼參數範本；呼叫時複製以維持案例隔離。</summary>
    private static readonly string[] PasswordArgumentTemplate =
        { "plan", "--org", Fixture.Organization, "--password", Fixture.SecretValue };

    /// <summary>只供白名單拒絕案例使用的合成參數範本；每次呼叫複製避免共用可變陣列。</summary>
    private static readonly string[] NonAllowlistedOrganizationArguments =
        { "plan", "--org", "production" };

    /// <summary>CLI 說明案例的固定輸入，避免每次執行測試重複配置參數陣列。</summary>
    private static readonly string[] HelpArguments = { "--help" };

    /// <summary>未知命令案例的固定輸入，避免重複配置相同參數陣列。</summary>
    private static readonly string[] UnknownCommandArguments = { "unsupported" };

    /// <summary>空組織須規劃組件、類型與步驟；規劃本身完全不寫入。</summary>
    [Fact(DisplayName = "空組織產生三層註冊計畫且不寫入")]
    public void Plan_EmptyOrganization_CreatesAssemblyTypeAndStep()
    {
        using var f = new Fixture();
        var plan = f.Plan();
        Assert.Equal(ExpectedPlanOperations, plan.Operations.Select(x => x.Kind));
        Assert.Empty(f.Service.Writes);
        Assert.Equal(TimeSpan.Zero, plan.CreatedUtc.Offset);
        Assert.Equal(64, plan.Sha256.Length);
    }

    /// <summary>第一次套用後再規劃同清單不得新增或重建步驟。</summary>
    [Fact(DisplayName = "相同清單重跑沒有變更")]
    public void Plan_SameState_NoOperations()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.Empty(f.Plan().Operations);
        Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
    }

    /// <summary>
    /// 已註冊的追蹤步驟切成推播、再切回追蹤時，必須更新原資料庫 ID 而非重建。
    /// 用會計數的假 secrets 驗證 plan 不解析秘密，apply 才解析，完成後同清單沒有差異，
    /// 且停用推播會移除不再使用的 secure 記錄；輸出不保留解析後內容。
    /// </summary>
    [Fact(DisplayName = "既有步驟可切換追蹤與推播而不重建或洩漏秘密")]
    public void Apply_ConfigurationChanged_UpdatesSameStepAndRemainsIdempotent()
    {
        using var f = new Fixture();
        using var push = new Fixture(push: true);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var stepId = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep")).Id;
        var plan = f.Engine.Plan(push.Manifest, f.Image, Fixture.Organization);
        Assert.Equal("UpdateStepConfiguration", Assert.Single(plan.Operations).Kind);
        Assert.Equal(0, f.SecretReads);
        f.Engine.Apply(plan, push.Manifest, f.Image);
        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(stepId, step.Id);
        Assert.Contains("\"mode\":\"push\"", step.GetAttributeValue<string>("configuration"), StringComparison.Ordinal);
        Assert.Equal(2, f.SecretReads);
        Assert.Empty(f.Engine.Plan(push.Manifest, f.Image, Fixture.Organization).Operations);
        Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstepsecureconfig"));
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.Equal(stepId, Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep")).Id);
        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstepsecureconfig"));
        Assert.Empty(f.Plan().Operations);
        Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(Path.Combine(f.Root, "export.json")), StringComparison.Ordinal);
    }

    /// <summary>
    /// 同一推播模板在假秘密保留或輪替後，都不得以唯讀 metadata 計畫宣稱秘密已驗證。
    /// 注入新的假 reader，斷言不被讀取、不寫資料、回傳未完成碼、租用輸出不含任何秘密；不是連線驗收。
    /// </summary>
    /// <param name="rotated">是否將本次假秘密來源換成不同值。</param>
    [Theory(DisplayName = "推播秘密未比對不能回報完整成功")]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_SecretValuesUnchecked_ReturnsIncompleteAndDisclosesScope(bool rotated)
    {
        using var f = new Fixture(push: true);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var readCount = 0;
        var writes = f.Service.Writes.Count;
        const string rotatedValue = "ROTATED-SYNTHETIC-NOT-A-CREDENTIAL";
        var engine = new DeploymentEngine(f.Service, f.Root, f.Clock, _ =>
        {
            readCount++;
            return rotated ? rotatedValue : Fixture.SecretValue;
        });
        var plan = engine.Plan(f.Manifest, f.Image, Fixture.Organization);
        Assert.Empty(plan.Operations);
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(3, DeploymentCli.WriteVerificationResult(plan, output));

        Assert.Equal(0, readCount);
        Assert.Equal(writes, f.Service.Writes.Count);
        var text = output.ToString();
        Assert.Contains("Secret values were not compared", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Deployment state matches", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
        Assert.DoesNotContain(rotatedValue, text, StringComparison.Ordinal);
    }

    /// <summary>含秘密佔位的 Markdown 必須揭露不比較值與不處理輪替，且既有佔位不解析契約仍成立。</summary>
    [Fact(DisplayName = "計畫明列秘密值未比對與輪替限制")]
    public void Plan_WithSecrets_DisclosesVerificationAndRotationLimits()
    {
        using var f = new Fixture(push: true);
        var text = DeploymentEngine.Markdown(f.Plan());
        Assert.Contains("Secret values are not compared", text, StringComparison.Ordinal);
        Assert.Contains("not a secret-rotation plan", text, StringComparison.Ordinal);
        Assert.Equal(0, f.SecretReads);
        Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
    }

    /// <summary>不含秘密的追蹤清單可完整驗證；確實存在 metadata 操作時仍回傳差異碼，不會被未完成碼掩蓋。</summary>
    [Fact(DisplayName = "公開設定相同成功而 metadata 差異仍失敗")]
    public void Verify_TraceMetadata_PreservesSuccessAndDifferenceCodes()
    {
        using var f = new Fixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(1, DeploymentCli.WriteVerificationResult(f.Plan(), output));
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.Equal(0, DeploymentCli.WriteVerificationResult(f.Plan(), output));
    }

    /// <summary>
    /// 重現首次建立成功後才開始讀取步驟的情境；假服務以獨立的官方 schema 拒絕未知欄位。
    /// 執行 verify 共用的真實規劃及結果判定，斷言一致時成功、沒有額外寫入或秘密讀取。
    /// 把安全設定查閱名稱誤改為 secureconfigid 時，本案例必須在 SDK 查詢邊界失敗。
    /// </summary>
    [Fact(DisplayName = "已建立追蹤步驟可用官方欄位驗證且唯讀")]
    public void Verify_AppliedTraceStep_UsesSupportedSchemaWithoutWrites()
    {
        using var f = new Fixture(enabled: true);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var writesBeforeVerify = f.Service.Writes.Count;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, DeploymentCli.WriteVerificationResult(f.Plan(), output));

        Assert.Equal(writesBeforeVerify, f.Service.Writes.Count);
        Assert.Equal(0, f.SecretReads);
        Assert.Contains("Deployment state matches", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 在唯一暫存方案根產生未簽章 net462 樣本及假清單，執行真正 CLI 的註冊、驗證、匯出及停用。
    /// 固定 schema 的假服務會拒絕不存在的欄位；驗證必須成功且每個命令的連線租約都釋放。
    /// 本類別所屬集合禁止與其他集合平行；程序工作目錄只在 try 期間切換並在 finally 恢復，
    /// 不接觸正式清單、部署 DLL、User Secrets、簽章金鑰或任何真實服務。
    /// </summary>
    [Fact(DisplayName = "真正 CLI 註冊驗證匯出與停用通過獨立 schema")]
    public void Cli_TraceLifecycle_ValidatesSchemaAndDisposesEveryLease()
    {
        using var f = new Fixture(enabled: true);
        var manifestDirectory = Path.Combine(f.Root, "d365", "registration");
        var pluginDirectory = Path.Combine(f.Root, "artifacts", "plugins");
        var cliArtifacts = Path.Combine(f.Root, "cli");
        Directory.CreateDirectory(manifestDirectory);
        Directory.CreateDirectory(pluginDirectory);
        var manifestPath = Path.Combine(manifestDirectory, "plugins.registration.json");
        File.WriteAllText(Path.Combine(f.Root, "SpeechMessageProducts.sln"), string.Empty);
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(f.Manifest));
        File.Copy(f.Emit(), Path.Combine(pluginDirectory, "SpeechMessage.Dynamics.Plugins.dll"));
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reports = new List<Exception>();
        var previousDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(f.Root);
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "plan", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
            var plan = Assert.Single(Directory.GetFiles(cliArtifacts, "plan.json", SearchOption.AllDirectories));
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "apply", "--org", Fixture.Organization, "--plan", plan }, connector, output, reports.Add, cliArtifacts));
            var stepId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
            var writesBeforeVerify = f.Service.Writes.Count;
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "verify", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
            Assert.Equal(writesBeforeVerify, f.Service.Writes.Count);
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "export", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));

            var disabled = f.Manifest with
            {
                Steps = new[] { f.Manifest.Steps[0] with { EnabledByOrganization = new Dictionary<string, bool>(StringComparer.Ordinal) } }
            };
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(disabled));
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "plan", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
            var disablePlan = Assert.Single(Directory.GetFiles(cliArtifacts, "plan.json", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, plan, StringComparison.Ordinal)));
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "apply", "--org", Fixture.Organization, "--plan", disablePlan }, connector, output, reports.Add, cliArtifacts));
            Assert.Equal(0, DeploymentCli.Run(
                new[] { "verify", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
            var step = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
            Assert.Equal(stepId, step.Id);
            Assert.Equal(1, step.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Empty(reports);
            Assert.Equal(7, connector.ConnectCount);
            Assert.Equal(7, connector.DisposeCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    /// <summary>
    /// 以官方查閱名稱注入既有安全設定，避免產品建立與讀取共用同一個拼字錯誤而假綠燈。
    /// 真正匯出只能保留存在性，不能解析或保存合成秘密，也不得為匯出而修改組織。
    /// </summary>
    [Fact(DisplayName = "匯出官方安全設定查閱只記錄存在性")]
    public void Export_ExistingSecureLookup_PreservesPresenceWithoutSecret()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        SeedExistingSecureConfiguration(f);
        var writesBeforeExport = f.Service.Writes.Count;
        var path = Path.Combine(f.Root, "existing-secure-export.json");

        f.Engine.WriteExport(Fixture.Organization, path);

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var step = Assert.Single(json.RootElement.GetProperty("Steps").EnumerateArray());
        Assert.True(step.GetProperty("HasSecureConfig").GetBoolean());
        Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(writesBeforeExport, f.Service.Writes.Count);
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>
    /// 既有 trace 步驟帶官方安全查閱時，清單要求空白設定必須規劃設定更新，不能誤判一致。
    /// 斷言更新沿用資料庫 ID、解除原查閱後刪除原安全列，且後續 verify 成功且不讀秘密。
    /// </summary>
    [Fact(DisplayName = "追蹤設定更新清除官方安全查閱及原安全列")]
    public void Apply_ExistingSecureLookup_TraceTransitionDeletesOriginalConfiguration()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var stepId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
        SeedExistingSecureConfiguration(f);
        var plan = f.Plan();
        Assert.Equal("UpdateStepConfiguration", Assert.Single(plan.Operations).Kind);

        f.Engine.Apply(plan, f.Manifest, f.Image);

        var step = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(stepId, step.Id);
        Assert.Null(step.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid"));
        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepsecureconfig"));
        Assert.Empty(f.Plan().Operations);
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>既有組件主次版本不同，須在任何寫入之前拒絕。</summary>
    [Fact(DisplayName = "主次版本衝突停止")]
    public void Plan_AssemblyMajorMinorChanged_Stops()
    {
        using var f = new Fixture(); f.SeedAssembly("2.0.0.0");
        Assert.Throws<InvalidDataException>(() => f.Plan());
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>同名但不同公鑰身份不能被視為一般內容更新。</summary>
    [Fact(DisplayName = "公鑰身份改變停止")]
    public void Plan_AssemblyPublicKeyTokenChanged_Stops()
    {
        using var f = new Fixture(); f.SeedAssembly(token: "fedcba9876543210");
        Assert.Throws<InvalidDataException>(() => f.Plan());
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>以會拋出的 secret reader 證明 plan 及 Markdown 根本不解析秘密。</summary>
    [Fact(DisplayName = "計畫與 Markdown 保留佔位且不讀秘密")]
    public void Plan_SecretPlaceholders_NeverResolvedIntoPlanOrMarkdown()
    {
        using var f = new Fixture(push: true);
        var plan = f.Plan();
        var markdown = DeploymentEngine.Markdown(plan);
        Assert.Contains("${secret:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, markdown, StringComparison.Ordinal);
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>規劃後組織出現新的組件，apply 必須拒絕且沒有寫入。</summary>
    [Fact(DisplayName = "組織現況已變拒絕套用")]
    public void Apply_OrganizationChangedSincePlan_Refuses()
    {
        using var f = new Fixture(); var plan = f.Plan(); f.SeedAssembly();
        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, f.Manifest, f.Image));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>任意操作清單被修改卻未重算 hash，寫入前就拒絕。</summary>
    [Fact(DisplayName = "計畫遭竄改拒絕套用")]
    public void Apply_TamperedPlanHash_Refuses()
    {
        using var f = new Fixture(); var plan = f.Plan() with { Operations = Array.Empty<DeploymentOperation>() };
        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, f.Manifest, f.Image));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>第一個 SDK 寫入時檢查可獨佔讀取的 export，證明備份已落盤與關檔。</summary>
    [Fact(DisplayName = "任何寫入前先完成備份")]
    public void Apply_ExportsCurrentStateBeforeAnyWrite()
    {
        using var f = new Fixture();
        f.Service.BeforeWrite = () => {
            var file = Assert.Single(Directory.GetFiles(f.Root, "export.json", SearchOption.AllDirectories));
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(stream.Length > 0);
        };
        var backup = f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.True(File.Exists(backup));
        Assert.NotEmpty(f.Service.Writes);
    }

    /// <summary>缺少組織啟用值一律停用；只有明確 true 才啟用，兩者皆保護。</summary>
    /// <param name="enabled">該組織是否明確核定啟用。</param>
    [Theory(DisplayName = "新步驟預設停用，明確啟用才開啟")]
    [InlineData(false)] [InlineData(true)]
    public void Apply_NewStep_DefaultDisabledUnlessManifestEnables(bool enabled)
    {
        using var f = new Fixture(enabled: enabled);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(enabled ? 0 : 1, step.GetAttributeValue<OptionSetValue>("statecode").Value);
    }

    /// <summary>更新組件內容及停用狀態後，rollback 要還原兩者而非只改版本字串。</summary>
    [Fact(DisplayName = "回復備份恢復組件內容及步驟啟用狀態")]
    public void Rollback_RestoresExportedStepStateAndAssemblyContent()
    {
        using var f = new Fixture(enabled: true);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var changedImage = f.Image with { Content = Convert.ToBase64String(new byte[] { 9, 8, 7 }), Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 9, 8, 7 })) };
        var changed = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { EnabledByOrganization = new Dictionary<string, bool>(StringComparer.Ordinal) } } };
        var plan = f.Engine.Plan(changed, changedImage, Fixture.Organization);
        var backup = f.Engine.Apply(plan, changed, changedImage);
        f.Engine.Rollback(backup, f.Manifest, Fixture.Organization);
        var assembly = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "pluginassembly"));
        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(f.Image.Content, assembly.GetAttributeValue<string>("content"));
        Assert.Equal(0, step.GetAttributeValue<OptionSetValue>("statecode").Value);
    }

    /// <summary>非白名單在 connector 被呼叫前拒絕，避免連到其他組織。</summary>
    [Fact(DisplayName = "非白名單組織在連線前拒絕")]
    public void Org_NotInAllowlist_Refused()
    {
        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.NotEqual(0, DeploymentCli.Run((string[])NonAllowlistedOrganizationArguments.Clone(), connector, output, _ => { }));
        Assert.Equal(0, connector.Calls);
    }

    /// <summary>密碼選項即使語法正常也必須拒絕，不連線、不回顯值。</summary>
    [Fact(DisplayName = "命令列密碼參數拒絕且不回顯")]
    public void Cli_PasswordArgument_Rejected()
    {
        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.NotEqual(0, DeploymentCli.Run((string[])PasswordArgumentTemplate.Clone(), connector, output, _ => { }));
        Assert.Equal(0, connector.Calls);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>互斥租約保護同組織寫入；持有第一把鎖時注入第二次取得，並斷言釋放後可重入。</summary>
    [Fact(DisplayName = "同組織寫入互斥且釋放後可再次取得")]
    public void OrganizationWriteLock_ExclusiveLeaseCanBeReacquiredAfterDispose()
    {
        using var f = new Fixture();
        var first = OrganizationWriteLock.Acquire(f.Root, TimeSpan.Zero);

        Assert.Throws<TimeoutException>(() => OrganizationWriteLock.Acquire(f.Root, TimeSpan.Zero));

        first.Dispose();
        first.Dispose();
        using var second = OrganizationWriteLock.Acquire(f.Root, TimeSpan.Zero);
        Assert.NotNull(second);
    }

    /// <summary>help 與未知命令不建立連線；以會計數的失敗 connector 證明 parser 先行拒絕。</summary>
    [Fact(DisplayName = "說明與無效命令不建立連線")]
    public void Cli_HelpAndUnknownCommand_DoNotConnect()
    {
        var connector = new ThrowingConnector();
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, DeploymentCli.Run(HelpArguments, connector, output, _ => { }));
        Assert.NotEqual(0, DeploymentCli.Run(UnknownCommandArguments, connector, output, _ => { }));

        Assert.Equal(0, connector.Calls);
    }

    /// <summary>路徑穿越輸入在 connector 呼叫前拒絕；假 connector 計數是未建立連線的決定性證據。</summary>
    [Fact(DisplayName = "Plan 路徑逸出組織輸出根時先拒絕")]
    public void Cli_ApplyPathOutsideOrganizationRoot_RefusesBeforeConnect()
    {
        using var f = new Fixture();
        var connector = new ThrowingConnector();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var outsidePath = Path.Combine(f.Root, "outside-plan.json");

        var exitCode = DeploymentCli.Run(
            new[] { "apply", "--org", Fixture.Organization, "--plan", outsidePath },
            connector,
            output,
            _ => { },
            artifactRoot: f.Root);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, connector.Calls);
        Assert.DoesNotContain(outsidePath, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>持有同組織寫入鎖時 CLI 不得建立連線；立即逾時是故障注入，連線計數是判定斷言。</summary>
    [Fact(DisplayName = "寫入命令先取得互斥鎖再建立連線")]
    public void Cli_WriteCommand_WaitsForOrganizationLockBeforeConnect()
    {
        using var f = new Fixture();
        var organizationRoot = Path.Combine(f.Root, Fixture.Organization);
        using var heldLock = OrganizationWriteLock.Acquire(organizationRoot, TimeSpan.Zero);
        var connector = new ThrowingConnector();
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = DeploymentCli.Run(
            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
            connector,
            output,
            _ => { },
            artifactRoot: f.Root,
            writeLockTimeout: TimeSpan.Zero);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, connector.Calls);
        Assert.Contains(nameof(TimeoutException), output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 假服務在建立 smoke task 後同步觸發 caller 取消；斷言 CLI 不告警、清除 task 並釋放連線。
    /// 反射只用來讓此案例先在缺少取消入口時以明確契約失敗；執行時仍呼叫正式 CLI 方法。
    /// </summary>
    [Fact(DisplayName = "CLI 取消 smoke 時清理資料且不送例外告警")]
    public void Cli_SmokeCallerCancellation_CleansUpWithoutReporting()
    {
        var run = typeof(DeploymentCli).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(method =>
                method.Name == nameof(DeploymentCli.Run) &&
                method.GetParameters().LastOrDefault()?.ParameterType == typeof(CancellationToken));
        Assert.NotNull(run);

        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
        f.Service.OnTaskCreated = cancellation.Cancel;
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reportCount = 0;

        var exitCode = (int)run!.Invoke(null, new object?[]
        {
            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
            connector,
            output,
            (Action<Exception>)(_ => reportCount++),
            f.Root,
            null,
            TimeSpan.Zero,
            cancellation.Token
        })!;

        Assert.Equal(130, exitCode);
        Assert.Equal(0, reportCount);
        Assert.Equal(1, connector.DisposeCount);
        Assert.Contains("Command cancelled.", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(f.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    /// <summary>同組織互斥鎖等待期間收到 caller 取消時，必須立即停止且尚未建立連線。</summary>
    [Fact(DisplayName = "等待寫入鎖時取消 smoke 不建立連線")]
    public async Task Cli_SmokeCancellationWhileWaitingForLock_StopsBeforeConnect()
    {
        using var f = new Fixture();
        var organizationRoot = Path.Combine(f.Root, Fixture.Organization);
        using var heldLock = OrganizationWriteLock.Acquire(organizationRoot, TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();
        var connector = new ThrowingConnector();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reportCount = 0;
        var cancelTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            cancellation.Cancel();
        });

        var exitCode = DeploymentCli.Run(
            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
            connector,
            output,
            _ => reportCount++,
            artifactRoot: f.Root,
            writeLockTimeout: TimeSpan.FromMilliseconds(100),
            cancellationToken: cancellation.Token);
        await cancelTask;

        Assert.Equal(130, exitCode);
        Assert.Equal(0, connector.Calls);
        Assert.Equal(0, reportCount);
        Assert.Contains("Command cancelled.", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>額外設定的組織別名會在連線前遭拒，避免 User Secrets 擴大唯一核准邊界。</summary>
    [Fact(DisplayName = "部署設定不得加入其他允許組織")]
    public void Connector_AdditionalAllowedOrganization_IsRejected()
    {
        var configuration = CreateConnectorConfiguration(additionalOrganization: "production");
        using var configurationOwner = (IDisposable)configuration;

        Assert.Throws<InvalidOperationException>(
            () => new UserSecretsOrganizationConnector(configuration));
    }

    /// <summary>
    /// 合法假 User Secrets 必須逐欄傳給建線邊界，且命令租約結束後釋放唯一假 client；
    /// 工廠只建立記憶體服務，案例不會嘗試連到設定中的 Organization.svc。
    /// </summary>
    [Fact(DisplayName = "合法 User Secrets 建立單次 client 並由租約釋放")]
    public void Connector_ValidSecrets_CreatesAndDisposesOneClient()
    {
        var configuration = CreateConnectorConfiguration();
        using var configurationOwner = (IDisposable)configuration;
        var service = new MemoryOrganization();
        var sawExpectedEndpoint = false;
        var sawExpectedCredentials = false;
        var connector = new UserSecretsOrganizationConnector(
            configuration,
            (organizationUrl, username, password) =>
            {
                sawExpectedEndpoint =
                    organizationUrl == "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc";
                sawExpectedCredentials =
                    username == "synthetic-user" && password == Fixture.SecretValue;
                return service;
            });

        var lease = connector.Connect(Fixture.Organization);

        Assert.Same(service, lease.Service);
        Assert.True(sawExpectedEndpoint);
        Assert.True(sawExpectedCredentials);
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, service.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => lease.Service);
    }

    /// <summary>端點主機或路徑不符時使用固定錯誤，且在建立地端 client 前拒絕。</summary>
    [Theory(DisplayName = "部署端點必須精確符合唯一 Organization.svc")]
    [InlineData("https://example.invalid/XRMServices/2011/Organization.svc")]
    [InlineData("http://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherdback.speechmessage.com.tw/")]
    [InlineData("https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc?mode=test")]
    public void Connector_InvalidOrganizationUrl_IsRejected(string organizationUrl)
    {
        var configuration = CreateConnectorConfiguration(organizationUrl: organizationUrl);
        using var configurationOwner = (IDisposable)configuration;
        var connector = new UserSecretsOrganizationConnector(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
            () => connector.Connect(Fixture.Organization));

        Assert.DoesNotContain(Fixture.SecretValue, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>缺少任一必要 credential 時固定拒絕；注入假設定並確認錯誤不回顯其值。</summary>
    /// <param name="missingCredential">本次故障注入要移除的設定鍵。</param>
    [Theory(DisplayName = "部署連線缺少必要 User Secrets 時拒絕")]
    [InlineData("Username")]
    [InlineData("Password")]
    public void Connector_MissingCredential_IsRejected(string missingCredential)
    {
        var configuration = CreateConnectorConfiguration(missingCredential: missingCredential);
        using var configurationOwner = (IDisposable)configuration;
        var connector = new UserSecretsOrganizationConnector(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
            () => connector.Connect(Fixture.Organization));

        Assert.DoesNotContain(Fixture.SecretValue, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>清單 JSON 損壞時不把原始內容寫入錯誤；檔案只在案例私有暫存根中建立。</summary>
    [Fact(DisplayName = "註冊清單錯誤不回顯原始內容")]
    public void ManifestLoader_MalformedJson_DoesNotEchoFileContent()
    {
        using var f = new Fixture();
        var directory = Path.Combine(f.Root, "d365", "registration");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugins.registration.json"),
            "malformed-" + Fixture.SecretValue);

        var exception = Assert.Throws<InvalidDataException>(
            () => RegistrationManifestLoader.Load(f.Root));

        Assert.DoesNotContain(Fixture.SecretValue, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>PE 掃描拒絕空檔與非 .NET 中繼資料，並確定性釋放各自的輸入檔案。</summary>
    [Fact(DisplayName = "組件掃描拒絕空檔與非 PE 內容")]
    public void AssemblyCatalog_EmptyAndNonPeFiles_AreRejected()
    {
        using var f = new Fixture();
        var emptyPath = Path.Combine(f.Root, "empty.dll");
        var textPath = Path.Combine(f.Root, "text.dll");
        File.WriteAllBytes(emptyPath, Array.Empty<byte>());
        File.WriteAllText(textPath, "synthetic-not-a-pe");

        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(emptyPath, f.Manifest));
        Assert.Throws<BadImageFormatException>(() => AssemblyCatalog.Read(textPath, f.Manifest));
        using var exclusive = new FileStream(textPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>清單的組件名稱必須與 PE metadata 完全相同，錯誤身份不得進入部署摘要。</summary>
    [Fact(DisplayName = "組件名稱與清單不符時拒絕")]
    public void AssemblyCatalog_AssemblyNameMismatch_IsRejected()
    {
        using var f = new Fixture();
        var path = f.Emit();
        var manifest = f.Manifest with { AssemblyName = "Different.Assembly" };

        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, manifest));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>注入不符合診斷內容的 trace，驗收失敗仍要刪掉唯一測試 task。</summary>
    [Fact(DisplayName = "smoke 斷言失敗仍刪除測試資料")]
    public async Task Smoke_AssertionFails_StillDeletesTestRecord()
    {
        using var f = new Fixture(); f.Service.Trace = "incorrect trace";
        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
    }

    /// <summary>用假時鐘推進三分鐘，不真的等待；逾時失敗後 task 與 timer 均須釋放。</summary>
    [Fact(DisplayName = "smoke 逾時仍刪除並回傳失敗")]
    public async Task Smoke_Timeout_DeletesTestRecordAndReturnsFailure()
    {
        using var f = new Fixture(); f.Service.Trace = null;
        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
        Assert.True(f.Service.TraceQueryCount > 1);
        Assert.Equal(2, f.Clock.DelayCount);
        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    /// <summary>追蹤在第二次輪詢才出現時仍須成功，並於結果確認後刪除唯一測試 task。</summary>
    [Fact(DisplayName = "smoke 等待延遲追蹤並在成功後刪除測試資料")]
    public async Task Smoke_TraceArrivesAfterPolling_ReturnsSuccessAndDeletesTestRecord()
    {
        using var f = new Fixture();
        f.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
        f.Service.TraceAvailableAfterPoll = 2;

        Assert.True(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));

        Assert.Equal(2, f.Service.TraceQueryCount);
        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    /// <summary>連線例外含假密碼時，CLI 輸出與共用 Exception.log 都不得寫出原始訊息。</summary>
    [Fact(DisplayName = "連線錯誤所有輸出不含秘密且保留例外身份")]
    public void Logs_NeverContainSecretValues()
    {
        var run = typeof(Program).GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(run);

        using var f = new Fixture();
        var connector = new ThrowingConnector();
        var configuration = CreateConnectorConfiguration();
        using var configurationOwner = (IDisposable)configuration;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = (int)run!.Invoke(null, new object?[]
        {
            new[] { "plan", "--org", Fixture.Organization },
            connector,
            output,
            configuration,
            Path.Combine(f.Root, "Logs"),
            CancellationToken.None
        })!;
        var emittedFiles = Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Append(output.ToString());
        var combinedOutput = string.Join(Environment.NewLine, emittedFiles);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(1, connector.Calls);
        Assert.False(ExceptionReporting.IsActive);
        Assert.True(File.Exists(Path.Combine(f.Root, "Logs", "Exception.log")));
        Assert.Contains(nameof(InvalidOperationException), combinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, combinedOutput, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>成功命令只建立一份服務租約，結束後立即釋放且不把連線留給下一個命令。</summary>
    [Fact(DisplayName = "命令完成後釋放唯一連線租約")]
    public void Cli_ExportCommand_DisposesCommandLease()
    {
        using var f = new Fixture();
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = DeploymentCli.Run(
            new[] { "export", "--org", Fixture.Organization },
            connector,
            output,
            _ => { },
            artifactRoot: f.Root);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, connector.ConnectCount);
        Assert.Equal(1, connector.DisposeCount);
        Assert.Single(Directory.GetFiles(f.Root, "export.json", SearchOption.AllDirectories));
    }

    /// <summary>連線後查詢失敗仍釋放租約；輸出遮罩假密碼並把原例外交給共用回報入口。</summary>
    [Fact(DisplayName = "命令失敗時釋放連線且遮罩例外訊息")]
    public void Cli_ExportFailure_DisposesLeaseAndRedactsException()
    {
        using var f = new Fixture();
        var failure = new InvalidOperationException("synthetic connection detail " + Fixture.SecretValue);
        f.Service.RetrieveFailure = failure;
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Exception? reported = null;

        var exitCode = DeploymentCli.Run(
            new[] { "export", "--org", Fixture.Organization },
            connector,
            output,
            exception => reported = exception,
            artifactRoot: f.Root);

        Assert.NotEqual(0, exitCode);
        Assert.Same(failure, reported);
        Assert.Equal(1, connector.ConnectCount);
        Assert.Equal(1, connector.DisposeCount);
        Assert.Contains(nameof(InvalidOperationException), output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Sensitive details are not shown.", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Details were sent", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// SDK fault 的各文字欄位及回報故障均注入假秘密；只有兩層數字碼可跨越輸出邊界。
    /// 斷言原例外只上報一次、上報前租約已釋放，且回報失敗也不改變失敗碼或遺失數字診斷。
    /// </summary>
    /// <param name="reporterFails">是否讓既有共用回報入口在接收原例外後故障。</param>
    [Theory(DisplayName = "Dataverse 失敗只顯示數字碼且保留原始回報與釋放")]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_DataverseFault_ShowsOnlyCodesAndReportsOriginalOnce(bool reporterFails)
    {
        using var f = new Fixture();
        var detail = CreateSensitiveFault(unchecked((int)0x80040220),
            CreateSensitiveFault(unchecked((int)0x80040217)));
        var failure = new FaultException<OrganizationServiceFault>(detail, new FaultReason(Fixture.SecretValue));
        f.Service.RetrieveFailure = failure;
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reportCount = 0;
        Exception? reported = null;
        var disposedBeforeReport = false;

        var exitCode = DeploymentCli.Run(
            new[] { "export", "--org", Fixture.Organization },
            connector,
            output,
            exception =>
            {
                reportCount++;
                reported = exception;
                disposedBeforeReport = connector.DisposeCount == 1;
                if (reporterFails) throw new InvalidOperationException(Fixture.SecretValue);
            },
            artifactRoot: f.Root);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, reportCount);
        Assert.Same(failure, reported);
        Assert.True(disposedBeforeReport);
        Assert.Equal(1, connector.ConnectCount);
        Assert.Equal(1, connector.DisposeCount);
        Assert.Empty(f.Service.Writes);
        var text = output.ToString();
        Assert.Contains("Dataverse fault code[0]: 0x80040220", text, StringComparison.Ordinal);
        Assert.Contains("Dataverse fault code[1]: 0x80040217", text, StringComparison.Ordinal);
        Assert.Contains("Sensitive details are not shown.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
        if (reporterFails)
            Assert.Contains("Diagnostic reporting was unavailable.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 注入五層 fault 或回指第一層的循環；輸出至多四個固定八位數字碼，不可無界走訪。
    /// 人工列出的正負整數期待值同時保護符號位、零值與前導零，案例結束必須釋放唯一租約。
    /// </summary>
    /// <param name="cyclic">是否在第四層後回指第一層，取代第五層。</param>
    [Theory(DisplayName = "Dataverse 內層與循環 fault 最多輸出四碼")]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_DataverseFault_NestedOrCyclicChain_IsBounded(bool cyclic)
    {
        using var f = new Fixture();
        var fourth = CreateSensitiveFault(int.MaxValue, CreateSensitiveFault(42));
        var first = CreateSensitiveFault(int.MinValue,
            CreateSensitiveFault(0, CreateSensitiveFault(1, fourth)));
        if (cyclic) fourth.InnerFault = first;
        f.Service.RetrieveFailure = new FaultException<OrganizationServiceFault>(first);
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = DeploymentCli.Run(
            new[] { "export", "--org", Fixture.Organization },
            connector, output, _ => { }, artifactRoot: f.Root);

        Assert.Equal(1, exitCode);
        var lines = output.ToString().Split(Environment.NewLine)
            .Where(line => line.StartsWith("Dataverse fault code[", StringComparison.Ordinal)).ToArray();
        Assert.Collection(lines,
            line => Assert.Equal("Dataverse fault code[0]: 0x80000000", line),
            line => Assert.Equal("Dataverse fault code[1]: 0x00000000", line),
            line => Assert.Equal("Dataverse fault code[2]: 0x00000001", line),
            line => Assert.Equal("Dataverse fault code[3]: 0x7FFFFFFF", line));
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, connector.DisposeCount);
    }

    /// <summary>缺失 Detail 的 SDK fault 仍須上報原例外並釋放租約；不能為取得數字診斷再拋空參考例外。</summary>
    [Fact(DisplayName = "Dataverse fault 沒有 Detail 時維持安全失敗")]
    public void Cli_DataverseFault_NullDetail_PreservesFailure()
    {
        using var f = new Fixture();
        var failure = new FaultException<OrganizationServiceFault>(null!, new FaultReason(Fixture.SecretValue));
        f.Service.RetrieveFailure = failure;
        var connector = new TrackingConnector(f.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var reports = new List<Exception>();

        Assert.Equal(1, DeploymentCli.Run(
            new[] { "export", "--org", Fixture.Organization },
            connector, output, reports.Add, artifactRoot: f.Root));

        Assert.Same(failure, Assert.Single(reports));
        Assert.Equal(1, connector.DisposeCount);
        Assert.Contains("Sensitive details are not shown.", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Dataverse fault code[", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 經真正程序診斷 owner 注入含假秘密的 SDK fault，檢查 stdout 與全部暫存日誌。
    /// 同時要求數字碼可見及共用日誌已建立，防止以跳過回報或完全隱藏診斷取得假綠燈。
    /// </summary>
    [Fact(DisplayName = "Dataverse fault 的控制台與共用日誌皆不含秘密")]
    public void Logs_DataverseFault_NeverContainSecretValues()
    {
        var run = typeof(Program).GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(run);
        using var f = new Fixture();
        f.Service.RetrieveFailure = new FaultException<OrganizationServiceFault>(
            CreateSensitiveFault(unchecked((int)0x80040220), CreateSensitiveFault(1)),
            new FaultReason(Fixture.SecretValue));
        var connector = new TrackingConnector(f.Service);
        var configuration = CreateConnectorConfiguration();
        using var configurationOwner = (IDisposable)configuration;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = (int)run!.Invoke(null, new object?[]
        {
            new[] { "export", "--org", Fixture.Organization },
            connector, output, configuration, Path.Combine(f.Root, "Logs"), CancellationToken.None
        })!;

        Assert.Equal(1, exitCode);
        Assert.Equal(1, connector.DisposeCount);
        Assert.False(ExceptionReporting.IsActive);
        Assert.True(File.Exists(Path.Combine(f.Root, "Logs", "Exception.log")));
        var combined = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
                .Select(File.ReadAllText).Append(output.ToString()));
        Assert.DoesNotContain(Fixture.SecretValue, combined, StringComparison.Ordinal);
        Assert.Contains("Dataverse fault code[0]: 0x80040220", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>plan 不讀取佔位；apply 才把假值送入假服務，備份與輸出仍不得保存該值。</summary>
    [Fact(DisplayName = "佔位秘密只在套用時解析且不落入輸出")]
    public void Apply_SecretPlaceholdersResolvedOnlyForWrites()
    {
        using var f = new Fixture(push: true);
        var plan = f.Plan();

        Assert.Equal(0, f.SecretReads);
        Assert.DoesNotContain(Fixture.SecretValue, DeploymentEngine.Markdown(plan), StringComparison.Ordinal);

        f.Engine.Apply(plan, f.Manifest, f.Image);

        Assert.Equal(2, f.SecretReads);
        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
        var secureConfigurationReference = Assert.IsType<EntityReference>(step["sdkmessageprocessingstepsecureconfigid"]);
        var secureConfiguration = Assert.Single(f.Service.Rows.Where(
            x => x.LogicalName == "sdkmessageprocessingstepsecureconfig" &&
                 x.Id == secureConfigurationReference.Id));
        Assert.Equal(Fixture.SecretValue, secureConfiguration.GetAttributeValue<string>("secureconfig"));
        var exportedJson = File.ReadAllText(Path.Combine(f.Root, "export.json"));
        Assert.DoesNotContain(Fixture.SecretValue, exportedJson, StringComparison.Ordinal);
    }

    /// <summary>匯出集合若收到相同資料庫 ID 的衝突步驟，必須拒絕而不能以列順序挑一筆。</summary>
    [Fact(DisplayName = "匯出遇到重複步驟資料庫 ID 時拒絕")]
    public void Export_DuplicateStepIds_FailsClosed()
    {
        using var f = new Fixture();
        var assemblyId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var message = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessage"));
        var filter = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessagefilter"));
        f.Service.Seed(new Entity("pluginassembly", assemblyId)
        {
            ["name"] = f.Image.Name,
            ["version"] = f.Image.Version,
            ["publickeytoken"] = f.Image.PublicKeyToken,
            ["content"] = f.Image.Content,
            ["isolationmode"] = new OptionSetValue(2),
            ["sourcetype"] = new OptionSetValue(0)
        });
        f.Service.Seed(new Entity("plugintype", typeId)
        {
            ["name"] = "DiagnosticsPingPlugin",
            ["typename"] = f.Manifest.Steps[0].Type,
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId)
        });
        var duplicateId = Guid.NewGuid();
        f.Service.Seed(CreateStep(duplicateId, typeId, message, filter, "first"));
        f.Service.Seed(CreateStep(duplicateId, typeId, message, filter, "conflicting"));

        Assert.Throws<InvalidDataException>(() =>
            f.Engine.WriteExport(Fixture.Organization, Path.Combine(f.Root, "duplicate-export.json")));
    }

    /// <summary>DLL 有清單未列出的公開 IPlugin 時不能只驗證清單中型別存在而假通過。</summary>
    [Fact(DisplayName = "DLL 中每個外掛都必須有註冊")]
    public void Manifest_EveryPluginTypeInBuiltDll_IsRegistered()
    {
        using var f = new Fixture();
        var path = f.Emit(extra: true);
        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, f.Manifest));
    }

    /// <summary>清單列了 DLL 中不存在的類型時 L2 必須拒絕；不載入執行樣本。</summary>
    [Fact(DisplayName = "清單類型不存在 DLL 時拒絕")]
    public void Manifest_TypeNotInBuiltDll_Fails()
    {
        using var f = new Fixture(); var path = f.Emit();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Type = "Missing.Plugin" } } };
        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, manifest));
    }

    /// <summary>辨識繼承 PluginShellBase 的公開外掛，避免正式 DLL 的間接 IPlugin 實作漏列。</summary>
    [Fact(DisplayName = "PE 掃描辨識 PluginShellBase 子類別")]
    public void Manifest_PluginShellSubclass_IsRecognized()
    {
        using var f = new Fixture();
        var path = f.Emit();

        var image = AssemblyCatalog.Read(path, f.Manifest);

        Assert.Equal(f.Manifest.Steps[0].Type, Assert.Single(image.PluginTypes));
    }

    /// <summary>
    /// 建立只屬當前案例的 SDK fault；各文字與字典鍵值都注入假秘密，不保留真實連線資訊。
    /// 只允許其數字碼輸出；InnerFault 可供深度與循環故障注入，不涉及任何背景資源。
    /// </summary>
    /// <param name="errorCode">人工指定、可獨立核對格式的數字碼。</param>
    /// <param name="innerFault">本案例的內層 fault；不跨案例共用。</param>
    /// <returns>含多個敏感文字欄位的合成 fault。</returns>
    private static OrganizationServiceFault CreateSensitiveFault(int errorCode, OrganizationServiceFault? innerFault = null) =>
        new()
        {
            ErrorCode = errorCode,
            Message = Fixture.SecretValue,
            TraceText = Fixture.SecretValue,
            ErrorDetails = { [Fixture.SecretValue] = Fixture.SecretValue },
            InnerFault = innerFault
        };

    /// <summary>
    /// 建立只含合成 connector 設定的記憶體組態；所有憑證樣本均為假值，
    /// 不讀取 User Secrets、環境變數或任何 D365 組織。
    /// </summary>
    /// <param name="organizationUrl">待驗證的 Organization.svc URL。</param>
    /// <param name="additionalOrganization">可選的白名單越界案例。</param>
    /// <param name="missingCredential">可選的必要設定移除案例。</param>
    /// <returns>由案例持有並 Dispose 的記憶體組態根。</returns>
    private static IConfigurationRoot CreateConnectorConfiguration(
        string organizationUrl = "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
        string? additionalOrganization = null,
        string? missingCredential = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Deployment:AllowedOrganizations:0"] = Fixture.Organization,
            ["Deployment:Organizations:goodshepherdback:OrganizationUrl"] = organizationUrl,
            ["Deployment:Organizations:goodshepherdback:Username"] = "synthetic-user",
            ["Deployment:Organizations:goodshepherdback:Password"] = Fixture.SecretValue
        };
        if (additionalOrganization is not null)
        {
            values["Deployment:AllowedOrganizations:1"] = additionalOrganization;
        }

        if (missingCredential is not null)
        {
            values.Remove("Deployment:Organizations:goodshepherdback:" + missingCredential);
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>只持有假密碼的失敗工廠；不建立任何服務或背景資源。</summary>
    private sealed class ThrowingConnector : IOrganizationConnector
    {
        /// <summary>呼叫次數供拒絕必須發生在連線前的決定性斷言。</summary>
        public int Calls { get; private set; }
        /// <summary>故障注入的原始實例，故意含假機密。</summary>
        public InvalidOperationException Failure { get; } = new("connection failed: " + Fixture.SecretValue);
        /// <summary>每次都拋固定合成例外，完全沒有網路行為。</summary>
        /// <param name="organization">不回顯的測試組織。</param><returns>此故障案例不產生租約。</returns>
        public OrganizationLease Connect(string organization) { Calls++; throw Failure; }
    }

    /// <summary>以記憶體服務故障注入命令生命週期，記錄每次連線與租約釋放。</summary>
    private sealed class TrackingConnector(IOrganizationService service) : IOrganizationConnector
    {
        /// <summary>證明每個 CLI 命令只建立一條連線。</summary>
        public int ConnectCount { get; private set; }
        /// <summary>證明命令成功或失敗後都會釋放其租約。</summary>
        public int DisposeCount { get; private set; }

        /// <summary>每次建立獨立租約，沒有網路、共用池或跨案例狀態。</summary>
        /// <param name="organization">已通過白名單的測試組織。</param>
        /// <returns>只供當次命令持有的記憶體服務租約。</returns>
        public OrganizationLease Connect(string organization)
        {
            ConnectCount++;
            return new OrganizationLease(service, () => DisposeCount++);
        }
    }

    /// <summary>
    /// 在案例組織種入使用官方邏輯名稱的既有安全設定與查閱，不呼叫待測建立邏輯。
    /// 所有值皆為合成資料，只由 fixture 擁有，案例 Dispose 時與暫存目錄一併釋放。
    /// </summary>
    /// <param name="fixture">本案例獨占的記憶體組織與合成秘密來源。</param>
    private static void SeedExistingSecureConfiguration(Fixture fixture)
    {
        var secureId = Guid.NewGuid();
        fixture.Service.Seed(new Entity("sdkmessageprocessingstepsecureconfig", secureId)
        {
            ["secureconfig"] = Fixture.SecretValue
        });
        var step = Assert.Single(fixture.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        fixture.Service.Update(new Entity("sdkmessageprocessingstep", step.Id)
        {
            ["sdkmessageprocessingstepsecureconfigid"] =
                new EntityReference("sdkmessageprocessingstepsecureconfig", secureId)
        });
    }

    /// <summary>建立指定身份的 Plugin 步驟，供重複 ID 故障注入使用。</summary>
    /// <param name="stepId">兩列刻意共用的資料庫 ID。</param>
    /// <param name="typeId">所屬 Plugin 型別 ID。</param>
    /// <param name="message">Create 訊息 metadata。</param>
    /// <param name="filter">task 訊息篩選器 metadata。</param>
    /// <param name="configuration">用以區分衝突列的合成設定。</param>
    /// <returns>本案例專用的假步驟列。</returns>
    private static Entity CreateStep(Guid stepId, Guid typeId, Entity message, Entity filter, string configuration) =>
        new("sdkmessageprocessingstep", stepId)
        {
            ["eventhandler"] = new EntityReference("plugintype", typeId),
            ["sdkmessageid"] = message.ToEntityReference(),
            ["sdkmessagefilterid"] = filter.ToEntityReference(),
            ["stage"] = new OptionSetValue(40),
            ["mode"] = new OptionSetValue(1),
            ["rank"] = 1,
            ["configuration"] = configuration,
            ["statecode"] = new OptionSetValue(1),
            ["statuscode"] = new OptionSetValue(2)
        };
}

/// <summary>
/// CLI 離線案例需要短暫變更程序工作目錄，因此整個集合禁止與其他集合平行。
/// 工作目錄由案例 finally 恢復；此集合只協調測試排程，不保存組織、憑證或案例資源。
/// </summary>
[CollectionDefinition("Deployment working directory", DisableParallelization = true)]
public sealed class DeploymentWorkingDirectoryScope
{
}

/// <summary>每案例獨占暫存、假服務與時鐘，Dispose 確定性清除；不碰使用者 secrets 或正式 key。</summary>
internal sealed class Fixture : IDisposable
{
    /// <summary>唯一核定的組織邊界。</summary>
    public const string Organization = "goodshepherdback";
    /// <summary>只供機密掃描與故障注入的假值。</summary>
    public const string SecretValue = "SYNTHETIC-SECRET-NOT-A-CREDENTIAL";
    /// <summary>唯一測試根，不從外部輸入組成清理目標。</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-deployment-" + Guid.NewGuid().ToString("N"));
    /// <summary>不含業務資料的記憶體組織。</summary>
    public MemoryOrganization Service { get; } = new();
    /// <summary>不依賴牆上時鐘的有限等待來源。</summary>
    public AdvancingClock Clock { get; } = new();
    /// <summary>固定清單候選，不含實際 token 或 userId。</summary>
    public RegistrationManifest Manifest { get; }
    /// <summary>以小型合成內容保護內容雜湊與還原，不作正式部署。</summary>
    public AssemblyImage Image { get; } = new("SpeechMessage.Dynamics.Plugins", "1.0.0.0", "0123456789abcdef", "AQID", Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), new[] { "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin" });
    /// <summary>本案例待測核心，使用相同假服務。</summary>
    public DeploymentEngine Engine { get; }
    /// <summary>證明唯讀計畫沒有解析秘密的計數。</summary>
    public int SecretReads { get; private set; }
    /// <summary>建立各案例隔離資源；推播也只使用佔位與假 secret。</summary>
    /// <param name="push">是否測試推播設定佔位。</param><param name="enabled">是否明確啟用測試步驟。</param>
    public Fixture(bool push = false, bool enabled = false)
    {
        Directory.CreateDirectory(Root);
        var step = new StepRegistration {
            UnsecureConfig = push ? "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"${secret:TestLineUserId}\"}" : "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
            SecureConfig = push ? "${secret:TestChannelToken}" : "",
            EnabledByOrganization = enabled ? new Dictionary<string, bool>(StringComparer.Ordinal) { [Organization] = true } : new Dictionary<string, bool>(StringComparer.Ordinal)
        };
        Manifest = new RegistrationManifest { Steps = new[] { step } };
        Engine = new DeploymentEngine(Service, Root, Clock, _ => { SecretReads++; return SecretValue; });
        Service.Seed(new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = "Create" });
        Service.Seed(new Entity("sdkmessagefilter", Guid.NewGuid()) { ["primaryobjecttypecode"] = "task", ["sdkmessageid"] = Service.Rows[0].ToEntityReference() });
    }
    /// <summary>對目前假組織規劃，測試無網路副作用。</summary>
    /// <returns>含固定時鐘的計畫。</returns>
    public DeploymentPlan Plan() => Engine.Plan(Manifest, Image, Organization);
    /// <summary>注入既有組件，以版本或公鑰衝突測試拒絕。</summary>
    /// <param name="version">合成版本。</param><param name="token">合成公鑰身份。</param>
    public void SeedAssembly(string version = "1.0.0.0", string token = "0123456789abcdef") => Service.Seed(new Entity("pluginassembly", Guid.NewGuid()) {
        ["name"] = Image.Name, ["version"] = version, ["publickeytoken"] = token, ["content"] = Image.Content,
        ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0)
    });
    /// <summary>離線產生沒有金鑰的最小 net462 樣本，僅供 MetadataReader 測試。</summary>
    /// <param name="extra">是否注入未註冊型別。</param><returns>不載入執行的 PE 路徑。</returns>
    public string Emit(bool extra = false)
    {
        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.6.2\")] " +
            "namespace Microsoft.Xrm.Sdk { public interface IPlugin { } } namespace SpeechMessage.Dynamics.Plugins { public abstract class PluginShellBase : Microsoft.Xrm.Sdk.IPlugin { } } " +
            "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { public sealed class DiagnosticsPingPlugin : SpeechMessage.Dynamics.Plugins.PluginShellBase { } " +
            (extra ? "public sealed class UnregisteredPlugin : Microsoft.Xrm.Sdk.IPlugin { }" : "") + " }";
        var compilation = CSharpCompilation.Create(Image.Name, new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(Root, "sample.dll"); var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(x => x.ToString())));
        return path;
    }
    /// <summary>待測程式已釋放 reader、stream、timer 後立即刪除案例自己的唯一根。</summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

/// <summary>無外部資源的 SDK 假組織，僅模擬 P0 中繼資料與測試 task；不把它當作產品實作。</summary>
internal sealed class MemoryOrganization : IOrganizationService, IDisposable
{
    /// <summary>
    /// 官方 sdkmessageprocessingstep schema 的本包使用範圍；獨立於產品常數維護，不能接受 secureconfigid 別名。
    /// 此唯讀用途的私有固定陣列不含案例資料；未知欄位在查詢或寫入前拒絕，避免寬鬆替身掩蓋伺服器錯誤。
    /// </summary>
    private static readonly string[] StepAttributes =
    {
        "sdkmessageprocessingstepid", "name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
        "stage", "mode", "rank", "filteringattributes", "configuration",
        "sdkmessageprocessingstepsecureconfigid", "statecode", "statuscode"
    };

    /// <summary>案例自己的候選列，不跨 fixture 共用。</summary>
    public List<Entity> Rows { get; } = new();
    /// <summary>用於驗證寫入先後順序的固定事件，不包含欄位值。</summary>
    public List<string> Writes { get; } = new();
    /// <summary>寫入前檢查 export 的故障注入點。</summary>
    public Action? BeforeWrite { get; set; }
    /// <summary>只供 smoke 對應記錄的合成追蹤內容；null 表示尚無回應。</summary>
    public string? Trace { get; set; }
    /// <summary>設定第幾次 plugintracelog 查詢才可看見合成追蹤。</summary>
    public int TraceAvailableAfterPoll { get; set; } = 1;
    /// <summary>記錄 smoke 追蹤查詢次數，證明案例真的輪詢而非立即失敗。</summary>
    public int TraceQueryCount { get; private set; }
    /// <summary>故障注入：查詢時回傳同一例外，供 CLI 驗證遮罩與租約清理。</summary>
    public Exception? RetrieveFailure { get; set; }
    /// <summary>smoke task 與其關聯記錄建立後觸發 caller 取消的測試掛鉤。</summary>
    public Action? OnTaskCreated { get; set; }
    /// <summary>假 client 的 deterministic release 計數；用於確認連線租約只釋放一次。</summary>
    public int DisposeCount { get; private set; }
    /// <summary>只在安排階段注入列，不計入產品 SDK 寫入。</summary>
    /// <param name="entity">只屬本案例的假列。</param>
    public void Seed(Entity entity) => Rows.Add(entity);
    /// <summary>模擬資料庫生成 ID，並記錄唯一身份；task 帶動自己專用的非同步作業。</summary>
    /// <param name="entity">候選列。</param><returns>資料庫身份。</returns>
    public Guid Create(Entity entity)
    {
        ValidateStepAttributes(entity.LogicalName, entity.Attributes.Keys);
        Record("Create:" + entity.LogicalName);
        var copy = Copy(entity); if (copy.Id == Guid.Empty) copy.Id = Guid.NewGuid(); Rows.Add(copy);
        if (entity.LogicalName == "task")
        {
            var correlation = Guid.NewGuid();
            Seed(new Entity("asyncoperation", Guid.NewGuid()) { ["regardingobjectid"] = copy.ToEntityReference(), ["correlationid"] = correlation });
            if (Trace is not null)
            {
                Seed(new Entity("plugintracelog", Guid.NewGuid()) { ["correlationid"] = correlation, ["typename"] = new StepRegistration().Type, ["messageblock"] = Trace });
            }

            OnTaskCreated?.Invoke();
        }
        return copy.Id;
    }
    /// <summary>只更新同一實體的資料庫身份，不按顯示名稱合併。</summary>
    /// <param name="entity">包含 ID 的更新。</param>
    public void Update(Entity entity)
    {
        ValidateStepAttributes(entity.LogicalName, entity.Attributes.Keys);
        Record("Update:" + entity.LogicalName); var row = Rows.Single(x => x.LogicalName == entity.LogicalName && x.Id == entity.Id);
        foreach (var pair in entity.Attributes) row[pair.Key] = pair.Value;
    }
    /// <summary>刪除精確身份，用以驗證 smoke finally 的所有出口。</summary>
    /// <param name="entityName">固定邏輯名。</param><param name="id">本案例的資料庫 ID。</param>
    public void Delete(string entityName, Guid id) { Record("Delete:" + entityName); Rows.RemoveAll(x => x.LogicalName == entityName && x.Id == id); }
    /// <summary>以 detached 副本回傳，避免測試以共用可變參考掩蓋發布缺陷。</summary>
    /// <param name="entityName">邏輯名。</param><param name="id">資料庫 ID。</param><param name="columnSet">要求欄位。</param><returns>獨立副本。</returns>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        ValidateStepAttributes(entityName, columnSet.Columns);
        return Copy(Rows.Single(x => x.LogicalName == entityName && x.Id == id));
    }
    /// <summary>只支援測試實際需要的 QueryExpression，未支援格式直接失敗。</summary>
    /// <param name="query">含隔離條件的結構查詢。</param><returns>獨立結果列集合。</returns>
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        if (RetrieveFailure is not null) throw RetrieveFailure;
        var expression = Assert.IsType<QueryExpression>(query);
        ValidateStepAttributes(expression.EntityName, expression.ColumnSet.Columns);
        ValidateStepFilter(expression.EntityName, expression.Criteria);
        if (expression.EntityName == "plugintracelog")
        {
            TraceQueryCount++;
            if (TraceQueryCount < TraceAvailableAfterPoll)
            {
                return new EntityCollection();
            }
        }

        var rows = Rows.Where(x => x.LogicalName == expression.EntityName && Matches(x, expression.Criteria)).Select(Copy).ToList();
        return new EntityCollection(rows);
    }

    /// <summary>
    /// 在結果列是否存在之前驗證步驟欄位，重現 Dataverse 的 QueryBuilderNoAttribute 邊界。
    /// 只回傳固定型別、數字碼與合成說明，不含輸入欄位值；非步驟實體保持既有模擬範圍。
    /// </summary>
    /// <param name="entityName">本次 SDK 操作的實體邏輯名稱。</param>
    /// <param name="attributes">查詢或寫入要求的欄位名稱，不讀取對應內容。</param>
    private static void ValidateStepAttributes(string entityName, IEnumerable<string> attributes)
    {
        if (entityName == "sdkmessageprocessingstep" &&
            attributes.Any(attribute => !StepAttributes.Contains(attribute, StringComparer.Ordinal)))
        {
            throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
            {
                ErrorCode = unchecked((int)0x80041103),
                Message = "Synthetic metadata attribute was not found."
            });
        }
    }

    /// <summary>遞迴檢查 AND／OR 條件中的步驟欄位，防止只修正 ColumnSet 卻把錯字移到條件後假通過。</summary>
    /// <param name="entityName">查詢實體的邏輯名稱。</param>
    /// <param name="filter">由本次產品呼叫建立、不跨案例保留的條件樹。</param>
    private static void ValidateStepFilter(string entityName, FilterExpression filter)
    {
        ValidateStepAttributes(entityName, filter.Conditions.Select(condition => condition.AttributeName));
        foreach (var child in filter.Filters)
        {
            ValidateStepFilter(entityName, child);
        }
    }
    /// <summary>模擬狀態切換；未定義訊息拒絕，避免假的成功。</summary>
    /// <param name="request">SDK 訊息。</param><returns>合成回應。</returns>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if (request.RequestName == "SetState") {
            var reference = (EntityReference)request["EntityMoniker"];
            Update(new Entity(reference.LogicalName, reference.Id) { ["statecode"] = request["State"], ["statuscode"] = request["Status"] });
            return new OrganizationResponse();
        }
        throw new NotSupportedException("Unsupported synthetic request.");
    }
    /// <summary>本包不使用關聯操作，意外使用立即失敗。</summary>
    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>本包不使用解除關聯，意外使用立即失敗。</summary>
    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>假服務沒有 OS handle；計數只用來斷言單次連線租約確實執行 release。</summary>
    public void Dispose() => DisposeCount++;
    /// <summary>只記固定事件，不記秘密或業務資料。</summary>
    /// <param name="name">編譯期種類。</param>
    private void Record(string name) { BeforeWrite?.Invoke(); Writes.Add(name); }
    /// <summary>建立欄位層級獨立候選，不把可變 SDK 選項或參照交回呼叫者。</summary>
    /// <param name="entity">來源列。</param><returns>detached 副本。</returns>
    private static Entity Copy(Entity entity)
    {
        var copy = new Entity(entity.LogicalName, entity.Id);
        foreach (var pair in entity.Attributes) copy[pair.Key] = pair.Value switch { EntityReference r => new EntityReference(r.LogicalName, r.Id), OptionSetValue o => new OptionSetValue(o.Value), _ => pair.Value };
        return copy;
    }

    /// <summary>只依精確欄位及 ID 判斷 AND／OR，未知比較不可假通過。</summary>
    /// <param name="row">候選列。</param><param name="filter">查詢條件。</param><returns>是否符合。</returns>
    private static bool Matches(Entity row, FilterExpression filter)
    {
        var values = filter.Conditions.Select(c => {
            object? actual = row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == row.LogicalName + "id" ? row.Id : null;
            if (actual is EntityReference reference) actual = reference.Id;
            if (actual is OptionSetValue option) actual = option.Value;
            return c.Operator switch { ConditionOperator.Equal => Equals(actual, c.Values[0]), ConditionOperator.In => c.Values.Contains(actual), _ => throw new NotSupportedException() };
        }).Concat(filter.Filters.Select(x => Matches(row, x)));
        return filter.FilterOperator == LogicalOperator.And ? values.All(x => x) : values.Any(x => x);
    }
}

/// <summary>
/// 單案例隔離的 UTC／單調假時鐘。短輪詢等待會快轉虛擬時間，長逾時計時器則保留至期限；
/// 因此測試能在不等待三分鐘的情況下驗證真正輪詢、逾時取消及 timer 釋放。
/// </summary>
internal sealed class AdvancingClock : TimeProvider
{
    /// <summary>只供本 fixture 的 UTC 時點。</summary>
    private DateTimeOffset now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    /// <summary>由本案例建立且尚未釋放的 timer；集合不跨 fixture 共用。</summary>
    private readonly List<OwnedTimer> timers = new();
    /// <summary>確認所有 delay 的取消登記與 timer 都釋放。</summary>
    public int ActiveTimers { get; private set; }
    /// <summary>記錄虛擬輪詢次數，避免 timeout 案例在第一次查詢前就被取消。</summary>
    public int DelayCount { get; private set; }
    /// <summary>回傳固定起點加上假延遲。</summary><returns>UTC 時點。</returns>
    public override DateTimeOffset GetUtcNow() => now;
    /// <summary>單調時間使用同一假時間，不讀硬體計時器。</summary><returns>tick 數。</returns>
    public override long GetTimestamp() => now.Ticks;
    /// <summary>固定 tick 頻率配合單調時間。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    /// <summary>長期限先排程；短輪詢等待快轉兩分鐘並執行所有已到期的一次性回呼。</summary>
    /// <param name="callback">延遲的單次完成回呼。</param><param name="state">延遲 owner 的狀態。</param><param name="dueTime">模擬延遲。</param><param name="period">不得重複。</param><returns>由等待者釋放的 timer。</returns>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new OwnedTimer(this, callback, state, dueTime);
        timers.Add(timer);
        ActiveTimers++;
        if (dueTime <= TimeSpan.FromSeconds(1))
        {
            DelayCount++;
            now += TimeSpan.FromMinutes(2);
            foreach (var scheduled in timers.ToArray())
            {
                scheduled.FireIfDue(now);
            }
        }

        return timer;
    }

    /// <summary>從案例 owner 移除已釋放 timer，讓生命週期斷言精確回到基線。</summary>
    /// <param name="timer">被釋放的單次 timer。</param>
    private void Release(OwnedTimer timer)
    {
        if (timers.Remove(timer))
        {
            ActiveTimers--;
        }
    }

    /// <summary>單次假 timer 的唯一 owner，釋放後計數回到基線。</summary>
    private sealed class OwnedTimer(AdvancingClock owner) : ITimer
    {
        /// <summary>期限到達時交由 BCL 延遲／取消來源的 callback。</summary>
        private TimerCallback? callback;
        /// <summary>只在 timer 活躍期間保留的 callback 狀態。</summary>
        private object? state;
        /// <summary>虛擬到期時間；觸發後設為 MaxValue，避免重複回呼。</summary>
        private DateTimeOffset dueAt;
        /// <summary>防止同步／非同步重複 Dispose 減少兩次計數。</summary>
        private bool disposed;
        /// <summary>建立一次性計時器；無限期限不會觸發 callback。</summary>
        /// <param name="timerOwner">本案例的唯一 timer 集合 owner。</param>
        /// <param name="timerCallback">期限到達時呼叫的委派。</param>
        /// <param name="timerState">傳回 callback 的狀態。</param>
        /// <param name="timerDueTime">虛擬到期時間。</param>
        public OwnedTimer(AdvancingClock timerOwner, TimerCallback timerCallback, object? timerState, TimeSpan timerDueTime) : this(timerOwner)
        {
            callback = timerCallback;
            state = timerState;
            dueAt = timerDueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : timerOwner.GetUtcNow() + timerDueTime;
        }

        /// <summary>虛擬時間到達期限時執行一次回呼；timer 仍由原呼叫端負責 Dispose。</summary>
        /// <param name="utcNow">目前的虛擬 UTC 時點。</param>
        public void FireIfDue(DateTimeOffset utcNow)
        {
            if (disposed || dueAt > utcNow || callback is null)
            {
                return;
            }

            dueAt = DateTimeOffset.MaxValue;
            callback(state);
        }

        /// <summary>測試只使用單次延遲，不接受改期。</summary>
        /// <param name="dueTime">不使用。</param><param name="period">不使用。</param><returns>不支援改期。</returns>
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        /// <summary>解除 timer owner，只釋放一次。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            callback = null;
            state = null;
            owner.Release(this);
        }
        /// <summary>非同步釋放與同步使用相同路徑。</summary><returns>已完成。</returns>
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
