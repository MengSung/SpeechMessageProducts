/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-01 離線組織服務替身的記憶體資料、查詢與隔離契約，
 * 讓後續 Plugin 規則測試不因空集合 fallback 而錯誤通過；不代表 Dataverse 實機驗證。
 *
 * 主要功能與流程：各案例以合成 Entity 建立獨立表，注入不支援的運算子、重複識別、
 * 多對一連結與可變 SDK 值，斷言 CRUD、篩選、排序、連結及回傳值的深層分離。
 * 故障之後再次讀取原資料，以確認失敗沒有部分更新或吞掉既有記錄。
 * 活動案例另以 appointment／task 的 activityid 驗證兩種查詢的篩選、投影及連結，
 * 注入衝突主鍵並確認舊快照不變；未知實體必須具名拒絕，不能猜測特殊主鍵。
 *
 * 使用邊界與維護：每個案例獨佔 fake，不使用真實連線、憑證或背景工作；案例結束
 * 由 using owner 清除表、關聯、回呼與計數。併行案例只共享同一案例明確建立的服務，
 * 不把合成資料放入可變 static；本檔不驗證真實 CRM 的權限、metadata 或傳輸。
 * 寫入測試另外保護成功 CRUD 的有界紀錄、payload 深層隔離、故障時不記錄、
 * 容量耗盡時不改資料，以及 Dispose 對審計快照來源的清理行為。
 */
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>以合成 SDK 輸入與決定性斷言保護離線資料及查詢契約，各案例各自擁有替身。</summary>
public sealed class FakeOrganizationServiceTests
{
    /// <summary>
    /// 成功 Create／Update／Delete 依序留下操作種類及 detached payload；初始化資料不算寫入，
    /// 修改來源或快照均不能變更記錄。合成服務由 using owner 清理，無真實組織服務依賴。
    /// </summary>
    [Fact]
    public void Fake_WriteRecords_CaptureSuccessfulOperationsAsDetachedSnapshots()
    {
        using var service = new FakeOrganizationService(new[] { new Entity("contact", Guid.NewGuid()) { ["name"] = "seed" } });
        var bytes = new byte[] { 1, 2 };
        var source = new Entity("contact") { ["name"] = "created", ["bytes"] = bytes };
        var id = service.Create(source);
        service.Update(new Entity("contact", id) { ["name"] = "updated" });
        service.Delete("contact", id);

        bytes[0] = 9;
        source["name"] = "caller mutation";
        var records = service.WriteRecords;
        Assert.Collection(records,
            item => Assert.Equal("Create", item.Operation),
            item => Assert.Equal("Update", item.Operation),
            item => Assert.Equal("Delete", item.Operation));
        Assert.Equal(id, records[0].Id);
        Assert.Equal("contact", records[0].EntityName);
        Assert.Equal("created", records[0].Entity!["name"]);
        Assert.Equal(1, ((byte[])records[0].Entity!["bytes"])[0]);
        Assert.Equal("updated", records[1].Entity!["name"]);
        Assert.Null(records[2].Entity);
        Assert.Equal(id, records[2].Id);

        var mutatedSnapshot = records[0].Entity!;
        mutatedSnapshot["name"] = "snapshot mutation";
        ((byte[])mutatedSnapshot["bytes"])[0] = 8;
        Assert.Equal("created", records[0].Entity!["name"]);
        Assert.Equal(1, ((byte[])records[0].Entity!["bytes"])[0]);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<OrganizationWriteRecord>)records).Clear());
    }

    /// <summary>
    /// Handler 例外及無法複製的值不得產生成功紀錄；達到紀錄上限後 Create 必須在改表前拒絕。
    /// 透過合成 fault 與上限填滿注入錯誤，並驗證表列及 audit 數量維持確定值。
    /// </summary>
    [Fact]
    public void Fake_WriteRecords_FaultsAndCapacityFailWithoutPartialWrite()
    {
        using (var failing = new FakeOrganizationService
        {
            CreateHandler = _ => throw new InvalidOperationException("synthetic")
        })
        {
            Assert.Throws<InvalidOperationException>(() => failing.Create(new Entity("contact")));
            Assert.Empty(failing.WriteRecords);
            failing.CreateHandler = null;
            Assert.Throws<NotSupportedException>(() => failing.Create(new Entity("contact") { ["unsupported"] = new object() }));
            Assert.Empty(failing.WriteRecords);
            Assert.Equal(0, failing.EntityCount);
        }

        using var bounded = new FakeOrganizationService();
        for (var index = 0; index < 4096; index++)
            bounded.Create(new Entity("contact") { ["sequence"] = index });
        Assert.Equal(4096, bounded.WriteRecords.Count);
        Assert.Throws<NotSupportedException>(() => bounded.Create(new Entity("contact") { ["sequence"] = 4096 }));
        Assert.Equal(4096, bounded.EntityCount);
        Assert.Equal(4096, bounded.WriteRecords.Count);
    }

    /// <summary>
    /// Dispose 清除服務持有的寫入紀錄；預先取出的唯讀快照仍由讀者持有，
    /// 但其中 Entity 仍是 detached 值，不會參照已清除的 fake 資料表。
    /// </summary>
    [Fact]
    public void Fake_WriteRecords_DisposeClearsOwnedRecordsAndKeepsPriorSnapshotsDetached()
    {
        var service = new FakeOrganizationService();
        service.Create(new Entity("contact") { ["name"] = "synthetic" });
        var priorSnapshot = service.WriteRecords;
        service.Dispose();

        Assert.Single(priorSnapshot);
        Assert.Empty(service.WriteRecords);
    }

    /// <summary>
    /// 活動只以 activityid 篩選，兩筆同名不同識別不能混淆；兩種查詢均須只取命中的正式主鍵。
    /// 使用合成表與不含主鍵屬性的輸入，刻意依賴替身補齊；using 在案例結束排空。
    /// </summary>
    [Theory]
    [InlineData("appointment", false)]
    [InlineData("appointment", true)]
    [InlineData("task", false)]
    [InlineData("task", true)]
    public void Fake_Activity_PrimaryFilter(string entityName, bool fetch)
    {
        using var service = new FakeOrganizationService();
        var id = service.Create(new Entity(entityName) { ["subject"] = "same" });
        service.Create(new Entity(entityName) { ["subject"] = "same" });
        var expression = new QueryExpression(entityName) { ColumnSet = new ColumnSet("activityid") };
        expression.Criteria.AddCondition("activityid", ConditionOperator.Equal, id);
        QueryBase query = fetch ? new FetchExpression("<fetch><entity name='" + entityName +
            "'><attribute name='activityid'/><filter><condition attribute='activityid' operator='eq' value='" +
            id.ToString("D") + "'/></filter></entity></fetch>") : expression;
        var row = Assert.Single(service.RetrieveMultiple(query).Entities);
        Assert.Equal(id, row.Id);
        Assert.Equal(id, row["activityid"]);
        Assert.False(row.Contains(entityName + "id"));
    }

    /// <summary>
    /// 未要求主鍵的投影仍須帶 activityid，不能製造 appointmentid／taskid；修改回傳主鍵不影響表。
    /// 合成活動經 Update 再用 Retrieve 及兩種查詢讀回，確保所有寫讀路徑共用身分規則。
    /// </summary>
    [Theory]
    [InlineData("appointment", false)]
    [InlineData("appointment", true)]
    [InlineData("task", false)]
    [InlineData("task", true)]
    public void Fake_Activity_PrimaryProjection(string entityName, bool fetch)
    {
        using var service = new FakeOrganizationService();
        var id = service.Create(new Entity(entityName));
        service.Update(new Entity(entityName, id) { ["subject"] = "after" });
        QueryBase query = fetch ? new FetchExpression("<fetch><entity name='" + entityName +
            "'><attribute name='subject'/></entity></fetch>") :
            new QueryExpression(entityName) { ColumnSet = new ColumnSet("subject") };
        var row = Assert.Single(service.RetrieveMultiple(query).Entities);
        Assert.Equal(id, row["activityid"]);
        Assert.Equal("after", row["subject"]);
        Assert.False(row.Contains(entityName + "id"));
        row["activityid"] = Guid.NewGuid();
        var retrieved = service.Retrieve(entityName, id, new ColumnSet(false));
        Assert.Equal(id, retrieved["activityid"]);
        Assert.Single(retrieved.Attributes);
    }

    /// <summary>
    /// 活動作為連結兩端時皆使用 activityid；加入不匹配的同名活動，避免忽略 join 的假陽性。
    /// 兩種查詢須保留根身分及別名主鍵，別名 metadata 指向實際活動欄位；using 清除全部合成列。
    /// </summary>
    [Theory]
    [InlineData("appointment", "task", false)]
    [InlineData("appointment", "task", true)]
    [InlineData("task", "appointment", false)]
    [InlineData("task", "appointment", true)]
    public void Fake_Activity_PrimaryLink(string rootName, string linkedName, bool fetch)
    {
        using var service = new FakeOrganizationService();
        var id = Guid.NewGuid();
        service.Create(new Entity(rootName, id) { ["subject"] = "same" });
        service.Create(new Entity(rootName) { ["subject"] = "same" });
        service.Create(new Entity(linkedName, id));
        service.Create(new Entity(linkedName));
        var expression = new QueryExpression(rootName) { ColumnSet = new ColumnSet("subject") };
        var link = expression.AddLink(linkedName, "activityid", "activityid");
        link.EntityAlias = "a";
        link.Columns = new ColumnSet("activityid");
        link.LinkCriteria.AddCondition("activityid", ConditionOperator.Equal, id);
        QueryBase query = fetch ? new FetchExpression("<fetch><entity name='" + rootName +
            "'><attribute name='subject'/><link-entity name='" + linkedName +
            "' from='activityid' to='activityid' alias='a'><attribute name='activityid'/>" +
            "<filter><condition attribute='activityid' operator='eq' value='" + id.ToString("D") +
            "'/></filter></link-entity></entity></fetch>") : expression;
        var row = Assert.Single(service.RetrieveMultiple(query).Entities);
        Assert.Equal(id, row.Id);
        Assert.Equal(id, row["activityid"]);
        var alias = Assert.IsType<AliasedValue>(row["a.activityid"]);
        Assert.Equal(linkedName, alias.EntityLogicalName);
        Assert.Equal("activityid", alias.AttributeLogicalName);
        Assert.Equal(id, alias.Value);
        Assert.False(row.Contains("a." + linkedName + "id"));
    }

    /// <summary>
    /// 與 Entity.Id 不同、空值或非 GUID 的 activityid 必須在 Create／Update 發布前拒絕。
    /// 故障後兩種查詢仍讀到原主鍵及原 subject，列數不變；案例 owner 確定性排空。
    /// </summary>
    [Theory]
    [InlineData("appointment", false)]
    [InlineData("appointment", true)]
    [InlineData("task", false)]
    [InlineData("task", true)]
    public void Fake_Activity_ConflictingIdentity(string entityName, bool fetch)
    {
        using var service = new FakeOrganizationService();
        var id = service.Create(new Entity(entityName) { ["subject"] = "before" });
        foreach (var invalid in new object?[] { Guid.NewGuid(), Guid.Empty, null, "invalid" })
        {
            Assert.Throws<InvalidOperationException>(() => service.Create(
                new Entity(entityName, Guid.NewGuid()) { ["activityid"] = invalid }));
            Assert.Throws<InvalidOperationException>(() => service.Update(
                new Entity(entityName, id) { ["subject"] = "after", ["activityid"] = invalid }));
        }
        QueryBase query = fetch ? new FetchExpression("<fetch><entity name='" + entityName +
            "'><attribute name='subject'/></entity></fetch>") :
            new QueryExpression(entityName) { ColumnSet = new ColumnSet("subject") };
        var row = Assert.Single(service.RetrieveMultiple(query).Entities);
        Assert.Equal(id, row["activityid"]);
        Assert.Equal("before", row["subject"]);
        Assert.Equal(1, service.EntityCount);
    }

    /// <summary>
    /// 未宣告 metadata 的活動及特殊主鍵實體不能套用名稱加 id；空表根與連結亦須具名拒絕。
    /// 故障注入使用 email／activitypointer／不明名稱；不建立外部服務，using 排空本案例。
    /// </summary>
    [Theory]
    [InlineData("email", false)]
    [InlineData("email", true)]
    [InlineData("activitypointer", false)]
    [InlineData("activitypointer", true)]
    [InlineData("unsupported_special", false)]
    [InlineData("unsupported_special", true)]
    public void Fake_Activity_UnknownPrimaryMappingFailsClosed(string entityName, bool fetch)
    {
        using var service = new FakeOrganizationService();
        Assert.Contains("PrimaryIdAttribute." + entityName, Assert.Throws<NotSupportedException>(
            () => service.Create(new Entity(entityName))).Message, StringComparison.Ordinal);
        QueryBase root = fetch ? new FetchExpression("<fetch><entity name='" + entityName +
            "'><attribute name='activityid'/></entity></fetch>") : new QueryExpression(entityName);
        Assert.Contains("PrimaryIdAttribute." + entityName, Assert.Throws<NotSupportedException>(
            () => service.RetrieveMultiple(root)).Message, StringComparison.Ordinal);
        var expression = new QueryExpression("contact");
        expression.AddLink(entityName, "regardingobjectid", "activityid");
        QueryBase linked = fetch ? new FetchExpression("<fetch><entity name='contact'><link-entity name='" +
            entityName + "' from='activityid' to='regardingobjectid'/></entity></fetch>") : expression;
        Assert.Contains("PrimaryIdAttribute." + entityName, Assert.Throws<NotSupportedException>(
            () => service.RetrieveMultiple(linked)).Message, StringComparison.Ordinal);
        Assert.Equal(0, service.EntityCount);
    }

    /// <summary>即使資料表為空，未知運算子也必須具名拒絕，避免未支援語法被當成查無資料。</summary>
    [Fact]
    public void Fake_UnsupportedQuery_ThrowsNamedFeature()
    {
        using var service = new FakeOrganizationService();
        var query = new QueryExpression("contact");
        query.Criteria.AddCondition("name", ConditionOperator.LastXDays, 7);
        var error = Assert.Throws<NotSupportedException>(() => service.RetrieveMultiple(query));
        Assert.Contains("LastXDays", error.Message, StringComparison.Ordinal);
    }

    /// <summary>建立與讀取均須複製 Money、lookup、巢狀實體與陣列，修改來源或結果不能回寫表。</summary>
    [Fact]
    public void Fake_Crud_DetachesMutableGraphs()
    {
        using var service = new FakeOrganizationService();
        var source = new Entity("contact")
        {
            ["name"] = "synthetic",
            ["amount"] = new Money(12m),
            ["parent"] = new EntityReference("account", Guid.NewGuid()) { Name = "before" },
            ["bytes"] = new byte[] { 1, 2 },
            ["child"] = new Entity("child") { ["choice"] = new OptionSetValue(3) }
        };
        var id = service.Create(source);
        ((Money)source["amount"]).Value = 99;
        var first = service.Retrieve("contact", id, new ColumnSet(true));
        Assert.Equal(12m, ((Money)first["amount"]).Value);
        ((EntityReference)first["parent"]).Name = "after";
        ((byte[])first["bytes"])[0] = 9;
        ((OptionSetValue)((Entity)first["child"])["choice"]).Value = 8;
        var second = service.Retrieve("contact", id, new ColumnSet(true));
        Assert.Equal("before", ((EntityReference)second["parent"]).Name);
        Assert.Equal(1, ((byte[])second["bytes"])[0]);
        Assert.Equal(3, ((OptionSetValue)((Entity)second["child"])["choice"]).Value);
        service.Update(new Entity("contact", id) { ["name"] = "updated", ["amount"] = null });
        var projected = service.Retrieve("contact", id, new ColumnSet("name", "amount"));
        Assert.Equal("updated", projected["name"]);
        Assert.False(projected.Contains("amount"));
        Assert.False(projected.Contains("parent"));
        service.Delete("contact", id);
        Assert.Throws<InvalidOperationException>(() => service.Retrieve("contact", id, new ColumnSet(true)));
    }

    /// <summary>不同正式識別即使同名仍全部保留；重複識別不能覆寫既有記錄。</summary>
    [Fact]
    public void Fake_Identity_DuplicateFailsButSameNameSurvives()
    {
        using var service = new FakeOrganizationService();
        var id = service.Create(new Entity("contact") { ["name"] = "same" });
        service.Create(new Entity("contact") { ["name"] = "same" });
        Assert.Throws<InvalidOperationException>(() => service.Create(new Entity("contact", id) { ["name"] = "changed" }));
        Assert.Equal(2, service.RetrieveMultiple(new QueryExpression("contact") { ColumnSet = new ColumnSet("name") }).Entities.Count);
        Assert.Equal("same", service.Retrieve("contact", id, new ColumnSet("name"))["name"]);
    }

    /// <summary>巢狀 OR、lookup 與 OptionSet 比較後先排序再 Top；不能依插入順序提早截斷。</summary>
    [Fact]
    public void Fake_Query_FiltersOrdersThenTakesTop()
    {
        using var service = new FakeOrganizationService();
        var parent = Guid.NewGuid();
        foreach (var rank in new[] { 2, 3, 1 })
            service.Create(new Entity("contact") { ["rank"] = rank, ["parent"] = new EntityReference("account", parent), ["state"] = new OptionSetValue(1) });
        var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("rank"), TopCount = 1 };
        query.Criteria.AddCondition("parent", ConditionOperator.Equal, parent);
        query.Criteria.AddCondition("state", ConditionOperator.Equal, 1);
        var nested = query.Criteria.AddFilter(LogicalOperator.Or);
        nested.AddCondition("rank", ConditionOperator.LessThan, 2);
        nested.AddCondition("rank", ConditionOperator.GreaterThan, 2);
        query.AddOrder("rank", OrderType.Descending);
        Assert.Equal(3, Assert.Single(service.RetrieveMultiple(query).Entities)["rank"]);
    }

    /// <summary>內連結只發佈匹配列；左連結保留無對應資料列，欄位使用 AliasedValue 且保持原主鍵。</summary>
    [Theory]
    [InlineData(JoinOperator.Inner, 1)]
    [InlineData(JoinOperator.LeftOuter, 2)]
    public void Fake_Query_LinksProjectDetachedAliases(JoinOperator join, int count)
    {
        using var service = new FakeOrganizationService();
        var account = service.Create(new Entity("account") { ["name"] = "parent" });
        var linked = service.Create(new Entity("contact") { ["parent"] = new EntityReference("account", account) });
        service.Create(new Entity("contact"));
        var query = new QueryExpression("contact") { ColumnSet = new ColumnSet(false) };
        var link = query.AddLink("account", "parent", "accountid", join);
        link.EntityAlias = "p";
        link.Columns = new ColumnSet("name");
        link.LinkCriteria.AddCondition("name", ConditionOperator.Equal, "parent");
        var rows = service.RetrieveMultiple(query).Entities;
        Assert.Equal(count, rows.Count);
        Assert.Equal("parent", ((AliasedValue)rows.Single(e => e.Id == linked)["p.name"]).Value);
        Assert.All(rows.Where(e => e.Id != linked), e => Assert.False(e.Contains("p.name")));
    }

    /// <summary>同等 FetchXML 篩選與排序的結果必須與 QueryExpression 相同，未知 XML 屬性不能忽略。</summary>
    [Fact]
    public void Fake_Fetch_EquivalentSubsetAndUnknownFeature()
    {
        using var service = new FakeOrganizationService();
        service.Create(new Entity("contact") { ["name"] = "beta", ["rank"] = 2 });
        var id = service.Create(new Entity("contact") { ["name"] = "alpha", ["rank"] = 1 });
        var fetch = new FetchExpression("<fetch top='1'><entity name='contact'><attribute name='name'/><filter><condition attribute='rank' operator='gt' value='0'/></filter><order attribute='name'/></entity></fetch>");
        Assert.Equal(id, Assert.Single(service.RetrieveMultiple(fetch).Entities).Id);
        var error = Assert.Throws<NotSupportedException>(() => service.RetrieveMultiple(new FetchExpression("<fetch aggregate='true'><entity name='contact'/></fetch>")));
        Assert.Contains("aggregate", error.Message, StringComparison.Ordinal);
    }

    /// <summary>常用 typed Execute 回應保持 SDK 型別並使用同一資料表，不可假成功或默默接受未知 message。</summary>
    [Fact]
    public void Fake_Execute_TypedCrudAndUnknownRequest()
    {
        using var service = new FakeOrganizationService();
        var created = (CreateResponse)service.Execute(new CreateRequest { Target = new Entity("contact") { ["name"] = "created" } });
        service.Execute(new UpdateRequest { Target = new Entity("contact", created.id) { ["name"] = "updated" } });
        var result = (RetrieveResponse)service.Execute(new RetrieveRequest { Target = new EntityReference("contact", created.id), ColumnSet = new ColumnSet("name") });
        Assert.Equal("updated", result.Entity["name"]);
        service.Execute(new DeleteRequest { Target = new EntityReference("contact", created.id) });
        Assert.Empty(service.RetrieveMultiple(new QueryExpression("contact")).Entities);
        var error = Assert.Throws<NotSupportedException>(() => service.Execute(new OrganizationRequest("UnknownSynthetic")));
        Assert.Contains("UnknownSynthetic", error.Message, StringComparison.Ordinal);
    }

    /// <summary>既有 callback 必須優先並保留原例外，Calls 仍只計算外部呼叫次數。</summary>
    [Fact]
    public void Fake_Callbacks_PreserveFaultAndCounts()
    {
        var fault = new InvalidOperationException("synthetic");
        using var service = new FakeOrganizationService { RetrieveMultipleHandler = _ => throw fault };
        Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => service.RetrieveMultiple(new QueryExpression("contact"))));
        Assert.Equal(1, service.Calls[nameof(service.RetrieveMultiple)]);
    }

    /// <summary>逐一保護純量比較的正反結果；使用固定整數值驗證運算子，不依賴表列舉順序。</summary>
    [Theory]
    [InlineData(ConditionOperator.Equal, false)]
    [InlineData(ConditionOperator.NotEqual, true)]
    [InlineData(ConditionOperator.GreaterThan, true)]
    [InlineData(ConditionOperator.GreaterEqual, true)]
    [InlineData(ConditionOperator.LessThan, false)]
    [InlineData(ConditionOperator.LessEqual, false)]
    [InlineData(ConditionOperator.In, false)]
    [InlineData(ConditionOperator.NotIn, true)]
    public void Fake_Query_ScalarOperators(ConditionOperator operation, bool expected)
    {
        using var service = new FakeOrganizationService();
        service.Create(new Entity("contact") { ["rank"] = 3 });
        var query = new QueryExpression("contact");
        query.Criteria.AddCondition("rank", operation, 2);
        Assert.Equal(expected ? 1 : 0, service.RetrieveMultiple(query).Entities.Count);
    }

    /// <summary>常用區間與字串運算必須正反可判定；Like 的百分比、底線與字面符號不是任意 regex。</summary>
    [Theory]
    [InlineData(ConditionOperator.Like, "a_ph%", true)]
    [InlineData(ConditionOperator.Like, "alpha.", false)]
    [InlineData(ConditionOperator.NotLike, "b%", true)]
    [InlineData(ConditionOperator.BeginsWith, "AL", true)]
    [InlineData(ConditionOperator.EndsWith, "HA", true)]
    [InlineData(ConditionOperator.Equal, "ALPHA", true)]
    [InlineData(ConditionOperator.NotEqual, "alpha", false)]
    public void Fake_Query_TextOperators(ConditionOperator operation, string pattern, bool expected)
    {
        using var service = new FakeOrganizationService();
        service.Create(new Entity("contact") { ["name"] = "alpha" });
        var query = new QueryExpression("contact");
        query.Criteria.AddCondition("name", operation, pattern);
        Assert.Equal(expected ? 1 : 0, service.RetrieveMultiple(query).Entities.Count);
    }

    /// <summary>缺欄位只由 Null 命中；非等於與 NotIn 不得把 null 誤當普通值，區間端點包含邊界。</summary>
    [Fact]
    public void Fake_Query_NullAndRangesHaveExplicitSemantics()
    {
        using var service = new FakeOrganizationService();
        var missing = service.Create(new Entity("contact"));
        var present = service.Create(new Entity("contact") { ["rank"] = 3 });
        var query = new QueryExpression("contact");
        query.Criteria.AddCondition("rank", ConditionOperator.Null);
        Assert.Equal(missing, Assert.Single(service.RetrieveMultiple(query).Entities).Id);
        query.Criteria.Conditions.Clear();
        query.Criteria.AddCondition("rank", ConditionOperator.NotNull);
        query.Criteria.AddCondition("rank", ConditionOperator.Between, 3, 4);
        Assert.Equal(present, Assert.Single(service.RetrieveMultiple(query).Entities).Id);
        query.Criteria.Conditions.Clear();
        query.Criteria.AddCondition("rank", ConditionOperator.NotBetween, 1, 2);
        Assert.Equal(present, Assert.Single(service.RetrieveMultiple(query).Entities).Id);
        query.Criteria.Conditions.Clear();
        query.Criteria.AddCondition("rank", ConditionOperator.NotEqual, 2);
        Assert.Equal(present, Assert.Single(service.RetrieveMultiple(query).Entities).Id);
    }

    /// <summary>排序 null 在前，同值由主鍵決定；兩欄排序及 TopCount 不得移除不同 ID 的同名資料。</summary>
    [Fact]
    public void Fake_Query_MultiColumnOrderingAndZeroTop()
    {
        using var service = new FakeOrganizationService();
        var nullId = service.Create(new Entity("contact") { ["rank"] = 9 });
        service.Create(new Entity("contact") { ["name"] = "same", ["rank"] = 1 });
        var highest = service.Create(new Entity("contact") { ["name"] = "same", ["rank"] = 2 });
        var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("rank") };
        query.AddOrder("name", OrderType.Ascending);
        query.AddOrder("rank", OrderType.Descending);
        var rows = service.RetrieveMultiple(query).Entities;
        Assert.Equal(nullId, rows[0].Id);
        Assert.Equal(highest, rows[1].Id);
        query.TopCount = 0;
        Assert.Empty(service.RetrieveMultiple(query).Entities);
    }

    /// <summary>ON 篩選及根 alias 篩選不能混用時序；左連結未命中時根 Null 條件保留該列。</summary>
    [Fact]
    public void Fake_Query_LeftLinkAliasNullAndNestedInner()
    {
        using var service = new FakeOrganizationService();
        var region = service.Create(new Entity("region") { ["name"] = "north" });
        var account = service.Create(new Entity("account") { ["region"] = new EntityReference("region", region), ["name"] = "parent" });
        var linked = service.Create(new Entity("contact") { ["parent"] = new EntityReference("account", account) });
        var unlinked = service.Create(new Entity("contact"));
        var query = new QueryExpression("contact");
        var link = query.AddLink("account", "parent", "accountid", JoinOperator.LeftOuter);
        link.EntityAlias = "p";
        link.Columns = new ColumnSet("name");
        // 明列巢狀連結的來源表；此 SDK 的 AddLink helper 會沿用上層來源名稱。
        var nested = new LinkEntity("account", "region", "region", "regionid", JoinOperator.Inner);
        link.LinkEntities.Add(nested);
        nested.EntityAlias = "r";
        nested.Columns = new ColumnSet("name");
        var rows = service.RetrieveMultiple(query).Entities;
        Assert.Equal("north", ((AliasedValue)rows.Single(row => row.Id == linked)["r.name"]).Value);
        Assert.False(rows.Single(row => row.Id == unlinked).Contains("r.name"));
        query.Criteria.AddCondition(new ConditionExpression("p", "name", ConditionOperator.Null));
        Assert.Equal(unlinked, Assert.Single(service.RetrieveMultiple(query).Entities).Id);
        query.Criteria.Conditions.Clear();
        link.LinkCriteria.AddCondition("name", ConditionOperator.Equal, "different");
        Assert.All(service.RetrieveMultiple(query).Entities, row => Assert.False(row.Contains("p.name")));
    }

    /// <summary>一對多連結造成同一主列重複時拒絕發布；TopCount 亦不能掩蓋資料組裝衝突。</summary>
    [Fact]
    public void Fake_Query_FanOutRejectsDuplicatePublishedIdentity()
    {
        using var service = new FakeOrganizationService();
        var account = service.Create(new Entity("account"));
        service.Create(new Entity("contact") { ["parent"] = new EntityReference("account", account) });
        service.Create(new Entity("contact") { ["parent"] = new EntityReference("account", account) });
        var query = new QueryExpression("account") { TopCount = 1 };
        query.AddLink("contact", "accountid", "parent", JoinOperator.Inner);
        Assert.Contains("duplicate identity", Assert.Throws<InvalidOperationException>(() => service.RetrieveMultiple(query)).Message, StringComparison.Ordinal);
    }

    /// <summary>查詢驗證必須先於空表與 OR 短路；distinct、分頁、進階 join 與欄位比較不能靜默忽略。</summary>
    [Theory]
    [InlineData("Distinct")]
    [InlineData("PageInfo")]
    [InlineData("Natural")]
    [InlineData("CompareColumns")]
    [InlineData("LastXDays")]
    [InlineData("BracketPattern")]
    [InlineData("DuplicateAlias")]
    [InlineData("OrderAlias")]
    public void Fake_Query_UnsupportedFeaturesFailEvenWhenEmpty(string feature)
    {
        using var service = new FakeOrganizationService();
        var query = new QueryExpression("contact");
        switch (feature)
        {
            case "Distinct": query.Distinct = true; break;
            case "PageInfo": query.PageInfo.Count = 5; break;
            case "Natural": query.AddLink("account", "parent", "accountid", JoinOperator.Natural); break;
            case "CompareColumns":
                query.Criteria.AddCondition(new ConditionExpression("name", ConditionOperator.Equal, true, "other")); break;
            case "LastXDays":
                query.Criteria.FilterOperator = LogicalOperator.Or;
                query.Criteria.AddCondition("name", ConditionOperator.Null);
                query.Criteria.AddCondition("createdon", ConditionOperator.LastXDays, 7); break;
            case "BracketPattern": query.Criteria.AddCondition("name", ConditionOperator.Like, "[a-z]"); break;
            case "DuplicateAlias": query.AddLink("account", "parent", "accountid").EntityAlias = "contact"; break;
            case "OrderAlias": query.Orders.Add(new OrderExpression("name", OrderType.Ascending) { Alias = "unsupported" }); break;
        }
        var error = Assert.Throws<NotSupportedException>(() => service.RetrieveMultiple(query));
        Assert.Contains(feature == "OrderAlias" ? "Alias" : feature, error.Message, StringComparison.Ordinal);
    }

    /// <summary>FetchXML 與 QueryExpression 的 link、巢狀 OR、GUID、bool 與排序共用語意，結果欄位逐一相同。</summary>
    [Fact]
    public void Fake_Fetch_LinksAndTypedValuesMatchQueryExpression()
    {
        using var service = new FakeOrganizationService();
        var account = service.Create(new Entity("account") { ["name"] = "synthetic" });
        var id = service.Create(new Entity("contact")
        {
            ["parent"] = new EntityReference("account", account),
            ["active"] = false, ["amount"] = new Money(15m), ["state"] = new OptionSetValue(2)
        });
        service.Create(new Entity("contact") { ["active"] = true });
        var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("amount") };
        query.Criteria.AddCondition("active", ConditionOperator.Equal, false);
        var alternative = query.Criteria.AddFilter(LogicalOperator.Or);
        alternative.AddCondition("parent", ConditionOperator.Equal, account);
        alternative.AddCondition("state", ConditionOperator.Equal, 99);
        var link = query.AddLink("account", "parent", "accountid", JoinOperator.LeftOuter);
        link.EntityAlias = "p"; link.Columns = new ColumnSet("name");
        var fetch = new FetchExpression("<fetch mapping='logical' version='1.0'><entity name='contact'><attribute name='amount'/>" +
            "<filter><condition attribute='active' operator='eq' value='0'/><filter type='or'>" +
            "<condition attribute='parent' operator='eq' value='" + account.ToString("D") + "'/>" +
            "<condition attribute='state' operator='eq' value='99'/></filter></filter>" +
            "<link-entity name='account' from='accountid' to='parent' alias='p' link-type='outer'><attribute name='name'/></link-entity>" +
            "</entity></fetch>");
        var expected = Assert.Single(service.RetrieveMultiple(query).Entities);
        var actual = Assert.Single(service.RetrieveMultiple(fetch).Entities);
        Assert.Equal(id, actual.Id);
        Assert.Equal(((Money)expected["amount"]).Value, ((Money)actual["amount"]).Value);
        Assert.Equal(((AliasedValue)expected["p.name"]).Value, ((AliasedValue)actual["p.name"]).Value);
    }

    /// <summary>非法／DTD XML 與未知元素或屬性均具名拒絕；外部實體不會發出檔案或網路讀取。</summary>
    [Theory]
    [InlineData("<fetch distinct='true'><entity name='contact'/></fetch>", "distinct")]
    [InlineData("<fetch><entity name='contact'><attribute name='name' alias='x'/></entity></fetch>", "alias")]
    [InlineData("<fetch><entity name='contact'><order attribute='name'><unknown/></order></entity></fetch>", "Nested")]
    [InlineData("<fetch><entity name='contact'><filter><condition attribute='name' operator='yesterday'/></filter></entity></fetch>", "yesterday")]
    [InlineData("<fetch><entity name='contact'/><entity name='account'/></fetch>", "entity")]
    [InlineData("<fetch><entity name='contact'>unexpected</entity></fetch>", "Text")]
    [InlineData("<fetch xmlns='unsupported'><entity name='contact'/></fetch>", "Element")]
    [InlineData("<fetch><entity", "MalformedOrDtd")]
    [InlineData("<!DOCTYPE fetch [<!ENTITY x SYSTEM 'file:///not-read'>]><fetch><entity name='contact'/></fetch>", "MalformedOrDtd")]
    public void Fake_Fetch_UnsupportedFeaturesAreNamed(string xml, string feature)
    {
        using var service = new FakeOrganizationService();
        var error = Assert.Throws<NotSupportedException>(() => service.RetrieveMultiple(new FetchExpression(xml)));
        Assert.Contains(feature, error.Message, StringComparison.Ordinal);
    }

    /// <summary>未知查詢只在指定序號被 queryResults 覆蓋；配置與回應兩側都分離，其餘序號仍拒絕。</summary>
    [Fact]
    public void Fake_QueryResults_ExplicitOverrideIsDetachedAndScopedToCall()
    {
        var seed = new Entity("contact", Guid.NewGuid()) { ["amount"] = new Money(2m) };
        var answer = new EntityCollection(); answer.Entities.Add(seed);
        using var service = new FakeOrganizationService();
        service.SetQueryResult(1, answer);
        ((Money)seed["amount"]).Value = 9;
        var unknown = new QueryByAttribute("contact");
        var first = service.RetrieveMultiple(unknown);
        Assert.Equal(2m, ((Money)Assert.Single(first.Entities)["amount"]).Value);
        service.SetQueryResult(2, first);
        ((Money)first.Entities[0]["amount"]).Value = 8;
        Assert.Equal(2m, ((Money)Assert.Single(service.RetrieveMultiple(unknown).Entities)["amount"]).Value);
        Assert.Contains("QueryByAttribute", Assert.Throws<NotSupportedException>(() => service.RetrieveMultiple(unknown)).Message, StringComparison.Ordinal);
        Assert.Equal(0, service.EntityCount);
    }

    /// <summary>queryResults 不能發布空或重複正式識別，也不能以顯示名稱把衝突悄悄合併。</summary>
    [Fact]
    public void Fake_QueryResults_RejectsInvalidIdentities()
    {
        using var service = new FakeOrganizationService();
        var answer = new EntityCollection();
        answer.Entities.Add(new Entity("contact"));
        Assert.Throws<InvalidOperationException>(() => service.SetQueryResult(1, answer));
        var id = Guid.NewGuid();
        answer.Entities.Clear();
        answer.Entities.Add(new Entity("contact", id));
        answer.Entities.Add(new Entity("contact", id));
        Assert.Throws<InvalidOperationException>(() => service.SetQueryResult(1, answer));
    }

    /// <summary>關聯全批驗證後發布，重複或缺端點不留下部分邊；刪除實體確定性清除指向它的關聯。</summary>
    [Fact]
    public void Fake_Associations_AreAtomicAndDeleteDrainsEdges()
    {
        using var service = new FakeOrganizationService();
        var parent = service.Create(new Entity("account"));
        var child = service.Create(new Entity("contact"));
        var other = service.Create(new Entity("contact"));
        var relationship = new Relationship("synthetic_members");
        var reference = new EntityReference("contact", child);
        service.Associate("account", parent, relationship, new EntityReferenceCollection { reference });
        Assert.True(service.IsAssociated("account", parent, relationship.SchemaName, reference));
        Assert.Throws<InvalidOperationException>(() => service.Associate("account", parent, relationship,
            new EntityReferenceCollection { new("contact", other), reference }));
        Assert.Equal(1, service.AssociationCount);
        service.Disassociate("account", parent, relationship, new EntityReferenceCollection { reference });
        Assert.Equal(0, service.AssociationCount);
        Assert.Throws<InvalidOperationException>(() => service.Associate("account", parent, relationship,
            new EntityReferenceCollection { reference, new("contact", Guid.NewGuid()) }));
        Assert.Equal(0, service.AssociationCount);
        service.Execute(new AssociateRequest { Target = new EntityReference("account", parent), Relationship = relationship, RelatedEntities = new EntityReferenceCollection { reference } });
        service.Execute(new DisassociateRequest { Target = new EntityReference("account", parent), Relationship = relationship, RelatedEntities = new EntityReferenceCollection { reference } });
        service.Associate("account", parent, relationship, new EntityReferenceCollection { reference });
        service.Delete("contact", child);
        Assert.Equal(0, service.AssociationCount);
    }

    /// <summary>Upsert 僅以正式 GUID 決定新增／更新；RetrieveMultiple typed response 與表共用 detached 結果。</summary>
    [Fact]
    public void Fake_Execute_UpsertAndRetrieveMultipleUseTypedResponses()
    {
        using var service = new FakeOrganizationService();
        var id = Guid.NewGuid();
        var created = (UpsertResponse)service.Execute(new UpsertRequest { Target = new Entity("contact", id) { ["name"] = "before" } });
        Assert.True(created.RecordCreated);
        Assert.Equal(id, created.Target.Id);
        var updated = (UpsertResponse)service.Execute(new UpsertRequest { Target = new Entity("contact", id) { ["name"] = "after" } });
        Assert.False(updated.RecordCreated);
        var response = (RetrieveMultipleResponse)service.Execute(new RetrieveMultipleRequest { Query = new QueryExpression("contact") { ColumnSet = new ColumnSet("name") } });
        Assert.Equal("after", Assert.Single(response.EntityCollection.Entities)["name"]);
        var unsupported = new RetrieveRequest { Target = new EntityReference("contact", id), ColumnSet = new ColumnSet(true) };
        unsupported.Parameters["RelatedEntitiesQuery"] = new RelationshipQueryCollection();
        Assert.Contains("RelatedEntitiesQuery", Assert.Throws<NotSupportedException>(() => service.Execute(unsupported)).Message, StringComparison.Ordinal);
    }

    /// <summary>未知可變值、循環資料、缺列及主鍵衝突都拒絕；失敗補丁不能先寫入合法部分欄位。</summary>
    [Fact]
    public void Fake_Crud_InvalidPatchLeavesPriorSnapshot()
    {
        using var service = new FakeOrganizationService();
        var id = service.Create(new Entity("contact") { ["name"] = "before" });
        Assert.Throws<NotSupportedException>(() => service.Update(new Entity("contact", id) { ["name"] = "after", ["unsupported"] = new object() }));
        Assert.Equal("before", service.Retrieve("contact", id, new ColumnSet("name"))["name"]);
        Assert.Throws<InvalidOperationException>(() => service.Update(new Entity("contact", id) { ["contactid"] = Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() => service.Update(new Entity("contact", Guid.NewGuid())));
        var cyclic = new Entity("contact"); cyclic["self"] = cyclic;
        Assert.Throws<NotSupportedException>(() => service.Create(cyclic));
        Assert.Equal(1, service.EntityCount);
    }

    /// <summary>併行 A／B 使用相同 GUID 仍不能交叉讀取；呼叫排空後 Dispose 清除資料、覆蓋、回呼與計數。</summary>
    [Fact]
    public void Fake_ConcurrentInstances_IsolateAndDisposeDrains()
    {
        using var first = new FakeOrganizationService();
        using var second = new FakeOrganizationService();
        var id = Guid.NewGuid();
        first.Create(new Entity("contact", id) { ["name"] = "A" });
        second.Create(new Entity("contact", id) { ["name"] = "B" });
        Parallel.For(0, 200, index =>
        {
            var service = index % 2 == 0 ? first : second;
            var name = index % 2 == 0 ? "A" : "B";
            service.Update(new Entity("contact", id) { ["name"] = name });
            Assert.Equal(name, service.Retrieve("contact", id, new ColumnSet("name"))["name"]);
        });
        first.SetQueryResult(1, new EntityCollection());
        first.RetrieveHandler = (_, _, _) => new Entity("contact", id);
        first.Dispose();
        Assert.Equal(0, first.EntityCount);
        Assert.Equal(0, first.AssociationCount);
        Assert.Empty(first.Calls);
        Assert.Equal(0, first.QueryResultCount);
        Assert.Null(first.RetrieveHandler);
        Assert.Throws<ObjectDisposedException>(() => first.RetrieveMultiple(new QueryExpression("contact")));
        Assert.Throws<ObjectDisposedException>(() => first.SetQueryResult(2, new EntityCollection()));
        Assert.Equal("B", second.Retrieve("contact", id, new ColumnSet("name"))["name"]);
    }
}
