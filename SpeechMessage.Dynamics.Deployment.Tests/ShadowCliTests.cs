/*
 * 檔案整體說明
 *
 * 目的與定位：以真正 DeploymentCli 驗證 shadow 命令的完整離線接線，防止 reader、
 * factory 及 runner 各自通過卻無法由命令執行。所有組織、metadata、身分及追蹤皆為
 * 每案獨占的合成資料，不讀取 User Secrets、不連 D365、不發 LINE。
 * 主要功能與流程：暫存方案含合成 net462 DLL、固定清單及黃金情境，先由正式引擎
 * 部署，再由命令完成權威身分核對、seed、觸發、追蹤比較及報告。故障注入涵蓋 seed Create fault、
 * 錯誤身分、追蹤內容、觸發例外、外部路徑、寫鎖與取消；決定性斷言為 lease 歸還、原
 * trace 等級與停用狀態恢復、自建 task 零殘留，以及報告不洩漏秘密或實際 GUID。
 * 使用邊界與維護：非平行集合保護程序 cwd；fixture 先恢復 cwd，再刪除自己的唯一根，
 * writer、租約與檔案均確定釋放。替身只擴充所需的權威 metadata 與精確追蹤查詢，
 * 合成 shadow／switch 透過明確 internal 接縫使用本案 Temp 下的共享鎖，不爭用正式
 * ProgramData 組織鎖；另以跨 artifacts 根互斥及立即再取鎖驗證，不能以不取鎖假綠。
 * 外部觀察以合成既有列驗版本摘要及保留，分頁故障在觸發前後注入，不假稱全域稽核。
 * 不改既有通用假服務，也不把合成綠燈稱為實機驗收。
 * P7分級相容：非診斷合成候選補完整live區塊，避免新必填格式遮蔽原本故障注入；
 * 不更動既有斷言、期望值、假服務所有權或清理方式。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>由公開命令驗真正 SDK runtime，沒有可繞過生命週期的假 runner 注入。</summary>
[Collection("Deployment working directory")]
public sealed class ShadowCliTests
{
    /// <summary>
    /// 修訂 2 退役的實機舊基準必須在連線、追蹤設定、種子與 token 讀取前拒絕；
    /// 舊 runner 的離線故障清理測試仍保留，但公開 shadow 不得再進入該路徑。
    /// </summary>
    /// <param name="basis">修訂 1 的兩種退役基準。</param>
    [Theory]
    [InlineData("legacy-live")]
    [InlineData("legacy-live-only")]
    public void ShadowCli_Revision2_RetiredBasisRefusedBeforeConnect(string basis)
    {
        using var f = new CliFixture();
        var path = Path.Combine(f.ScenarioRoot, "expected.json");
        var expected = JsonNode.Parse(File.ReadAllText(path))!;
        expected["basis"] = basis;
        File.WriteAllText(path, expected.ToJsonString());
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run());
        Assert.Equal(0, f.Connects);
        Assert.Equal(0, f.Service.Triggers);
        Assert.Equal(0, f.TokenSecretReads);
        Assert.Equal(before, f.Core.Service.Writes.Count);
    }

    /// <summary>
    /// 真命令從已部署清單讀取新步驟映像綁定，不靠測試直接注入 factory。
    /// 合成平台自行持有 context；修訂 2 只觸發新輪，核對種子清理、設定還原與沒有例外告警。
    /// 舊映像故意使用不同 Name 與 EntityAlias，保護正式 context 必須依 alias 配對的契約。
    /// </summary>
    /// <param name="legacy">包含舊註冊來源時也只能跑新輪，舊映像不得導致舊步驟啟用。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowCli_BindsRegisteredImagesThroughCommand(bool legacy)
    {
        using var f = new CliFixture(images: true);
        if (legacy)
        {
            f.AddLegacyCase(withoutToken: true);
            f.Core.Service.Seed(new Entity("sdkmessageprocessingstepimage", Guid.NewGuid())
            {
                ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", f.Service.LegacyStep),
                ["name"] = "DifferentDisplayName", ["entityalias"] = "Image", ["imagetype"] = new OptionSetValue(1),
                ["attributes"] = "subject"
            });
        }
        Assert.Equal(0, f.Run());
        Assert.Equal(1, f.Service.Triggers);
        f.AssertRestored();
        Assert.Empty(f.Reports);
    }

    /// <summary>真 CLI 將期望推播接到完整比較；內容不同仍保存失敗報告，所有出口還原設定、停用及排空 seed。</summary>
    /// <param name="differentContent">僅改合成實際摘要，不改欄位快照，必須使比較失敗。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowCli_PushProjectionComparedThroughCommand(bool differentContent)
    {
        using var f = new CliFixture();
        var path = Path.Combine(f.ScenarioRoot, "expected.json");
        var expected = JsonNode.Parse(File.ReadAllText(path))!;
        expected["cases"]![0]!["lineCalls"]!.AsArray().Add(JsonNode.Parse("""{"method":"POST","endpoint":"/v2/bot/message/push","recipients":["contact-1"],"messageCount":1,"shadow":{"templateKey":"Attendance.Reply","retryKey":"task-1","contentSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}"""));
        File.WriteAllText(path, expected.ToJsonString());
        f.Service.PushIntent = true;
        f.Service.DifferentPushContent = differentContent;
        Assert.Equal(differentContent ? 1 : 0, f.Run());
        Assert.Equal(1, f.Service.Triggers);
        f.AssertRestored();
        Assert.Empty(f.Reports);
        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(f.Core.Root, "artifacts", "shadow"), "*.md", SearchOption.AllDirectories)));
        Assert.Contains(differentContent ? "Passed=false" : "Passed=true", report, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Service.Contact.ToString("D"), report, StringComparison.Ordinal);
    }

    /// <summary>公開第一輪在 D34、盤點、權威身分或基準不合時拒絕；不得以 reading 取代真舊輪或先寫 trace。</summary>
    /// <param name="fault">一次注入一種啟用前關卡失敗；所有案例皆須零新寫入及零 token 解析。</param>
    [Theory]
    [InlineData("d34")]
    [InlineData("hardcoded")]
    [InlineData("reading")]
    [InlineData("identity")]
    public void SwitchCli_LegacyPreconditionsRefuseBeforeWrites(string fault)
    {
        using var f = new CliFixture();
        f.AddLegacyCase();
        if (fault == "identity") f.WrongIdentity = true;
        if (fault == "d34") f.Core.Service.Seed(new Entity("contact", Guid.NewGuid()) { ["new_lineid"] = "other-synthetic-recipient" });
        var path = Path.Combine(f.Core.Root, "d365", "registration", "legacy-token-inventory.json");
        if (fault == "hardcoded") File.WriteAllText(path, File.ReadAllText(path).Replace("record-fields", "unconditional-hardcoded", StringComparison.Ordinal));
        path = Path.Combine(f.ScenarioRoot, "expected.json");
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run(["switch", "--org", Fixture.Organization, "--module", "localization", "--to", "legacy"]));
        Assert.Equal(before, f.Core.Service.Writes.Count);
        Assert.Empty(f.Service.RoundKinds);
        Assert.Equal(0, f.TokenSecretReads);
        Assert.Equal(f.Connects, f.Releases);
    }

    /// <summary>不論舊 token 盤點是否完整，修訂 2 的退役基準都須在連線及任何寫入前拒絕。</summary>
    /// <param name="fault">每案只破壞固定盤點的一個安全條件。</param>
    [Theory]
    [InlineData("missing-file")]
    [InlineData("missing-entry")]
    [InlineData("hardcoded")]
    [InlineData("unresolved")]
    [InlineData("wrong-assembly")]
    [InlineData("shared-target")]
    [InlineData("bad-field")]
    [InlineData("duplicate-entry")]
    public void ShadowCli_UnsafeLegacyInventoryRefusesBeforeWrites(string fault)
    {
        using var f = new CliFixture();
        f.AddLegacyCase();
        var path = Path.Combine(f.Core.Root, "d365", "registration", "legacy-token-inventory.json");
        var data = JsonNode.Parse(File.ReadAllText(path))!;
        if (fault == "missing-entry") data["entries"] = new JsonArray();
        if (fault == "hardcoded") data["entries"]![0]!["tokenPolicy"] = "unconditional-hardcoded";
        if (fault == "unresolved") data["entries"]![0]!["tokenPolicy"] = "unresolved";
        if (fault == "wrong-assembly") data["entries"]![0]!["assembly"] = "Vendor.Wrong";
        if (fault == "shared-target") data["entries"]![0]!["tokenFields"]![0]!["alias"] = "church-a";
        if (fault == "bad-field") data["entries"]![0]!["tokenFields"]![0]!["attribute"] = "score";
        if (fault == "duplicate-entry") data["entries"]!.AsArray().Add(data["entries"]![0]!.DeepClone());
        File.WriteAllText(path, data.ToJsonString());
        if (fault == "missing-file") File.Delete(path);
        var expectedPath = Path.Combine(f.ScenarioRoot, "expected.json");
        var expected = JsonNode.Parse(File.ReadAllText(expectedPath))!;
        expected["basis"] = "legacy-live-only";
        File.WriteAllText(expectedPath, expected.ToJsonString());
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run());
        Assert.Equal(before, f.Core.Service.Writes.Count);
        Assert.Empty(f.Service.RoundKinds);
        Assert.Equal(0, f.TokenSecretReads);
        Assert.Equal(f.Connects, f.Releases);
    }

    /// <summary>真正 CLI 必須將新列 Create 意圖以明確父 lookup 配對，不能要求預先知道新 GUID。</summary>
    [Fact]
    public void ShadowCli_RelatedCreateMatchesAliasAndCleansUnexpectedRealChildren()
    {
        using var f = new CliFixture();
        f.AddRelatedCase();
        f.Service.RelatedIntent = true;
        f.Service.RealChildren = 1;
        Assert.Equal(0, f.Run());
        f.AssertRestored();
        Assert.Empty(f.Reports);
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName == "region");
    }

    /// <summary>新列同輪 Create 後 Delete 仍須比較完整意圖；最終不存在不能使動態別名失效。</summary>
    [Fact]
    public void ShadowCli_RelatedCreateThenDeleteKeepsIntentIdentity()
    {
        using var f = new CliFixture();
        f.AddRelatedCase();
        f.Service.RelatedIntent = true;
        f.Service.DeleteRelatedIntent = true;
        var path = Path.Combine(f.ScenarioRoot, "expected.json");
        var expected = JsonNode.Parse(File.ReadAllText(path))!;
        expected["cases"]![0]!["writes"]!.AsArray().Add(JsonNode.Parse("""
            {"operation":"Delete","entity":"region","alias":"child-1","fields":{}}
            """));
        File.WriteAllText(path, expected.ToJsonString());
        Assert.Equal(0, f.Run());
        f.AssertRestored();
        Assert.Empty(f.Reports);
    }

    /// <summary>同 selector 的兩個新列不可任取第一筆；即使影子比較拒絕，真實子列仍須先於父列刪除。</summary>
    [Fact]
    public void ShadowCli_AmbiguousRelatedIntentsFailAfterCleaningAllOwnedChildren()
    {
        using var f = new CliFixture();
        f.AddRelatedCase();
        f.Service.RelatedIntent = true;
        f.Service.DuplicateRelatedIntent = true;
        f.Service.RealChildren = 2;
        Assert.Equal(1, f.Run());
        Assert.Equal(1, f.Service.Triggers);
        f.AssertRestored();
        Assert.IsType<InvalidDataException>(Assert.Single(f.Reports));
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName == "region");
    }

    /// <summary>trigger 送達後失敗或取消也要重新發現關聯子列，不能只清最初 seed 清冊。</summary>
    /// <param name="cancel">取消使用 caller token；例外保留原物件交共用告警。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowCli_RelatedChildrenCleanedAfterUncertainTrigger(bool cancel)
    {
        using var f = new CliFixture();
        using var cancellation = new CancellationTokenSource();
        f.AddRelatedCase();
        f.Service.RealChildren = 2;
        f.Service.FailTrigger = !cancel;
        if (cancel) f.Service.AfterTrigger = cancellation.Cancel;
        Assert.Equal(cancel ? 130 : 1, f.Run(cancellationToken: cancellation.Token));
        f.AssertRestored();
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName == "region");
        if (cancel) Assert.Empty(f.Reports);
        else Assert.Same(f.Service.Failure, Assert.Single(f.Reports));
    }

    /// <summary>舊輪用真 SDK runtime 讀回新列；同名但不同父 ID 的既有列必須保留，歧義只阻止發布不阻止清理。</summary>
    /// <param name="children">同一 lookup 的實際新列筆數，兩筆代表不可唯一配對。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ShadowSdk_LegacyRelatedRowsReadAndCleanByOwnedParentOnly(int children)
    {
        using var f = new CliFixture();
        f.Service.RealChildren = children;
        var unrelated = new Entity("region", Guid.NewGuid()) { ["score"] = 7, ["parentid"] = new EntityReference("task", Guid.NewGuid()) };
        f.Core.Service.Create(unrelated);
        var step = f.Core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").Id;
        var definition = new ShadowScenarioDefinition(ShadowTraceParser.Parse("SMV-SHADOW|1|{\"end\":true,\"count\":0}"),
            [new("region", "child-1", ["score", "parentid"]) { Related = new("parentid", "task-1") }], new HashSet<string>());
        var source = new ShadowSdkScenario([new("task-1", new Entity("task") { ["subject"] = "synthetic" })],
            new("Create", "task-1", new Entity("task")), [step], [step], [new StepRegistration().Type], [], false)
        {
            PrimaryIdAttributes = new Dictionary<string, string> { ["task"] = "activityid", ["region"] = "regionid" }
        };
        var runtime = new ShadowSdkRuntime(f.Service, f.Service.Organization, f.Service.Church, f.Service.Contact,
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, TimeProvider.System);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        if (children == 1)
        {
            var row = Assert.Single(round.ObserveLegacy(TimeSpan.FromSeconds(1), CancellationToken.None).Rows);
            Assert.Equal("child-1", round.Aliases[row.Id]);
            Assert.Equal(7, row.Fields.GetProperty("score").GetInt32());
        }
        else Assert.Throws<InvalidDataException>(() => round.ObserveLegacy(TimeSpan.FromSeconds(1), CancellationToken.None));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        Assert.Equal(unrelated.Id, Assert.Single(f.Core.Service.Rows.Where(row => row.LogicalName == "region")).Id);
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName == "task");
    }

    /// <summary>不完整或身分錯配的子列回應不得讓 owner 刪父後丟失查詢範圍；解除故障可重試清乾淨。</summary>
    /// <param name="fault">模擬 SDK 分頁、錯父 lookup、重複 ID 或子列刪除失敗。</param>
    [Theory]
    [InlineData("page")]
    [InlineData("wrong-parent")]
    [InlineData("duplicate-id")]
    [InlineData("delete-child")]
    public void ShadowSdk_RelatedCleanupFaultPreservesParentForRetry(string fault)
    {
        using var f = new CliFixture();
        f.Service.RealChildren = 1;
        var step = f.Core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").Id;
        var definition = new ShadowScenarioDefinition(ShadowTraceParser.Parse("SMV-SHADOW|1|{\"end\":true,\"count\":0}"),
            [new("region", "child-1", ["score"]) { Related = new("parentid", "task-1") }], new HashSet<string>());
        var source = new ShadowSdkScenario([new("task-1", new Entity("task") { ["subject"] = "synthetic" })],
            new("Create", "task-1", new Entity("task")), [step], [step], [new StepRegistration().Type], [], false)
        {
            PrimaryIdAttributes = new Dictionary<string, string> { ["task"] = "activityid", ["region"] = "regionid" }
        };
        var runtime = new ShadowSdkRuntime(f.Service, f.Service.Organization, f.Service.Church, f.Service.Contact,
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, TimeProvider.System);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        f.Service.RelatedReadFault = fault;
        Assert.Throws<AggregateException>(round.DeleteOwnedRecords);
        Assert.Single(f.Core.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(f.Core.Service.Rows.Where(row => row.LogicalName == "region"));
        Assert.Throws<InvalidDataException>(round.VerifyNoRemainingRecords);
        f.Service.RelatedReadFault = "";
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName is "task" or "region");
    }

    /// <summary>new 基準只執行新輪；兩次同日執行保留各自證據，且每次確定還原與清理。</summary>
    [Fact]
    public void ShadowCli_NewRoundRunsThroughSdkAndPreservesSameDayEvidence()
    {
        using var f = new CliFixture();
        Assert.Equal(0, f.Run());
        Assert.Equal(0, f.Run());
        f.AssertRestored();
        Assert.Equal(2, f.Connects);
        Assert.Equal(2, f.Releases);
        Assert.Equal(2, f.Service.Triggers);
        var reports = Directory.GetFiles(Path.Combine(f.Core.Root, "artifacts", "shadow"), "*.md", SearchOption.AllDirectories);
        Assert.Equal(2, reports.Length);
        Assert.All(reports, path =>
        {
            var text = File.ReadAllText(path);
            Assert.Contains("basis=new", text, StringComparison.Ordinal);
            Assert.Contains("Passed=true", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
            Assert.DoesNotContain(f.Service.Church.ToString("D"), text, StringComparison.Ordinal);
        });
        Assert.Empty(f.Reports);
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>新輪有額外意圖須回一並留失敗報告，不能以 observe 最終值相同掩蓋。</summary>
    [Fact]
    public void ShadowCli_DifferentIntentFailsAfterCleanup()
    {
        using var f = new CliFixture();
        f.Service.DifferentIntent = true;
        Assert.Equal(1, f.Run());
        f.AssertRestored();
        var path = Assert.Single(Directory.GetFiles(Path.Combine(f.Core.Root, "artifacts", "shadow"), "*.md", SearchOption.AllDirectories));
        Assert.Contains("Passed=false", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Empty(f.Reports);
    }

    /// <summary>觸發送達後失敗仍停用及清理，且共用告警必須在 lease 歸還後收到真實例外。</summary>
    [Fact]
    public void ShadowCli_TriggerFailureRestoresBeforeReporting()
    {
        using var f = new CliFixture();
        f.Service.FailTrigger = true;
        Assert.Equal(1, f.Run());
        f.AssertRestored();
        Assert.Same(f.Service.Failure, Assert.Single(f.Reports));
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 在記憶體 SDK 的真實 Create 邊界注入 Dataverse fault，經 ShadowSdkRuntime 包裝並走完整 CLI catch；
    /// 斷言 reporter 收到原始 fault、輸出只含 seed 身分與數字碼，且 trace／步驟／資料均清理。
    /// </summary>
    [Fact]
    public void ShadowCli_SeedCreateFaultFlowsThroughRuntimeAndReportsSafeContext()
    {
        using var f = new CliFixture();
        var original = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) },
            new FaultReason("Synthetic sensitive server message"));
        f.Service.CreateFailure = original;
        var scenarioPath = Path.Combine(f.ScenarioRoot, "scenarios.json");
        var scenarios = JsonNode.Parse(File.ReadAllText(scenarioPath))!;
        scenarios["cases"]![0]!["seed"]!.AsArray().Insert(2,
            JsonNode.Parse("""{"alias":"region-seed","entity":"region","fields":{"score":7}}"""));
        File.WriteAllText(scenarioPath, scenarios.ToJsonString());

        Assert.Equal(1, f.Run());

        Assert.Same(original, Assert.Single(f.Reports));
        Assert.Contains("Dataverse fault code[0]: 0x80040217", f.Output.ToString(), StringComparison.Ordinal);
        Assert.Contains("alias=region-seed; entity=region; referenceAliases=(none)", f.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic sensitive server message", f.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, f.Service.Triggers);
        f.AssertRestored();
    }

    /// <summary>
    /// 真實 seed Create 已落地後擲 fault 並讓第一輪 Delete 失敗，驗證 runner 聚合後的 CLI
    /// reporter 保留原 AggregateException，診斷仍指出 seed alias／fault code，測試 owner 最後清理唯一合成殘留。
    /// </summary>
    [Fact]
    public void ShadowCli_SeedFaultAndCleanupFaultReportsAggregateAndSafeContext()
    {
        using var f = new CliFixture();
        var original = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) },
            new FaultReason("Synthetic sensitive server message"));
        f.Service.CreateFailure = original;
        f.Service.CreateFailureAfterWrite = true;
        f.Service.FailSeedDelete = true;
        var scenarioPath = Path.Combine(f.ScenarioRoot, "scenarios.json");
        var scenarios = JsonNode.Parse(File.ReadAllText(scenarioPath))!;
        scenarios["cases"]![0]!["seed"]!.AsArray().Insert(2,
            JsonNode.Parse("""{"alias":"region-seed","entity":"region","fields":{"score":7}}"""));
        File.WriteAllText(scenarioPath, scenarios.ToJsonString());

        try
        {
            Assert.Equal(1, f.Run());
            var aggregate = Assert.IsType<AggregateException>(Assert.Single(f.Reports));
            Assert.Contains(aggregate.Flatten().InnerExceptions,
                failure => failure is ShadowSeedCreateException seed && ReferenceEquals(seed.InnerException, original));
            Assert.Contains("Dataverse fault code[0]: 0x80040217", f.Output.ToString(), StringComparison.Ordinal);
            Assert.Contains("alias=region-seed; entity=region; referenceAliases=(none)",
                f.Output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic sensitive server message", f.Output.ToString(), StringComparison.Ordinal);

            var leftover = Assert.Single(f.Core.Service.Rows.Where(row => row.LogicalName == "region"));
            f.Core.Service.Delete("region", leftover.Id);
            f.AssertRestored();
        }
        finally
        {
            foreach (var row in f.Core.Service.Rows.Where(row => row.LogicalName == "region").ToArray())
                f.Core.Service.Delete("region", row.Id);
        }
    }

    /// <summary>SDK 送達後 caller 取消仍須停用及刪除，清理查詢不得沿用已取消的 token。</summary>
    /// <param name="synchronous">以同步 request 或非同步 CRUD 入口送達。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowCli_CancellationAfterTriggerRestoresWithoutNotification(bool synchronous)
    {
        using var f = new CliFixture(synchronous);
        using var cancellation = new CancellationTokenSource();
        f.Service.AfterTrigger = cancellation.Cancel;
        Assert.Equal(130, f.Run(cancellationToken: cancellation.Token));
        f.AssertRestored();
        Assert.Empty(f.Reports);
    }

    /// <summary>部署或基準不合時在 trace／seed 第一筆寫入前失敗，不能靜默只跑新輪。</summary>
    /// <param name="fault">各案只改變一種準備階段的核對項目。</param>
    [Theory]
    [InlineData("configuration")]
    [InlineData("registration-image")]
    [InlineData("legacy-live-only")]
    [InlineData("no-live-case")]
    public void ShadowCli_UnsupportedOrDriftedStateHasNoWrites(string fault)
    {
        using var f = new CliFixture();
        if (fault == "configuration")
            f.Core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep")["configuration"] = "{\"mode\":\"Live\"}";
        if (fault == "registration-image")
            f.Core.Service.Seed(new Entity("sdkmessageprocessingstepimage", Guid.NewGuid())
            {
                ["sdkmessageprocessingstepid"] = f.Core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").ToEntityReference(),
                ["name"] = "UnexpectedImage"
            });
        if (fault == "legacy-live-only")
        {
            var path = Path.Combine(f.ScenarioRoot, "expected.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"basis\":\"new\"", "\"basis\":\"legacy-live-only\"", StringComparison.Ordinal));
        }
        if (fault == "no-live-case")
        {
            var path = Path.Combine(f.ScenarioRoot, "scenarios.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"liveShadow\":true", "\"liveShadow\":false", StringComparison.Ordinal));
        }
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run());
        Assert.Equal(before, f.Core.Service.Writes.Count);
        Assert.Equal(0, f.Service.Triggers);
        Assert.Equal(f.Connects, f.Releases);
    }

    /// <summary>秘密中的 ID 只是候選；與服務端關聯不合時不得寫 trace、seed 或步驟。</summary>
    [Fact]
    public void ShadowCli_WrongConfiguredIdentityHasNoWrites()
    {
        using var f = new CliFixture();
        f.WrongIdentity = true;
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run());
        Assert.Equal(before, f.Core.Service.Writes.Count);
        Assert.Equal(1, f.Connects);
        Assert.Equal(1, f.Releases);
    }

    /// <summary>固定來源邊界、組織、參數及 caller 取消必須在連線前拒絕。</summary>
    /// <param name="fault">只注入一種命令前置失敗。</param>
    [Theory]
    [InlineData("path")]
    [InlineData("organization")]
    [InlineData("unknown-option")]
    [InlineData("duplicate-option")]
    [InlineData("cancelled")]
    public void ShadowCli_InvalidInputRefusesBeforeConnect(string fault)
    {
        using var f = new CliFixture();
        var args = f.Arguments.ToList();
        if (fault == "path") args[^1] = f.Core.Root;
        if (fault == "organization") args[2] = "production";
        if (fault == "unknown-option") args.AddRange(["--token", Fixture.SecretValue]);
        if (fault == "duplicate-option") args.AddRange(["--module", "localization"]);
        using var cancellation = new CancellationTokenSource();
        if (fault == "cancelled") cancellation.Cancel();
        Assert.Equal(fault == "cancelled" ? 130 : 2, f.Run(args.ToArray(), cancellation.Token));
        Assert.Equal(0, f.Connects);
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>shadow 與 apply 共用組織鎖；取鎖逾時前不能建立第二個 SDK lease。</summary>
    [Fact]
    public void ShadowCli_UsesExistingOrganizationWriteLock()
    {
        using var f = new CliFixture();
        using var held = OrganizationWriteLock.Acquire(Path.Combine(f.ArtifactRoot, Fixture.Organization), TimeSpan.Zero);
        Assert.Equal(1, f.Run());
        Assert.Equal(0, f.Connects);
        Assert.IsType<TimeoutException>(Assert.Single(f.Reports));
    }

    /// <summary>
    /// 合成入口使用本案獨立共享根；即使其他案例的同名組織鎖正被持有，仍可完整執行，
    /// 且產生真正的合成共享鎖檔，不能靠略過共享鎖取得假綠。所有 handle 由 using 排空。
    /// </summary>
    [Fact]
    public void ShadowCli_SyntheticLockRootIsSeparateAndStillUsesSharedLock()
    {
        using var f = new CliFixture();
        using var other = OrganizationWriteLock.Acquire(
            Path.Combine(f.Core.Root, "other", Fixture.Organization), TimeSpan.Zero,
            Path.Combine(f.Core.Root, "other-shared"));
        Assert.Equal(0, f.RunIsolated());
        Assert.True(File.Exists(Path.Combine(f.SyntheticSharedRoot, Fixture.Organization + ".lock")));
        f.AssertRestored();
    }

    /// <summary>
    /// 同一合成共享根仍跨不同 artifacts 根互斥；逾時不得 Connect，釋放後須可重試成功，
    /// 並立即重新獨占取得兩把鎖，證明失敗及成功出口均不依 GC 清理。
    /// </summary>
    [Fact]
    public void ShadowCli_SyntheticSharedContentionFailsBeforeConnectThenDrains()
    {
        using var f = new CliFixture();
        using (OrganizationWriteLock.Acquire(Path.Combine(f.Core.Root, "other", Fixture.Organization),
            TimeSpan.Zero, f.SyntheticSharedRoot))
        {
            Assert.Equal(1, f.RunIsolated());
            Assert.Equal(0, f.Connects);
            Assert.IsType<TimeoutException>(Assert.Single(f.Reports));
        }
        f.Reports.Clear();
        Assert.Equal(0, f.RunIsolated());
        f.AssertRestored();
        using var probe = OrganizationWriteLock.Acquire(Path.Combine(f.ArtifactRoot, Fixture.Organization),
            TimeSpan.Zero, f.SyntheticSharedRoot);
        Assert.Empty(f.Reports);
    }

    /// <summary>合成輸出根必須是 Temp 真子樹；相對路徑、Temp 本身及 ProgramData 均在副作用前拒絕。</summary>
    /// <param name="invalidScope">每案只有一個路徑邊界故障，不建立測試擁有範圍外的檔案。</param>
    [Theory]
    [InlineData("relative")]
    [InlineData("temp")]
    [InlineData("programdata")]
    public void ShadowCli_SyntheticScopeOutsideTempFailsBeforeSideEffects(string invalidScope)
    {
        using var f = new CliFixture();
        var root = invalidScope switch
        {
            "relative" => "relative-artifacts",
            "temp" => Path.GetTempPath(),
            _ => DeploymentCli.GetSharedOrganizationLockDirectory()
        };
        Assert.Equal(2, f.RunIsolated(artifactRoot: root));
        Assert.Equal(0, f.Connects);
        Assert.Empty(f.Reports);
        Assert.False(Directory.Exists(f.SyntheticSharedRoot));
    }

    /// <summary>
    /// 真正產品 connector 即使搭配合法 Temp 根也不可選用合成接縫；設定為空且工廠故意擲錯，
    /// 決定性斷言是固定拒絕、零工廠呼叫及零合成鎖目錄，沒有 User Secrets 或網路。
    /// </summary>
    [Fact]
    public void ShadowCli_SyntheticSeamRefusesProductConnectorBeforeAnyClient()
    {
        using var f = new CliFixture();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        using var configurationOwner = (IDisposable)configuration;
        var clientCreates = 0;
        var connector = new UserSecretsOrganizationConnector(configuration, (_, _, _) =>
        {
            clientCreates++;
            throw new InvalidOperationException("No real client may be constructed.");
        });
        Assert.Equal(2, DeploymentCli.RunSyntheticShadow(f.Arguments, connector, f.Output,
            f.Reports.Add, f.ArtifactRoot, writeLockTimeout: TimeSpan.Zero));
        Assert.Equal(0, clientCreates);
        Assert.Empty(f.Reports);
        Assert.False(Directory.Exists(f.SyntheticSharedRoot));
    }

    /// <summary>正式 CLI 沒有可注入共享鎖的選項；未知旗標在鎖／連線之前拒絕，不能透過 argv 放寬。</summary>
    [Fact]
    public void ShadowCli_ProductArgumentsCannotSelectSyntheticLockDirectory()
    {
        using var f = new CliFixture();
        var args = f.Arguments.Concat(["--shared-lock-directory", f.SyntheticSharedRoot]).ToArray();
        Assert.Equal(2, DeploymentCli.Run(args, f, f.Output, f.Reports.Add, f.ArtifactRoot,
            writeLockTimeout: TimeSpan.Zero));
        Assert.Equal(0, f.Connects);
        Assert.Empty(f.Reports);
        Assert.False(Directory.Exists(f.SyntheticSharedRoot));
    }

    /// <summary>同步步驟沒有 asyncoperation；必須用本次請求 ID、正式 step、型別及時間窗找唯一追蹤。</summary>
    [Fact]
    public void ShadowCli_SynchronousRoundUsesExactRequestTraceWithoutAsyncOperation()
    {
        using var f = new CliFixture(synchronous: true);
        Assert.Equal(0, f.Run());
        Assert.NotEqual(Guid.Empty, f.Service.RequestId);
        Assert.Equal(0, f.Service.AsyncQueries);
        Assert.Equal(1, f.Service.Triggers);
        f.AssertRestored();
    }

    /// <summary>同步追蹤歧義不能取第一筆；請求已建立的記錄仍須由 owner 排空。</summary>
    [Fact]
    public void ShadowCli_DuplicateSynchronousTraceFailsClosedAndRestores()
    {
        using var f = new CliFixture(synchronous: true);
        f.Service.DuplicateTrace = true;
        Assert.Equal(1, f.Run());
        Assert.Equal(1, f.Service.Triggers);
        Assert.Equal(0, f.Service.AsyncQueries);
        Assert.IsType<InvalidDataException>(Assert.Single(f.Reports));
        f.AssertRestored();
    }

    /// <summary>單案例擁有合成方案、cwd、服務及 writer；報告回呼同時驗 lease 先釋放。</summary>
    private sealed class CliFixture : IOrganizationConnector, IDisposable
    {
        private readonly string previousDirectory = Directory.GetCurrentDirectory();
        internal Fixture Core { get; } = new();
        internal TestService Service { get; }
        internal StringWriter Output { get; } = new();
        internal List<Exception> Reports { get; } = [];
        internal int Connects;
        internal int Releases;
        internal bool WrongIdentity;
        internal int TokenSecretReads;
        internal string ArtifactRoot => Path.Combine(Core.Root, "artifacts", "d365");
        /// <summary>只屬本案 Temp 根的固定共享鎖位置，不接觸真正 ProgramData 組織鎖。</summary>
        internal string SyntheticSharedRoot => Path.Combine(ArtifactRoot, ".synthetic-shared-locks");
        internal string ScenarioRoot => Path.Combine(Core.Root, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", "localization");
        internal string[] Arguments => ["shadow", "--org", Fixture.Organization, "--module", "localization", "--scenario-root", ScenarioRoot];

        /// <summary>使用實際 loader、PE reader 及引擎建立可核對的離線部署，不注入預設成功的驗證。</summary>
        internal CliFixture(bool synchronous = false, bool images = false)
        {
            Service = new TestService(Core) { Synchronous = synchronous };
            var step = Core.Manifest.Steps[0] with { Live = new StepLiveRegistration(), Module = "localization", UnsecureConfig = "{\"mode\":\"Shadow\"}",
                Mode = synchronous ? "Synchronous" : "Asynchronous",
                Images = images ? [new RegistrationImage { Name = "Image", Type = "PostImage", Attributes = ["subject"] }] : [] };
            var manifest = Core.Manifest with { Steps = [step] };
            var registration = Path.Combine(Core.Root, "d365", "registration");
            var plugins = Path.Combine(Core.Root, "artifacts", "plugins");
            Directory.CreateDirectory(registration);
            Directory.CreateDirectory(plugins);
            Directory.CreateDirectory(ScenarioRoot);
            File.WriteAllText(Path.Combine(Core.Root, "SpeechMessageProducts.sln"), "");
            File.WriteAllText(Path.Combine(registration, "plugins.registration.json"),
                JsonSerializer.Serialize(new RegistrationDocument { Assemblies = [manifest] }, RegistrationManifestLoader.Json));
            var dll = Path.Combine(plugins, manifest.AssemblyName + ".dll");
            File.Copy(Core.Emit(), dll);
            var image = AssemblyCatalog.Read(dll, manifest);
            Core.Engine.Apply(Core.Engine.Plan(manifest, image, Fixture.Organization), manifest, image);
            File.WriteAllText(Path.Combine(ScenarioRoot, "scenarios.json"), """
                {"module":"localization","cases":[{"name":"one","church":{"alias":"church-a","organizationName":"goodshepherdback","timeZone":"Asia/Taipei","culture":"zh-TW"},"seed":[{"alias":"church-a","entity":"account","fields":{}},{"alias":"contact-1","entity":"contact","fields":{}},{"alias":"task-1","entity":"task","fields":{"subject":"synthetic"}}],"trigger":{"project":"Synthetic","type":"SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin","entity":"task","message":"Create","target":"task-1","fields":{},"depth":1},"now":"2026-11-14T00:00:00Z","volatile":[],"liveShadow":true,"observe":[{"alias":"task-1","entity":"task","fields":["subject"]}]}]}
                """);
            File.WriteAllText(Path.Combine(ScenarioRoot, "expected.json"), """
                {"module":"localization","basis":"new","cases":[{"name":"one","entrypoint":"SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin","writes":[],"lineCalls":[],"outputs":{},"exception":null}],"declaredDifferences":[]}
                """);
            if (images)
            {
                var scenarioPath = Path.Combine(ScenarioRoot, "scenarios.json");
                var scenarios = JsonNode.Parse(File.ReadAllText(scenarioPath))!;
                scenarios["cases"]![0]!["trigger"]!["images"] = JsonNode.Parse("""{"post":{"Image":{"subject":"synthetic"}}}""");
                File.WriteAllText(scenarioPath, scenarios.ToJsonString());
            }
            Directory.SetCurrentDirectory(Core.Root);
        }

        /// <summary>合成關聯表不出現在 seed，驗證 CLI 確實從 observe 載入 metadata；子列只依本輪 task lookup 配對。</summary>
        internal void AddRelatedCase()
        {
            var scenarioPath = Path.Combine(ScenarioRoot, "scenarios.json");
            var scenarios = JsonNode.Parse(File.ReadAllText(scenarioPath))!;
            scenarios["cases"]![0]!["observe"]!.AsArray().Add(JsonNode.Parse("""
                {"alias":"child-1","entity":"region","fields":["score","parentid"],"related":{"attribute":"parentid","alias":"task-1"}}
                """));
            File.WriteAllText(scenarioPath, scenarios.ToJsonString());
            var expectedPath = Path.Combine(ScenarioRoot, "expected.json");
            var expected = JsonNode.Parse(File.ReadAllText(expectedPath))!;
            expected["cases"]![0]!["writes"]!.AsArray().Add(JsonNode.Parse("""
                {"operation":"Create","entity":"region","alias":"child-1","fields":{"score":7,"parentid":{"ref":"task-1","entity":"task"}}}
                """));
            File.WriteAllText(expectedPath, expected.ToJsonString());
        }

        /// <summary>建立閱讀對照的合成舊註冊來源；盤點只保留測試素材，不授權啟用已退役入口。</summary>
        internal void AddLegacyCase(bool withoutToken = false)
        {
            var oldAssembly = new Entity("pluginassembly", Guid.NewGuid()) { ["name"] = "Vendor.Legacy", ["version"] = "1.0.0.0", ["isolationmode"] = new OptionSetValue(2) };
            var oldType = new Entity("plugintype", Guid.NewGuid()) { ["typename"] = "Vendor.Legacy.Plugin", ["pluginassemblyid"] = oldAssembly.ToEntityReference(), ["isworkflowactivity"] = false };
            var oldStep = new Entity("sdkmessageprocessingstep", Guid.NewGuid())
            {
                ["eventhandler"] = oldType.ToEntityReference(), ["name"] = "Synthetic legacy",
                ["sdkmessageid"] = Core.Service.Rows.Single(row => row.LogicalName == "sdkmessage").ToEntityReference(),
                ["sdkmessagefilterid"] = Core.Service.Rows.Single(row => row.LogicalName == "sdkmessagefilter").ToEntityReference(),
                ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2),
                ["stage"] = new OptionSetValue(40), ["mode"] = new OptionSetValue(1), ["rank"] = 1
            };
            Core.Service.Seed(oldAssembly); Core.Service.Seed(oldType); Core.Service.Seed(oldStep);
            var manifestPath = Path.Combine(Core.Root, "d365", "registration", "plugins.registration.json");
            var registration = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            registration["assemblies"]![0]!["steps"]![0]!["legacy"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new
            { assembly = "Vendor.Legacy", type = "Vendor.Legacy.Plugin", stepId = oldStep.Id.ToString("D") })));
            File.WriteAllText(manifestPath, registration.ToJsonString());
            var scenarioPath = Path.Combine(ScenarioRoot, "scenarios.json");
            var scenarios = JsonNode.Parse(File.ReadAllText(scenarioPath))!;
            scenarios["cases"]![0]!["trigger"]!["type"] = "Vendor.Legacy.Plugin";
            scenarios["cases"]![0]!["seed"]!.AsArray().Insert(2,
                JsonNode.Parse("""{"alias":"token-config","entity":"region","fields":{"score":1}}"""));
            File.WriteAllText(scenarioPath, scenarios.ToJsonString());
            var expectedPath = Path.Combine(ScenarioRoot, "expected.json");
            var expected = JsonNode.Parse(File.ReadAllText(expectedPath))!;
            expected["basis"] = "reading";
            expected["cases"]![0]!["entrypoint"] = "Vendor.Legacy.Plugin";
            File.WriteAllText(expectedPath, expected.ToJsonString());
            Directory.CreateDirectory(Path.Combine(Core.Root, "wp-reports"));
            File.WriteAllText(Path.Combine(Core.Root, "wp-reports", "synthetic-inventory.md"), "Synthetic source review only.");
            File.WriteAllText(Path.Combine(Core.Root, "d365", "registration", "legacy-token-inventory.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    entries = new[] { new { module = "localization", assembly = "Vendor.Legacy", type = "Vendor.Legacy.Plugin",
                        tokenPolicy = withoutToken ? "none" : "record-fields", evidence = "wp-reports/synthetic-inventory.md",
                        externalWrites = Array.Empty<object>(),
                        tokenFields = withoutToken ? Array.Empty<object>() :
                            new object[] { new { alias = "token-config", entity = "region", attribute = "new_channelaccesstoken" } } } }
                }));
            Service.LegacyStep = oldStep.Id;
            Service.TokenRequired = !withoutToken;
        }

        /// <summary>經明確合成接縫執行同一正式授權及命令流程；秘密僅是當案常值，不接觸真鎖。</summary>
        internal int Run(string[]? args = null, CancellationToken cancellationToken = default) =>
            RunIsolated(args, cancellationToken: cancellationToken);

        /// <summary>
        /// 以反射要求明確的合成入口，缺實作時以執行期斷言形成 RED；並非編譯錯誤。
        /// reporter 同時斷言連線已歸還，原例外保留供原有契約驗證，不產生背景資源。
        /// </summary>
        /// <param name="args">當案合成命令，缺省為固定 shadow。</param>
        /// <param name="artifactRoot">故障注入用輸出根，合法預設限本案 Temp。</param>
        /// <param name="cancellationToken">caller 擁有的取消訊號，固定最後一個參數。</param>
        /// <returns>正式共用授權與命令執行的結束碼。</returns>
        internal int RunIsolated(string[]? args = null, string? artifactRoot = null, CancellationToken cancellationToken = default)
        {
            var method = typeof(DeploymentCli).GetMethod("RunSyntheticShadow",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);
            Action<Exception> reporter = failure => { Assert.Equal(Connects, Releases); Reports.Add(failure); };
            Func<string, string> reader = key => key switch
            {
                "ShadowTestChurchId" => (WrongIdentity ? Guid.NewGuid() : Service.Church).ToString("D"),
                "ShadowTestContactId" => Service.Contact.ToString("D"),
                "TestLineUserId" => "synthetic-recipient",
                "TestChannelToken" => ReadTestToken(),
                _ => throw new InvalidOperationException("Unexpected secret request.")
            };
            return Assert.IsType<int>(method.Invoke(null,
                [args ?? Arguments, this, Output, reporter, artifactRoot ?? ArtifactRoot, reader, TimeSpan.Zero, cancellationToken]));
        }

        /// <summary>只計數真命令在受控舊輪解析合成秘密，避免新輪或前置拒絕也取得 token。</summary>
        private string ReadTestToken() { TokenSecretReads++; return Fixture.SecretValue; }

        /// <summary>合成 lease 由 CLI using 歸還；服務本身由 fixture 持有，不跨案例共用。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal(Fixture.Organization, organization);
            Connects++;
            return new OrganizationLease(Service, () => Releases++);
        }

        /// <summary>檢查正式 runtime 的自建清冊已排空，且沒有寫入共享教會與會友。</summary>
        internal void AssertRestored()
        {
            Assert.DoesNotContain(Core.Service.Rows, row => row.LogicalName == "task");
            Assert.Equal(1, Core.Service.Rows.Single(row => row.Id == Service.Organization).GetAttributeValue<OptionSetValue>("plugintracelogsetting").Value);
            Assert.All(Core.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"),
                row => Assert.Equal(1, row.GetAttributeValue<OptionSetValue>("statecode").Value));
            Assert.Equal(Connects, Releases);
            Assert.Equal("shared", Core.Service.Rows.Single(row => row.Id == Service.Church).GetAttributeValue<string>("name"));
        }

        /// <summary>即使斷言失敗仍先還原 cwd，再確定釋放案例的 writer 與唯一暫存根。</summary>
        public void Dispose()
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Output.Dispose();
            Core.Dispose();
        }
    }

    /// <summary>只補權威身分、metadata 與有界追蹤的獨占替身，不持有網路或背景資源。</summary>
    private sealed class TestService : IOrganizationService
    {
        private readonly Fixture core;
        private readonly Guid correlation = Guid.NewGuid();
        internal Guid Organization { get; } = Guid.NewGuid();
        internal Guid Church { get; } = Guid.NewGuid();
        internal Guid Contact { get; } = Guid.NewGuid();
        internal Exception Failure { get; } = new InvalidOperationException(Fixture.SecretValue);
        internal Exception? CreateFailure;
        internal bool CreateFailureAfterWrite;
        internal bool FailSeedDelete;
        internal bool DifferentIntent;
        internal bool PushIntent;
        internal bool DifferentPushContent;
        internal bool FailTrigger;
        internal Action? AfterTrigger;
        internal int Triggers;
        internal bool Synchronous;
        internal bool DuplicateTrace;
        internal bool RelatedIntent;
        internal bool DuplicateRelatedIntent;
        internal bool DeleteRelatedIntent;
        internal int RealChildren;
        internal string RelatedReadFault = "";
        internal Guid LegacyStep;
        internal bool TokenRequired;
        internal readonly List<bool> RoundKinds = [];
        internal readonly List<Guid> TriggerTargets = [];
        internal Guid RequestId;
        internal int AsyncQueries;
        internal int ExternalFaultRead { get; set; }
        private int externalReads;
        private Guid target;

        /// <summary>權威種子由測試安排提供；正式 runtime 不得建立或刪除這些共享列。</summary>
        internal TestService(Fixture core)
        {
            this.core = core;
            core.Service.Seed(new Entity("organization", Organization) { ["plugintracelogsetting"] = new OptionSetValue(1) });
            core.Service.Seed(new Entity("account", Church) { ["name"] = "shared" });
            core.Service.Seed(new Entity("contact", Contact) { ["new_lineid"] = "synthetic-recipient", ["parentcustomerid"] = new EntityReference("account", Church) });
        }

        /// <summary>提供正式 SDK response 型別；未知訊息交既有替身明確白名單。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is CreateRequest create)
            {
                Assert.True(Synchronous);
                RequestId = create.RequestId ?? Guid.Empty;
                Assert.NotEqual(Guid.Empty, RequestId);
                return new CreateResponse { Results = { ["id"] = Create(create.Target) } };
            }
            if (request is RetrieveCurrentOrganizationRequest)
                return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail { OrganizationId = Organization, UniqueName = Fixture.Organization } } };
            if (request is not RetrieveEntityRequest metadata) return core.Service.Execute(request);
            var primary = metadata.LogicalName switch { "task" => "activityid", "account" => "accountid", "contact" => "contactid", "region" => "regionid", _ => throw new InvalidOperationException() };
            var entity = new EntityMetadata { LogicalName = metadata.LogicalName };
            var subject = new StringAttributeMetadata { LogicalName = "subject", MaxLength = 100 };
            subject.GetType().GetProperty("IsValidForCreate")!.SetValue(subject, true);
            subject.GetType().GetProperty("IsValidForUpdate")!.SetValue(subject, true);
            entity.GetType().GetProperty("PrimaryIdAttribute")!.SetValue(entity, primary);
            var parent = new LookupAttributeMetadata { LogicalName = "parentid", Targets = ["task"] };
            var score = new IntegerAttributeMetadata { LogicalName = "score", MinValue = 0, MaxValue = 100 };
            var token = new StringAttributeMetadata { LogicalName = "new_channelaccesstoken", MaxLength = 2048 };
            var church = new LookupAttributeMetadata { LogicalName = "new_church", Targets = ["account"] };
            foreach (var field in new AttributeMetadata[] { parent, score, token })
            {
                field.GetType().GetProperty("IsValidForCreate")!.SetValue(field, true);
                field.GetType().GetProperty("IsValidForUpdate")!.SetValue(field, true);
            }
            entity.GetType().GetProperty("Attributes")!.SetValue(entity, new AttributeMetadata[] { new UniqueIdentifierAttributeMetadata { LogicalName = primary }, subject, parent, score, token, church,
                new BigIntAttributeMetadata { LogicalName = "versionnumber" } });
            return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = entity } };
        }

        /// <summary>真正呼叫通用服務建列，補官方活動主鍵，故障在送達後注入以驗清理清冊。</summary>
        public Guid Create(Entity entity)
        {
            if (CreateFailure is not null && entity.LogicalName == "region" && !CreateFailureAfterWrite) throw CreateFailure;
            var id = core.Service.Create(entity);
            if (CreateFailure is not null && entity.LogicalName == "region") throw CreateFailure;
            if (entity.LogicalName == "task")
            {
                target = id;
                core.Service.Rows.Single(row => row.LogicalName == "task" && row.Id == id)["activityid"] = id;
                Triggers++;
                if (LegacyStep != Guid.Empty)
                {
                    var legacy = core.Service.Rows.Single(row => row.Id == LegacyStep).GetAttributeValue<OptionSetValue>("statecode").Value == 0;
                    var enabledNew = core.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep" && row.Id != LegacyStep)
                        .Any(row => row.GetAttributeValue<OptionSetValue>("statecode").Value == 0);
                    Assert.NotEqual(legacy, enabledNew);
                    if (legacy && TokenRequired)
                        Assert.Equal(Fixture.SecretValue, core.Service.Rows.Single(row => row.LogicalName == "region").GetAttributeValue<string>("new_channelaccesstoken"));
                    RoundKinds.Add(legacy);
                    TriggerTargets.Add(id);
                }
                for (var index = 0; index < RealChildren; index++)
                    core.Service.Create(new Entity("region", Guid.NewGuid()) { ["score"] = 7, ["parentid"] = new EntityReference("task", id) });
                if (FailTrigger) throw Failure;
                AfterTrigger?.Invoke();
            }
            return id;
        }

        /// <summary>一般讀取委派獨占假組織，仍會核對 ID。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => core.Service.Retrieve(entityName, id, columnSet);
        /// <summary>所有設定及停用寫入皆走真正引擎與通用替身。</summary>
        public void Update(Entity entity) => core.Service.Update(entity);
        /// <summary>清理只接受正式 runtime 提供的精確身份。</summary>
        public void Delete(string entityName, Guid id)
        {
            if (entityName == "region" && FailSeedDelete)
            {
                FailSeedDelete = false;
                throw new InvalidOperationException("Injected seed cleanup failure");
            }
            if (entityName == "region" && RelatedReadFault == "delete-child") throw new InvalidOperationException("Injected cleanup fault");
            core.Service.Delete(entityName, id);
        }

        /// <summary>精確驗非同步 step、target、correlation 及型別；不以任何查詢都回成功。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            if (expression.EntityName == "region" && expression.Criteria.Conditions.Single().AttributeName == "new_church")
            {
                Assert.Equal(65, expression.TopCount);
                Assert.Equal(3, expression.ColumnSet.Columns.Count);
                Assert.Contains("new_church", expression.ColumnSet.Columns);
                Assert.Contains("regionid", expression.ColumnSet.Columns);
                Assert.Contains("versionnumber", expression.ColumnSet.Columns);
                var snapshot = core.Service.RetrieveMultiple(query);
                if (++externalReads == ExternalFaultRead) snapshot.MoreRecords = true;
                return snapshot;
            }
            if (expression.EntityName == "plugintype" && expression.ColumnSet.Columns.Contains("isworkflowactivity"))
            {
                var columns = expression.ColumnSet;
                expression.ColumnSet = new ColumnSet("typename", "pluginassemblyid");
                try { return core.Service.RetrieveMultiple(expression); }
                finally { expression.ColumnSet = columns; }
            }
            if (expression.EntityName == "region" && expression.Criteria.Conditions.Single().AttributeName == "parentid")
            {
                Assert.Equal(65, expression.TopCount);
                Assert.False(expression.ColumnSet.AllColumns);
                var related = core.Service.RetrieveMultiple(query);
                if (RelatedReadFault == "page") related.MoreRecords = true;
                if (RelatedReadFault == "wrong-parent" && related.Entities.Count != 0)
                    related.Entities[0]["parentid"] = new EntityReference("task", Guid.NewGuid());
                if (RelatedReadFault == "duplicate-id" && related.Entities.Count != 0)
                    related.Entities.Add(related.Entities[0]);
                return related;
            }
            if (expression.EntityName is not ("asyncoperation" or "plugintracelog")) return core.Service.RetrieveMultiple(query);
            Assert.Equal(2, expression.TopCount);
            Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "createdon" && condition.Operator == ConditionOperator.OnOrAfter);
            if (expression.EntityName == "asyncoperation")
            {
                AsyncQueries++;
                Assert.False(Synchronous);
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "regardingobjectid" && Equals(condition.Values[0], target));
                var stepId = Assert.IsType<Guid>(expression.Criteria.Conditions.Single(condition => condition.AttributeName == "owningextensionid").Values[0]);
                Assert.Contains(core.Service.Rows, row => row.LogicalName == "sdkmessageprocessingstep" && row.Id == stepId);
                return new EntityCollection([new Entity("asyncoperation", Guid.NewGuid()) { ["correlationid"] = correlation, ["statecode"] = new OptionSetValue(3), ["statuscode"] = new OptionSetValue(30) }]);
            }
            if (Synchronous)
            {
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "requestid" && Equals(condition.Values[0], RequestId));
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "pluginstepid" &&
                    Equals(condition.Values[0], core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").Id));
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "mode" && Equals(condition.Values[0], 0));
            }
            else Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "correlationid" && Equals(condition.Values[0], correlation));
            Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "typename" && Equals(condition.Values[0], new StepRegistration().Type));
            var trace = DifferentIntent
                ? "SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"" + target.ToString("D") + "\",\"fields\":{\"score\":9}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}"
                : "SMV-SHADOW|1|{\"end\":true,\"count\":0}";
            if (RelatedIntent)
            {
                var lines = new List<string>();
                var relatedIds = new List<string>();
                for (var index = 0; index < (DuplicateRelatedIntent ? 2 : 1); index++)
                {
                    var id = Guid.NewGuid().ToString("D");
                    relatedIds.Add(id);
                    lines.Add("SMV-SHADOW|1|" + JsonSerializer.Serialize(new
                    {
                        seq = index + 1, operation = "Create", entity = "region", id,
                        fields = new { score = 7, parentid = new { entity = "task", id = target.ToString("D") } }
                    }));
                }
                if (DeleteRelatedIntent)
                    foreach (var id in relatedIds)
                        lines.Add("SMV-SHADOW|1|" + JsonSerializer.Serialize(new { seq = lines.Count + 1, operation = "Delete", entity = "region", id }));
                lines.Add("SMV-SHADOW|1|{\"end\":true,\"count\":" + lines.Count + "}");
                trace = string.Join("\r\n", lines);
            }
            if (PushIntent)
                trace = "SMV-SHADOW|1|" + JsonSerializer.Serialize(new
                {
                    seq = 1, operation = "Push", contactId = Contact.ToString("D"), templateKey = "Attendance.Reply",
                    retryKey = target.ToString("D"), messageCount = 1, contentSha256 = new string(DifferentPushContent ? 'b' : 'a', 64)
                }) + "\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}";
            var result = new EntityCollection([new Entity("plugintracelog", Guid.NewGuid()) { ["messageblock"] = trace }]);
            if (DuplicateTrace) result.Entities.Add(new Entity("plugintracelog", Guid.NewGuid()) { ["messageblock"] = trace });
            return result;
        }

        /// <summary>此模組沒有關聯寫入，意外呼叫必須失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>此模組沒有解除關聯寫入，意外呼叫必須失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
