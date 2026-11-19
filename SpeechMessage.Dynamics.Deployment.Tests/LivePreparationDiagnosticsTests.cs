/*
檔案整體說明
目的與定位：P7 Live 準備原因碼的離線故障注入測試，直接執行 CLI 準備路徑與安全診斷，
不連組織、不讀 User Secrets、不發 LINE。每案配置獨立 snapshot、合成身分與 metadata。
主要流程：逐項破壞 off 計畫、舊程式狀態、D34、情境、步驟唯一性與模式、聯絡人數、
metadata、活動參數／流程樣板及 factory 輸入，要求固定碼和情境識別，不准開始 seed。
決定性斷言：原因碼與真正故障關卡一致、業務寫入零、原始例外本文及秘密哨兵不輸出；
取消維持原 OperationCanceledException。數字 fault 與 seed alias 仍須穿過診斷包裝。
資源與隔離：JSON 均為 detached 值，writer 與取消來源由測試 Dispose；服務只有記憶體
資料且拒絕所有寫入，無連線、計時器或背景工作。測試通過不代表實機 Live 證據。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Dynamics.Deployment.Shadow;
using SpeechMessage.Dynamics.Deployment.Switching;
using SpeechMessage.Dynamics.Deployment.Live;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以真正準備流程故障驗固定碼，不能只 new 診斷例外自證碼表。</summary>
[Collection("Deployment working directory")]
public sealed class LivePreparationDiagnosticsTests
{
    /// <summary>
    /// 真CLI在來源、連線與快照三個不同邊界故障時須報不同碼；前兩項在租約前，
    /// 快照失敗則必須先歸還唯一lease再報告。固定來源複製既有合成P7 JSON，無真組織I/O。
    /// </summary>
    /// <param name="code">13來源、14快照、15連線、16本機資源路徑。</param>
    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void LivePreparation_CliBoundary_ReportsCodeAndReleasesLease(int code)
    {
        using var fixture = new Fixture();
        using var scope = fixture.EnterCliRepository();
        var catalogText = """{"schemaVersion":1,"organizations":[{"name":"goodshepherdback","kind":"test","host":"synthetic.invalid","switchState":"off","tierOverrides":[]}]}""";
        File.WriteAllText(Path.Combine(fixture.Root, "d365", "registration", "organizations.json"), catalogText);
        var catalog = OrganizationCatalog.Parse(catalogText);
        var folder = Path.Combine(LiveText.Get("AcceptanceFolder"), LiveText.Get("ScenarioFolder"), "localization");
        Directory.CreateDirectory(Path.Combine(fixture.Root, folder));
        if (code != 13)
        {
            var root = Root();
            foreach (var file in Directory.GetFiles(Path.Combine(root, folder), "*.json"))
                File.Copy(file, Path.Combine(fixture.Root, folder, Path.GetFileName(file)));
        }
        var blockedRoot = Path.Combine(fixture.Root, "blocked-artifact-root");
        if (code == 16) File.WriteAllText(blockedRoot, "synthetic");
        var connector = new BoundaryConnector(code == 15);
        using var writer = new StringWriter();
        Exception? reported = null;
        var exit = DeploymentCli.RunWithCatalog(["live-check", "--org", Fixture.Organization, "--module", "localization",
            "--scenario-root", Path.Combine(fixture.Root, folder)], catalog, connector, writer,
            failure => { Assert.Equal(connector.Connects, connector.Disposals); reported = failure; }, null, code == 16 ? blockedRoot : null);
        Assert.Equal(1, exit);
        Assert.Contains("LIVE-PREP-" + code.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SafetyService.Sentinel, writer.ToString(), StringComparison.Ordinal);
        Assert.NotNull(reported);
        Assert.IsNotType<LivePreparationFailure>(reported);
        Assert.Equal(code == 14 ? 1 : 0, connector.Disposals);
    }

    /// <summary>保留Live禁止Shadow能力的碼12；包裝不能吞原fault或因摘要注入增加終端行。</summary>
    [Fact]
    public void LivePreparation_ForbiddenCapability_Code12AndBoundedSafeSummary()
    {
        var original = new InvalidOperationException(SafetyService.Sentinel);
        var failure = Assert.Throws<LivePreparationFailure>(() =>
            LivePreparationFailure.Capture<int>(LivePreparationReason.ForbiddenShadowCapability, "care", "case\nfake",
                new string('a', 1000), "task", () => throw original, CancellationToken.None));
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        var text = writer.ToString();
        Assert.StartsWith("LIVE-PREP-12", text, StringComparison.Ordinal);
        Assert.Contains("case\\u000Afake", text, StringComparison.Ordinal);
        Assert.Contains(new string('a', 64) + "...", text, StringComparison.Ordinal);
        Assert.Single(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain(SafetyService.Sentinel, text, StringComparison.Ordinal);
        Assert.Same(original, DeploymentCli.ExceptionForReporting(failure));
    }

    /// <summary>內部逾時不是caller正常取消；必須保留原例外及固定原因，不能回130假稱使用者取消。</summary>
    [Fact]
    public void LivePreparation_InternalCancellation_IsFailureAndOriginalIsReported()
    {
        var original = new OperationCanceledException(SafetyService.Sentinel);
        var failure = Assert.Throws<LivePreparationFailure>(() =>
            LivePreparationFailure.Capture<int>(LivePreparationReason.Metadata, "care", "case-a", "record", "task",
                () => throw original, CancellationToken.None));
        Assert.Same(original, DeploymentCli.ExceptionForReporting(failure));
    }

    /// <summary>
    /// 每案只注入一個前置缺陷；若關卡移除、碼號錯置或開始業務寫入即失敗。
    /// metadata 故障特別含敏感本文，必須僅輸出數字碼與受審情境識別。
    /// </summary>
    /// <param name="code">不可重編的固定診斷碼。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void LivePreparation_RealGate_ReportsStableCodeBeforeAnyWrite(int code)
    {
        var service = new SafetyService();
        var snapshot = Snapshot();
        var file = Scenario();
        switch (code)
        {
            case 1: snapshot = snapshot with { Assemblies = [new("New.Assembly", "local", "other")] }; break;
            case 2: snapshot = snapshot with { Steps = [snapshot.Steps[0] with { Enabled = false }],
                Workflows = [new(Guid.NewGuid(), 1, "<Activity><Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>")],
                LegacyActivities = [new("Old.Assembly", "Old.Activity")] }; break;
            case 3: service.WrongIdentity = true; break;
            case 4: file = file with { LiveShadow = false }; break;
            case 5: file = file with { Entrypoint = "Unknown.Plugin" }; break;
            case 6: snapshot = snapshot with { Steps = [snapshot.Steps[0] with { Enabled = false }] }; break;
            case 7: file = file with { Seed = Json("""[{"alias":"church-a","entity":"account","fields":{}},{"alias":"record","entity":"task","fields":{}}]""") }; break;
            case 8: service.MetadataFault = true; break;
            case 9:
            case 10:
                file = file with { IsActivity = true, Entrypoint = "New.Activity",
                    Trigger = Json(code == 9 ? """{"inputs":{"synthetic":true}}""" : """{"inputs":{}}""") };
                snapshot = snapshot with { Activities = [new(new ActivityRegistration
                    { Module = "care", Type = "New.Activity", Live = new LiveRegistration() }, "New.Assembly")] };
                break;
            case 11: file = file with { Basis = "unsupported" }; break;
        }
        var failure = Invoke(snapshot, file, service);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        var text = writer.ToString();
        Assert.Contains("LIVE-PREP-" + code.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains("module=care", text, StringComparison.Ordinal);
        if (code >= 5) Assert.Contains("scenario=case-a", text, StringComparison.Ordinal);
        Assert.DoesNotContain(SafetyService.Sentinel, text, StringComparison.Ordinal);
        Assert.Equal(0, service.Writes);
        if (code == 8) Assert.Contains("0x80040265", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 流程讀取或解析錯誤必須有碼10與情境識別；注入SDK fault及不合法XML，不能洩露本文或寫seed。
    /// </summary>
    /// <param name="fault">true注入讀取fault，false注入不合法樣板XML。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LivePreparation_WorkflowReadOrParseFailure_IsCode10(bool fault)
    {
        var service = new SafetyService { WorkflowFault = fault, WorkflowXml = fault ? null : "<invalid" };
        var snapshot = ActivitySnapshot();
        var file = ActivityScenario();
        var failure = Invoke(snapshot, file, service);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.Contains("LIVE-PREP-10", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("scenario=case-a", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SafetyService.Sentinel, writer.ToString(), StringComparison.Ordinal);
        if (fault) Assert.Contains("0x80040265", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 離線鎖定流程狀態：停用樣板可供複製並抵達factory，啟用流程仍在碼02拒絕，
    /// 沒有「必須啟用樣板」的矛盾；不得為活動演練而放寬舊流程互斥。
    /// 故意以unsupported basis終止factory，確保不進runtime或寫seed。
    /// </summary>
    /// <param name="state">0為停用樣板，1為啟用流程。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LivePreparation_DisabledTemplateDoesNotConflictWithLegacyGate(int state)
    {
        const string xml = "<Activity><Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>";
        var snapshot = ActivitySnapshot() with { Steps = [], Workflows = [new(Guid.NewGuid(), state, xml)],
            LegacyActivities = [new("Old.Assembly", "Old.Activity")],
            Activities = [new(new ActivityRegistration { Module = "care", Type = "New.Activity", Live = new LiveRegistration(),
                Legacy = [new LegacyActivityReference { Assembly = "Old.Assembly", Type = "Old.Activity" }] }, "New.Assembly")] };
        var service = new SafetyService { WorkflowXml = xml, WorkflowState = state };
        var failure = Invoke(snapshot, ActivityScenario() with { Basis = "unsupported" }, service);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.Contains(state == 0 ? "LIVE-PREP-11" : "LIVE-PREP-02", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 以既存switch-log盤點的146個啟用平台流程／零啟用舊活動流程重放四模組，
    /// 平台流程不應誤觸舊流程互斥。使用真P7來源及合成binding／D34，故意在metadata注入fault，
    /// 決定性斷言須抵達碼08而非碼02，且零寫入；不能據此宣稱原情境整體準備或實機通過。
    /// </summary>
    /// <param name="module">之前只有匿名準備失敗而沒有比較結果的四個模組。</param>
    [Theory]
    [InlineData("localization")]
    [InlineData("appointments")]
    [InlineData("weeklyreports")]
    [InlineData("attendance")]
    public void LivePreparation_OfflineReplay_StandardActiveWorkflowsDoNotBlockModule(string module)
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), module, true));
        var snapshot = Snapshot();
        var registration = snapshot.Steps[0].Registration with { Module = module, Type = file.Entrypoint,
            Entity = file.Trigger.GetProperty("entity").GetString()!, Message = file.Trigger.GetProperty("message").GetString()! };
        snapshot = snapshot with { Steps = [snapshot.Steps[0] with { Registration = registration }],
            LegacyActivities = [new("Old.Assembly", "Old.Activity")],
            Workflows = Enumerable.Range(0, 146).Select(_ => new SwitchWorkflow(Guid.NewGuid(), 1, "<Activity />")).ToArray() };
        var service = new SafetyService { MetadataFault = true };
        var failure = Invoke(snapshot, file, service, module: module);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.Contains("LIVE-PREP-08", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("module=" + module, writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("scenario=" + file.Name, writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 週報的owner必須重用已核定WhoAmI共享參照，不可當作待建立的systemuser。
    /// 走真CLI準備入口與版控週報檔，最小metadata故意讓後續factory拒絕；斷言在此之前已讀
    /// 權威owner一次且零寫入。這只證明Live繫結接線，不宣稱週報metadata或實機通過。
    /// </summary>
    [Fact]
    public void LivePreparation_WeeklyOwner_UsesAuthoritativeSharedBindingBeforeFactory()
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "weeklyreports", true));
        var snapshot = Snapshot();
        snapshot = snapshot with { Steps = [snapshot.Steps[0] with { Registration = snapshot.Steps[0].Registration with
            { Module = "weeklyreports", Type = file.Entrypoint, Message = "Update", Entity = "new_group_present_weekly_report" } }] };
        var service = new SafetyService();
        var failure = Invoke(snapshot, file, service, module: "weeklyreports");
        Assert.Equal(1, service.WhoAmICalls);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.Contains("LIVE-PREP-11", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>兩個共享聯絡人及兩個步驟均須拒絕；防止把唯一性誤寫成僅非空檢查。</summary>
    /// <param name="duplicateContacts">true破壞聯絡人唯一性，false破壞註冊步驟唯一性。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LivePreparation_DuplicateContactsOrBindings_AreRejected(bool duplicateContacts)
    {
        var snapshot = Snapshot();
        var file = Scenario();
        if (duplicateContacts)
            file = file with { Seed = Json("""[{"alias":"church-a","entity":"account","fields":{}},{"alias":"contact-a","entity":"contact","fields":{}},{"alias":"contact-b","entity":"contact","fields":{}},{"alias":"record","entity":"task","fields":{}}]""") };
        else snapshot = snapshot with { Steps = [snapshot.Steps[0], snapshot.Steps[0] with { Id = Guid.NewGuid() }] };
        var service = new SafetyService();
        var failure = Invoke(snapshot, file, service);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.Contains(duplicateContacts ? "LIVE-PREP-07" : "LIVE-PREP-05", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>活動快照只提供合成新版活動，不依賴任何磁碟註冊或真實服務。</summary>
    private static SwitchSnapshot ActivitySnapshot() => Snapshot() with { Activities = [new(new ActivityRegistration
        { Module = "care", Type = "New.Activity", Live = new LiveRegistration() }, "New.Assembly")] };

    /// <summary>活動明列record與空輸入；內容故障由每個案例獨立注入。</summary>
    private static ShadowFileCase ActivityScenario() => Scenario() with { IsActivity = true, Entrypoint = "New.Activity",
        Trigger = Json("""{"inputs":{}}""") };

    /// <summary>取消不可變成準備失敗或觸發 notifier；原取消例外從反射包裝中原樣傳出。</summary>
    [Fact]
    public void LivePreparation_CallerCancellation_RemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = Invoke(Snapshot(), Scenario(), new SafetyService(), token: cancellation.Token);
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.DoesNotContain("LIVE-PREP", writer.ToString(), StringComparison.Ordinal);
    }

    /// <summary>共享 seed fault 診斷仍保留數字碼及 alias，控制字元不可插入偽造終端行。</summary>
    [Fact]
    public void LivePreparation_SeedFault_ReportsAliasAndNumericCodeWithoutBody()
    {
        var fault = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
            { ErrorCode = unchecked((int)0x80040265), Message = SafetyService.Sentinel, TraceText = SafetyService.Sentinel });
        var failure = new ShadowSeedCreateException("seed-a\nfake", "task", [], fault);
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);
        Assert.Contains("alias=seed-a\\u000Afake", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("0x80040265", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SafetyService.Sentinel, writer.ToString(), StringComparison.Ordinal);
    }

    /// <summary>反射只存取真正私有準備入口，不增產品測試專用分支；回傳內層供既有診斷列印。</summary>
    private static Exception Invoke(SwitchSnapshot snapshot, ShadowFileCase file, SafetyService service, string module = "care", CancellationToken token = default)
    {
        using var writer = new StringWriter();
        var method = typeof(DeploymentCli).GetMethod("RunPreparedLive", BindingFlags.NonPublic | BindingFlags.Static)!;
        var wrapper = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
            [snapshot.Organization, module, new[] { file }, Path.GetTempPath(), Path.GetTempPath(),
                service, snapshot, writer, (Func<string, string>)service.Secret, token]));
        return wrapper.InnerException!;
    }

    /// <summary>只提供單一可規劃的 Live 新步驟；組件與 ID 合成且資料不跨案例共用。</summary>
    private static SwitchSnapshot Snapshot() => new(
        new OrganizationDefinition("goodshepherdback", "test", "synthetic.invalid", "off"), Guid.NewGuid(),
        "manifest", "catalog", "fingerprint", [new("New.Assembly", "same", "same")],
        [new(Guid.NewGuid(), new StepRegistration { Module = "care", Type = "New.Plugin", Entity = "task",
            Message = "Update", Live = new StepLiveRegistration(), UnsecureConfig = """{"mode":"Shadow"}""" },
            true, """{"mode":"Live"}""", null)], [], [], [], []);

    /// <summary>三筆合成 seed 僅標識受審別名；不含顯示資料、秘密或外部 GUID。</summary>
    private static ShadowFileCase Scenario() => new("case-a", true,
        Json("""[{"alias":"church-a","entity":"account","fields":{}},{"alias":"contact-a","entity":"contact","fields":{}},{"alias":"record","entity":"task","fields":{}}]"""),
        Json("""{"target":"record","entity":"task","message":"Update","fields":{}}"""),
        Json("""[{"alias":"record","entity":"task","fields":["subject"]}]"""), [],
        Json("[]"), Json("[]"), Json("null"), "new", "New.Plugin", Json("{}"),
        Json("""{"alias":"church-a"}"""), "2026-11-19T00:00:00Z", Json("{}"), Json("[]"), false);

    /// <summary>文件立刻釋放，DTO 只保留 detached JSON，不保留解析 owner。</summary>
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }

    /// <summary>來源以本測試檔位置界定，避免臨時CWD讀到其他worktree或真實設定。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Directory.GetParent(Path.GetDirectoryName(file)!)!.FullName;

    /// <summary>獨占的合成租約，只計數成功建立者；連線失敗不產生需歸還的lease。</summary>
    private sealed class BoundaryConnector(bool fail) : IOrganizationConnector
    {
        internal int Connects { get; private set; }
        internal int Disposals { get; private set; }
        /// <summary>故障在服務建立前注入；成功時服務不持外部資源，release必須恰一次。</summary>
        public OrganizationLease Connect(string organization)
        {
            if (fail) throw new IOException(SafetyService.Sentinel);
            Connects++;
            return new OrganizationLease(new SafetyService(), () => Disposals++);
        }
    }

    /// <summary>唯讀合成安全與 metadata 能力；寫入一律計數並拒絕，沒有連線或可釋放外部資源。</summary>
    private sealed class SafetyService : IOrganizationService
    {
        internal const string Sentinel = "SENTINEL-PRIVATE-BODY";
        private readonly Guid church = Guid.NewGuid();
        private readonly Guid contact = Guid.NewGuid();
        private readonly byte[] key = new byte[64];
        internal bool WrongIdentity { get; set; }
        internal bool MetadataFault { get; set; }
        internal bool WorkflowFault { get; set; }
        internal string? WorkflowXml { get; set; }
        internal int WorkflowState { get; set; }
        internal int Writes { get; private set; }
        internal int WhoAmICalls { get; private set; }

        /// <summary>測試秘密只在案例內存；固定全零 key 僅供合成加解密，絕非部署設定。</summary>
        internal string Secret(string name) => name switch
        {
            "ShadowTestChurchId" => church.ToString("D"), "ShadowTestContactId" => contact.ToString("D"),
            "TestLineUserId" => "synthetic-recipient", "TokenKey" => Convert.ToBase64String(key),
            "TestChannelToken" => Sentinel, _ => throw new InvalidOperationException("Unexpected secret request.")
        };
        /// <summary>安全關卡只得到本案 contact/channel；活動樣板集合為空以注入找不到樣板。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var name = Assert.IsType<QueryExpression>(query).EntityName;
            return name switch
            {
                "contact" => new EntityCollection(new List<Entity> { new("contact", contact)
                    { ["parentcustomerid"] = new EntityReference("account", church) } }),
                "new_linechannel" => new EntityCollection(new List<Entity> { new("new_linechannel", Guid.NewGuid())
                    { ["new_accesstokenencrypted"] = ChannelTokenProtector.Protect(Sentinel, key) } }),
                "workflow" => WorkflowRows(), _ => throw new NotSupportedException()
            };
        }
        /// <summary>只回合成樣板中繼資料或故障，驗證真實metadata reader及XML parser的診斷界面。</summary>
        private EntityCollection WorkflowRows()
        {
            if (WorkflowFault) throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
                { ErrorCode = unchecked((int)0x80040265), Message = Sentinel, TraceText = Sentinel });
            return WorkflowXml is null ? new EntityCollection() : new EntityCollection(new List<Entity> { new("workflow", Guid.NewGuid())
                { ["primaryentity"] = "task", ["statecode"] = new OptionSetValue(WorkflowState), ["xaml"] = WorkflowXml } });
        }
        /// <summary>只允許權威共享教會讀取，不提供任何修改。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columns) => new(entityName, id);
        /// <summary>以正式 SDK response 提供權威身分及最小合法 metadata，故障只在受測 metadata 關卡。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is WhoAmIRequest)
            { WhoAmICalls++; return new WhoAmIResponse { Results = { ["UserId"] = Guid.NewGuid() } }; }
            if (request is RetrieveCurrentOrganizationRequest)
                return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail
                    { UniqueName = WrongIdentity ? "other" : "goodshepherdback", OrganizationId = Guid.NewGuid() } } };
            var metadataRequest = Assert.IsType<RetrieveEntityRequest>(request);
            if (MetadataFault) throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
                { ErrorCode = unchecked((int)0x80040265), Message = Sentinel, TraceText = Sentinel });
            var name = metadataRequest.LogicalName;
            var metadata = new EntityMetadata { LogicalName = name };
            typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.PrimaryIdAttribute))!.SetValue(metadata, name + "id");
            typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(metadata,
                new AttributeMetadata[] { new UniqueIdentifierAttributeMetadata { LogicalName = name + "id" } });
            return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = metadata } };
        }
        /// <summary>準備失敗必須零業務寫入，任何 Create 都使測試失敗。</summary>
        public Guid Create(Entity row) { Writes++; throw new NotSupportedException(); }
        /// <summary>準備不可更新任何列。</summary>
        public void Update(Entity row) { Writes++; throw new NotSupportedException(); }
        /// <summary>準備不可刪除任何列。</summary>
        public void Delete(string name, Guid id) { Writes++; throw new NotSupportedException(); }
        /// <summary>準備不可改關聯。</summary>
        public void Associate(string name, Guid id, Relationship relation, EntityReferenceCollection rows) { Writes++; throw new NotSupportedException(); }
        /// <summary>準備不可移除關聯。</summary>
        public void Disassociate(string name, Guid id, Relationship relation, EntityReferenceCollection rows) { Writes++; throw new NotSupportedException(); }
    }
}
