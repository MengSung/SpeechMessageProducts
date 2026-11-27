/*
檔案整體說明
目的與定位：以真實 SlotRequestPlugin 保護產品不符結果與持久診斷的同一交易邊界，不把安全 trace 當作通知已成功。
主要功能與流程：沿用已有 allocation Upsert／列鎖替身，注入無 tracing、trace 故障、空平台 correlation 與重複申請鍵；直接檢查正式已提交申請單內的固定封套與狀態，而非重寫 Plugin 判斷。另把帶有合成敏感 Message／Data 的真 Exception 交診斷入口，核對僅安全白名單通過。
使用邊界與維護：測試 owner 擁有單案資料與交易，所有同步 Execute 返回後活動交易必須為零；不連 D365／LINE、不讀 secrets、不啟動背景工作。非租戶及池滿為正常結果，不應留告警封套。這些離線斷言不代表部署端已接線或通知送達。
*/
using System;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Platform.Plugins.ChannelPool;
using Xunit;
namespace SpeechMessage.Dynamics.Platform.Plugins.Tests.ChannelPool;

/// <summary>保護 F3 的真例外投影、交易持久性及普通業務拒絕靜默契約。</summary>
public sealed class F3ProductMismatchDiagnosticsTests
{
    /// <summary>即使沒有 trace 或 trace 自身故障，產品不符仍提交一筆含固定安全欄位的正式申請。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Mismatch_PersistsSafeEnvelope_WithOrWithoutTracing(bool unavailable, bool throws)
    {
        var db = new PoolTransactionFixture { TraceUnavailable = unavailable, TraceThrows = throws };
        db.AddChannel();
        var incident = new Guid("15811ea9-fbc8-4c34-9f8d-e52f156ba600");
        var result = db.Execute(new SlotRequestPlugin(PoolTransactionFixture.Config),
            PoolTransactionFixture.Request(db.AddTenant()), "construction",
            changeContext: context => context.SetupGet(value => value.CorrelationId).Returns(incident));

        Assert.Equal(100000002, result.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        var committed = Assert.Single(db.Rows.Where(row => row.LogicalName == "new_slotrequest"));
        Assert.NotEqual(Guid.Empty, committed.Id);
        var envelope = Envelope(committed);
        Assert.Equal(1, (int)envelope["version"]!);
        Assert.Equal(incident.ToString("D"), (string?)envelope["incident"]);
        Assert.Equal("2026-11-26T12:00:00.0000000Z", (string?)envelope["utc"]);
        Assert.Equal("GoodButler", (string?)envelope["product"]);
        Assert.Equal("SlotRequest.ProductMismatch", (string?)envelope["operation"]);
        Assert.Equal("Microsoft.Xrm.Sdk.InvalidPluginExecutionException", (string?)envelope["exceptionType"]);
        Assert.Equal(6, envelope.Count);
        Assert.True(committed.Attributes.TryGetValue("new_f3diagnosticstate", out var state));
        Assert.Equal(0, Assert.IsType<int>(state));
        Assert.Equal(0, db.BusinessWrites);
        Assert.Equal(0, db.ActiveTransactions);
        if (!unavailable && !throws) Assert.Contains(incident.ToString("D"), Assert.Single(db.Traces), StringComparison.Ordinal);
    }

    /// <summary>平台 correlation 空值不允許空 incident；新 GUID 只屬本次事件，兩筆申請不可混用。</summary>
    [Fact]
    public void Mismatch_EmptyPlatformCorrelation_GeneratesNonemptyDistinctIncidents()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(); var plugin = new SlotRequestPlugin(PoolTransactionFixture.Config);
        var first = db.Execute(plugin, PoolTransactionFixture.Request(db.AddTenant()), "construction",
            changeContext: context => context.SetupGet(value => value.CorrelationId).Returns(Guid.Empty));
        var second = db.Execute(plugin, PoolTransactionFixture.Request(db.AddTenant()), "construction",
            changeContext: context => context.SetupGet(value => value.CorrelationId).Returns(Guid.Empty));
        var a = Guid.Parse((string)Envelope(first)["incident"]!);
        var b = Guid.Parse((string)Envelope(second)["incident"]!);
        Assert.NotEqual(Guid.Empty, a); Assert.NotEqual(a, b);
    }

    /// <summary>真正例外的 Message／Data 不可序列化；輸出型別取自原物件，沒有完整 Exception 保留。</summary>
    [Fact]
    public void DiagnosticIngress_AcceptsRealException_ProjectsOnlySafeFields()
    {
        var original = new InvalidPluginExecutionException("synthetic-secret-do-not-publish");
        original.Data["token"] = "synthetic-data-do-not-publish";
        var target = new Entity("new_slotrequest");
        var context = new Mock<IPluginExecutionContext>();
        context.SetupGet(value => value.CorrelationId).Returns(Guid.NewGuid());
        context.SetupGet(value => value.OperationCreatedOn).Returns(new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc));
        PoolData.RecordProductMismatch(null, original, target, "GoodGuild", context.Object);
        var envelope = Envelope(target);
        Assert.Equal(original.GetType().FullName, (string?)envelope["exceptionType"]);
        var text = envelope.ToString();
        Assert.DoesNotContain("synthetic-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-data", text, StringComparison.Ordinal);
        Assert.Equal(6, envelope.Count);
    }

    /// <summary>非租戶與池滿不製造例外事件；使用不同正常分支避免把所有失敗結果一概告警。</summary>
    [Theory]
    [InlineData(false, 100000003)]
    [InlineData(true, 100000001)]
    public void ExpectedBusinessResults_DoNotCreateDiagnostic(bool tenant, int result)
    {
        var db = new PoolTransactionFixture();
        var response = db.Execute(new SlotRequestPlugin(PoolTransactionFixture.Config), PoolTransactionFixture.Request(db.AddTenant(tenant)));
        Assert.Equal(result, response.GetAttributeValue<OptionSetValue>("new_result")?.Value);
        Assert.False(response.Contains("new_f3diagnostic"));
        Assert.False(response.Contains("new_f3diagnosticstate"));
        Assert.Empty(db.Traces);
    }

    /// <summary>診斷欄位皆由伺服器寫入；caller 偽造封套或已通知狀態必須在任何業務寫入前拒絕。</summary>
    [Theory]
    [InlineData("new_f3diagnostic")]
    [InlineData("new_f3diagnosticstate")]
    public void CallerDiagnosticFields_AreRejected(string field)
    {
        var db = new PoolTransactionFixture(); db.AddChannel(); var request = PoolTransactionFixture.Request(db.AddTenant());
        request[field] = "forged";
        Assert.Throws<InvalidPluginExecutionException>(() => db.Execute(new SlotRequestPlugin(PoolTransactionFixture.Config), request));
        Assert.Equal(0, db.BusinessWrites);
        Assert.DoesNotContain(db.Rows, row => row.LogicalName == "new_slotrequest");
    }

    /// <summary>同鍵正式 Create 失敗時不得增加第二份持久事件；保留原申請單供 caller 精確讀回。</summary>
    [Fact]
    public void DuplicateRequestFault_DoesNotPublishSecondDiagnostic()
    {
        var db = new PoolTransactionFixture(); db.AddChannel(); var tenant = db.AddTenant();
        var correlation = Guid.NewGuid().ToString("D"); var plugin = new SlotRequestPlugin(PoolTransactionFixture.Config);
        db.Execute(plugin, PoolTransactionFixture.Request(tenant, correlation: correlation), "construction");
        var first = Assert.Single(db.Rows.Where(row => row.LogicalName == "new_slotrequest"));
        var text = first.GetAttributeValue<string>("new_f3diagnostic");
        Assert.Throws<InvalidOperationException>(() => db.Execute(plugin, PoolTransactionFixture.Request(tenant, correlation: correlation), "construction"));
        Assert.Equal(text, Assert.Single(db.Rows.Where(row => row.LogicalName == "new_slotrequest")).GetAttributeValue<string>("new_f3diagnostic"));
        Assert.Equal(0, db.ActiveTransactions);
    }

    /// <summary>只解析真入口已產出的字串；缺封套須以斷言失敗，不能用預設資料掩蓋尚未實作。</summary>
    private static JObject Envelope(Entity row)
    {
        var text = row.GetAttributeValue<string>("new_f3diagnostic");
        Assert.False(string.IsNullOrEmpty(text));
        Assert.InRange(text.Length, 1, 1024);
        // 保留封套原始 UTC 字串；Newtonsoft 自動轉 Date token 會讓 string cast 改用文化格式，掩蓋線上契約。
        using (var input = new StringReader(text))
        using (var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None })
            return JObject.Load(reader);
    }
}
