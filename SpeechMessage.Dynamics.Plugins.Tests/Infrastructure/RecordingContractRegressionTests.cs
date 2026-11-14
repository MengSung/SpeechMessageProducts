/*
 * 檔案整體說明
 *
 * 目的與定位：保護獨立審查指出的影子隱私、連結讀取及未支援相關實體圖契約，
 * 使用既有記憶體服務及合成 SDK 值，完全不連 Dataverse 或 LINE。
 * 主要功能與流程：注入既有自訂個資欄位、QueryExpression／FetchXML 的別名結果，
 * 以及 RelatedEntities 寫入；驗證雜湊、來源與結果分離及拒絕後零發布。
 * 使用邊界與維護：兩種 service 皆由單一案例 using owner 擁有並排空；測試資料
 * 只有固定合成值，不能冒充真實個資或實機結果。查詢及寫入在同步範圍完成，
 * 沒有快取、共享狀態、背景工作或外部資源，失敗不跳過清理。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Infrastructure;

/// <summary>使用 recorder 與假服務整合的決定性回歸案例，避免分層綠燈掩蓋契約缺口。</summary>
public sealed class RecordingContractRegressionTests
{
    /// <summary>既有自訂身分證與拼字特殊的手機欄位在一般、巢狀及 alias 路徑均只留雜湊。</summary>
    [Theory]
    [InlineData("new_personal_id")]
    [InlineData("new_cell_hpone")]
    [InlineData("company")]
    public void Recording_CustomPersonalFields_HashedAtAllDepths(string field)
    {
        using var inner = new FakeOrganizationService();
        using var recording = new RecordingOrganizationService(inner);
        var entity = new Entity("contact", Guid.NewGuid()) { [field] = "synthetic-private" };
        entity["new_child"] = new Entity("contact", Guid.NewGuid()) { [field] = "synthetic-private" };
        entity["new_alias"] = new AliasedValue("contact", field, "synthetic-private");
        entity.KeyAttributes[field] = "synthetic-private";

        recording.Update(entity);
        var projection = JObject.Parse(Assert.Single(recording.Writes).ProjectionJson);
        var digest = (string?)projection["fields"]![field]!["sha256"];

        Assert.Equal(64, digest!.Length);
        Assert.Equal(digest, (string?)projection["fields"]!["new_child"]!["fields"]![field]!["sha256"]);
        Assert.Equal(digest, (string?)projection["fields"]!["new_alias"]!["sha256"]);
        Assert.Equal(digest, (string?)projection["keys"]![field]!["sha256"]);
        Assert.DoesNotContain("synthetic-private", projection.ToString(), StringComparison.Ordinal);
        Assert.Empty(inner.Calls);
    }

    /// <summary>查詢別名可通過 recorder，且修改回傳 Money 不得回寫假服務或下一次結果。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_LinkAliases_ReadDetached(bool fetch)
    {
        using var inner = new FakeOrganizationService();
        var parent = new Entity("account", Guid.NewGuid()) { ["new_amount"] = new Money(10m) };
        var child = new Entity("contact", Guid.NewGuid()) { ["parentcustomerid"] = parent.ToEntityReference() };
        inner.Create(parent);
        inner.Create(child);
        using var recording = new RecordingOrganizationService(inner);
        var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("parentcustomerid") };
        query.AddLink("account", "parentcustomerid", "accountid").EntityAlias = "p";
        query.LinkEntities[0].Columns = new ColumnSet("new_amount");
        QueryBase selected = fetch
            ? new FetchExpression("<fetch><entity name='contact'><attribute name='parentcustomerid'/><link-entity name='account' from='accountid' to='parentcustomerid' alias='p'><attribute name='new_amount'/></link-entity></entity></fetch>")
            : query;

        var first = Assert.Single(recording.RetrieveMultiple(selected).Entities);
        ((Money)((AliasedValue)first["p.new_amount"]).Value).Value = 99m;
        var second = Assert.Single(recording.RetrieveMultiple(selected).Entities);

        Assert.Equal(10m, ((Money)((AliasedValue)second["p.new_amount"]).Value).Value);
        Assert.Empty(recording.Writes);
    }

    /// <summary>RelatedEntities 尚未支援時四個寫入入口皆須拒絕，不得產生看似完整的成功意圖。</summary>
    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("ExecuteCreate")]
    [InlineData("ExecuteUpdate")]
    public void Recording_RelatedEntityWrites_RejectBeforePublication(string operation)
    {
        using var inner = new FakeOrganizationService();
        using var recording = new RecordingOrganizationService(inner);
        var entity = new Entity("account", Guid.NewGuid());
        entity.RelatedEntities.Add(new Relationship("account_contacts"),
            new EntityCollection { Entities = { new Entity("contact", Guid.NewGuid()) } });

        var error = Assert.Throws<NotSupportedException>(() =>
        {
            switch (operation)
            {
                case "Create": recording.Create(entity); break;
                case "Update": recording.Update(entity); break;
                case "ExecuteCreate": recording.Execute(new CreateRequest { Target = entity }); break;
                case "ExecuteUpdate": recording.Execute(new UpdateRequest { Target = entity }); break;
            }
        });

        Assert.Contains("RelatedEntities", error.Message, StringComparison.Ordinal);
        Assert.Empty(recording.Writes);
        Assert.Equal(0, recording.TotalCount);
        Assert.Empty(inner.Calls);
    }

    /// <summary>影子 JSON 往返必須保留高精度金額與日期字串的原始 offset，巢狀投影亦同。</summary>
    [Fact]
    public void Recording_DecimalAndOffset_PreserveProjectionValues()
    {
        using var inner = new FakeOrganizationService();
        using var recording = new RecordingOrganizationService(inner);
        using var pushes = new ShadowPushRecorder();
        const decimal amount = 12345678901234567890.123456789m;
        const string date = "2030-01-02T03:04:05+05:45";
        var child = new Entity("task") { ["new_amount"] = new Money(amount), ["new_date"] = date };
        recording.Update(new Entity("account", Guid.NewGuid())
        {
            ["new_amount"] = new Money(amount), ["new_date"] = date, ["new_child"] = child
        });
        var tracing = new FakeTracingService();

        new ShadowRecordWriter(tracing).Write(recording, pushes);

        var line = tracing.Entries[0];
        Assert.Contains("\"new_amount\":12345678901234567890.123456789", line, StringComparison.Ordinal);
        Assert.Contains("\"new_date\":\"2030-01-02T03:04:05+05:45\"", line, StringComparison.Ordinal);
        Assert.Contains("\"new_child\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("E+", line, StringComparison.Ordinal);
    }
}
