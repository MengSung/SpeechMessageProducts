/*
 * 檔案整體說明
 * 目的與定位：P5-12 真 OutboundLetterPlugin 的離線平台契約，保護外送方向、深度、
 * 單一正式收件者、權威來源教會、影子寫入及 Live 查詢前拒絕，不把 HTTP 替身當實機。
 * 主要流程：每案用正式外殼加既有 FakeOrganizationService，合成 letter／account／
 * contact／channel；追蹤須有完整推播摘要與完成意圖，真 SDK WriteRecords 必須空。
 * 故障注入會改變收件者教會、移除綁定、增加第二個 party 或使用無教會關聯的寄件者；共用 fixture 使用真 RemoteExecutionContext，允許 CAS 子域
 * 不依賴 Moq／私有反射重跑同一鏈。
 * 決定性斷言核對短路與原例外，不以姓名或收件者本身反推來源教會。
 * 資源與隔離：fixture 是單案 SDK／trace 的唯一 owner；同步 Execute 排空後清除
 * 所有來源及回呼。沒有真網路、秘密、Session、背景工作或跨案例共用狀態。
 * 驗證範圍只有離線外殼與意圖，goodshepherdback 追蹤維持 live=deferred。
 */
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Messaging;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>真平台薄外殼的讀取、影子及 fail-closed 邊界。</summary>
public sealed class OutboundLetterShellTests
{
    /// <summary>真外殼記錄單人推播與完成候選，底層 SDK 沒有真寫入。</summary>
    [Fact]
    public void Messaging_ShadowMode_NoRealWrites()
    {
        using var f = new Fixture();
        f.Run();
        Assert.Contains(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
        Assert.Contains(f.Trace.Entries, x => x.Contains("Messaging.OutboundLetter"));
        Assert.Contains(f.Trace.Entries, x => x.Contains("\"operation\":\"Update\""));
        var patch = f.Patch();
        Assert.Equal(1, (int)patch["statecode"]!);
        Assert.Equal(4, (int)patch["statuscode"]!);
        Assert.Equal("SMV:Messaging.Completed", (string?)patch["category"]);
        Assert.False(patch.ContainsKey("description"));
        Assert.Empty(f.Sdk.WriteRecords);
    }

    /// <summary>保留深度二且 Create 放行；Update 三層不得執行任何 SDK 讀取。</summary>
    [Theory]
    [InlineData("Create", 3, true)]
    [InlineData("Update", 2, true)]
    [InlineData("Update", 3, false)]
    public void Messaging_DepthPolicy_MatchesLegacy(string message, int depth, bool executes)
    {
        using var f = new Fixture();
        f.Context.MessageName = message;
        f.Context.Depth = depth;
        f.Run();
        Assert.Equal(executes, f.Sdk.Calls.ContainsKey("Retrieve"));
    }

    /// <summary>入站僅讀方向即結束；不讀寄件者、頻道或記錄任何推播。</summary>
    [Fact]
    public void Letter_Inbound_Ignored()
    {
        using var f = new Fixture(outbound: false);
        f.Run();
        Assert.Equal(1, f.Sdk.Calls["Retrieve"]);
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
        Assert.Empty(f.Sdk.WriteRecords);
    }

    /// <summary>Live 在讀 letter 之前就依核定缺口拒絕，不能因為規則測試可送就開啟。</summary>
    [Fact]
    public void Messaging_Live_RefusedBeforeSdkRead()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidPluginExecutionException>(() => f.Run(live: true));
        Assert.Empty(f.Sdk.Calls);
    }

    /// <summary>來源教會由寄件者 account 固定；收件者另一教會時只記失敗，不記推播。</summary>
    [Theory]
    [InlineData(true, true, "ChurchMismatch")]
    [InlineData(false, false, "NotBound")]
    public void Messaging_RecipientOtherChurchOrNotBound_NoPush(bool otherChurch, bool bound, string reason)
    {
        using var f = new Fixture(otherChurch: otherChurch, bound: bound);
        f.Run();
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
        Assert.Contains(f.Trace.Entries, x => x.Contains("Messaging.Failure." + reason));
        var patch = f.Patch();
        Assert.Equal(2, (int)patch["statecode"]!);
        Assert.Equal(5, (int)patch["statuscode"]!);
        Assert.Equal("SMV:Messaging.Failure." + reason, (string?)patch["category"]);
        Assert.Equal(reason == "NotBound" ? "未綁定 LINE" : "收件者教會不符", (string?)patch["subcategory"]);
        Assert.Empty(f.Sdk.WriteRecords);
    }

    /// <summary>既有非本模組類別不能被搶用，須在收件者及頻道讀取前停止且零意圖。</summary>
    [Fact]
    public void Messaging_ExistingCategory_NotOverwritten()
    {
        using var f = new Fixture(category: "owned by another module");
        Assert.Throws<InvalidOperationException>(() => f.Run());
        Assert.Equal(1, f.Sdk.Calls["Retrieve"]);
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Update\"") || x.Contains("\"operation\":\"Push\""));
    }

    /// <summary>已終止活動不能因狀態回寫重入而再推播；不是依靠本機記憶體去重。</summary>
    [Fact]
    public void Messaging_CompletedActivity_NotResent()
    {
        using var f = new Fixture(state: 1);
        f.Run();
        Assert.Equal(1, f.Sdk.Calls["Retrieve"]);
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
    }

    /// <summary>離線活動查詢依實際 metadata 的 activityid 篩選，不接受不存在的 letterid。</summary>
    [Fact]
    public void Messaging_LetterPrimaryIdentity_UsesActivityId()
    {
        using var f = new Fixture();
        var query = new QueryExpression("letter") { ColumnSet = new ColumnSet("activityid") };
        query.Criteria.AddCondition("activityid", ConditionOperator.Equal, f.Context.PrimaryEntityId);
        var row = Assert.Single(f.Sdk.RetrieveMultiple(query).Entities);
        Assert.Equal(f.Context.PrimaryEntityId, row["activityid"]);
        Assert.False(row.Contains("letterid"));
    }

    /// <summary>多個收件 party 直接拒絕，不能靜默挑第一人或重複推送。</summary>
    [Fact]
    public void Messaging_MultipleRecipients_Refused()
    {
        using var f = new Fixture(recipientCount: 2);
        Assert.Throws<InvalidOperationException>(() => f.Run());
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
    }

    /// <summary>沒有正式教會關聯的 systemuser 寄件者不得從收件者猜來源教會。</summary>
    [Fact]
    public void Messaging_UnprovenSourceChurch_Refused()
    {
        using var f = new Fixture(senderTable: "systemuser");
        Assert.Throws<InvalidOperationException>(() => f.Run());
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
    }

    /// <summary>SDK 的原始錯誤須重新拋出同實例，不能像退役外掛吞錯。</summary>
    [Fact]
    public void Messaging_UnexpectedError_Rethrown()
    {
        using var f = new Fixture();
        var original = new InvalidOperationException("synthetic private failure");
        f.Sdk.RetrieveHandler = (_, _, _) => throw original;
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => f.Run()));
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains(original.Message));
    }

    /// <summary>缺少權威狀態不能推定為 Open；在任何收件者查詢或意圖前封閉失敗。</summary>
    [Fact]
    public void Messaging_MissingState_RefusedBeforePush()
    {
        using var f = new Fixture();
        var original = f.Sdk.Retrieve("letter", f.Context.PrimaryEntityId, new ColumnSet(true));
        original.Attributes.Remove("statecode");
        f.Sdk.RetrieveHandler = (table, _, _) => table == "letter" ? original : throw new InvalidOperationException("Unexpected downstream read.");
        var failure = Assert.Throws<InvalidOperationException>(() => f.Run());
        Assert.Equal("A formal letter state is required.", failure.Message);
        Assert.DoesNotContain(f.Trace.Entries, x => x.Contains("\"operation\":\"Push\""));
        Assert.Empty(f.Sdk.WriteRecords);
    }

    /// <summary>同外殼重用 A/B/A 各自只用當次服務，清理後不保留上一個活動來源。</summary>
    [Fact]
    public void Messaging_ReusedShell_ABA_Isolated()
    {
        var plugin = new OutboundLetterPlugin("{\"mode\":\"Shadow\"}", "");
        foreach (var otherChurch in new[] { false, true, false })
        {
            using var f = new Fixture(otherChurch: otherChurch);
            plugin.Execute(f.Provider);
            Assert.Equal(!otherChurch, f.Trace.Entries.Exists(x => x.Contains("\"operation\":\"Push\"")));
            Assert.Empty(f.Sdk.WriteRecords);
        }
    }

    /// <summary>每案各自擁有合成 SDK 與平台服務，沒有共用可變資料或外部連線。</summary>
    internal sealed class Fixture : IDisposable
    {
        internal readonly FakeOrganizationService Sdk;
        /// <summary>本案正式收件人識別，供完整影子比對，不以顯示姓名判斷。</summary>
        internal readonly Guid RecipientId;
        internal readonly FakeTracingService Trace = new();
        internal readonly RemoteExecutionContext Context;
        internal readonly FakeServiceProvider Provider;

        /// <summary>正式活動 ID 與來源教會分別建立，不使用 sender 顯示名稱授權。</summary>
        internal Fixture(bool outbound = true, bool otherChurch = false, bool bound = true,
            int recipientCount = 1, string senderTable = "account", string? category = null, int state = 0, string content = "synthetic raw text")
        {
            var activity = Guid.NewGuid(); var church = Guid.NewGuid(); var contact = Guid.NewGuid(); var channel = Guid.NewGuid();
            RecipientId = contact;
            var recipients = new EntityCollection();
            for (var i = 0; i < recipientCount; i++)
                recipients.Entities.Add(new Entity("activityparty", Guid.NewGuid()) { ["partyid"] = new EntityReference("contact", contact) });
            Sdk = new FakeOrganizationService(new[]
            {
                new Entity("letter", activity) { ["directioncode"] = outbound, ["description"] = content,
                    ["statecode"] = new OptionSetValue(state), ["statuscode"] = new OptionSetValue(1), ["category"] = category,
                    ["to"] = recipients, ["from"] = new EntityCollection(new[] { new Entity("activityparty", Guid.NewGuid())
                        { ["partyid"] = new EntityReference(senderTable, church) } }) },
                new Entity("account", church) { ["name"] = "Synthetic", ["new_linechannelid"] = new EntityReference("new_linechannel", channel),
                    ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW" },
                new Entity("contact", contact) { ["parentcustomerid"] = new EntityReference("account", otherChurch ? Guid.NewGuid() : church),
                    ["new_lineid"] = bound ? "U00000000000000000000000000000000" : null },
                new Entity("new_linechannel", channel)
            });
            Context = new RemoteExecutionContext { MessageName = "Create", PrimaryEntityName = "letter", PrimaryEntityId = activity,
                Stage = 40, Mode = 1, Depth = 1, OrganizationId = Guid.NewGuid(), OrganizationName = "synthetic-messaging",
                UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid() };
            Context.InputParameters["Target"] = new Entity("letter", activity) { ["directioncode"] = outbound };
            Provider = new FakeServiceProvider().Add<IPluginExecutionContext>(Context).Add<ITracingService>(Trace)
                .Add<IOrganizationServiceFactory>(new FakeOrganizationServiceFactory(Sdk));
        }

        /// <summary>建立真外殼，不替換 ExecuteRule 或共用 recording 實作。</summary>
        internal void Run(bool live = false) =>
            new OutboundLetterPlugin(live ? "{\"mode\":\"Live\"}" : "{\"mode\":\"Shadow\"}", "").Execute(Provider);

        /// <summary>解析共用 writer 實際輸出的唯一 Update，不以手造候選取代規則驗證。</summary>
        internal JObject Patch()
        {
            var update = Trace.Entries.Single(x => x.Contains("\"operation\":\"Update\""));
            return (JObject)JObject.Parse(update.Substring("SMV-SHADOW|1|".Length))["fields"]!;
        }

        /// <summary>同步 Execute 排空後清除 SDK、回呼與追蹤，不處置平台並不存在的真連線。</summary>
        public void Dispose() { Sdk.Dispose(); Trace.Entries.Clear(); Context.InputParameters.Clear(); Context.PreEntityImages.Clear(); Context.PostEntityImages.Clear(); }
    }

    /// <summary>只借出本案 SDK，不建立連線、不轉移 SDK owner 或保留跨案身分。</summary>
    private sealed class FakeOrganizationServiceFactory(IOrganizationService sdk) : IOrganizationServiceFactory
    {
        /// <summary>模擬平台工廠的單次借用，SDK 的 Dispose 仍由 fixture 負責。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) => sdk;
    }
}
