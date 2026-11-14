/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-01 Shadow 記錄器的完整寫入投影、Create→Update overlay、
 * 明確 Execute 白名單、深複製隔離、隱私及有界生命週期，不連 Dataverse 或 LINE。
 * 主要功能與流程：以案例獨占的 FakeOrganizationService 觀察零寫入及讀取轉送；注入
 * 未知 request、未知可變欄位、超量寫入與推播，驗證 fail closed、總數及排空。完整
 * JSON 行總長含前綴與 CRLF 不超過 8000，overflow.total 記全部意圖，end.count 記
 * 實際輸出意圖。巢狀 SDK 值及 caller mutation 驗證快照不是淺複製或共用可變參考。
 * 使用邊界與維護：資料均為合成 GUID、文字與摘要，secret 標籤只是負向測試字串；
 * 所有服務與 recorder 由 using owner 確定清理。壓力案例僅證明有界保留及排空，
 * 不宣稱實機容量、授權、傳送或長時間 profiler 已驗收；失敗時以決定性斷言定位契約。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Infrastructure;

/// <summary>以離線替身驗證 Shadow 的寫入攔截、資料隔離、輸出與資源容量契約。</summary>
public sealed class RecordingOrganizationServiceTests
{
    /// <summary>Create 及 patch 均須完整記錄，巢狀值修改不回滲，不合併真查詢也不執行真寫入。</summary>
    [Fact]
    public void Recording_CreateThenUpdate_ServedFromOverlay()
    {
        using var inner = new FakeOrganizationService { RetrieveMultipleHandler = _ => new EntityCollection() };
        using var recorder = new RecordingOrganizationService(inner);
        var source = new Entity("task") { ["new_status"] = "one", ["amount"] = new Money(10), ["blob"] = new byte[] { 1, 2 } };
        var id = recorder.Create(source);
        ((Money)source["amount"]).Value = 100;
        ((byte[])source["blob"])[0] = 99;
        var patch = new Entity("task", id) { ["new_status"] = "two" };
        recorder.Update(patch);
        patch["new_status"] = "caller changed patch";
        var row = recorder.Retrieve("task", id, new ColumnSet(true));
        Assert.Equal("two", row["new_status"]);
        Assert.Equal(10m, ((Money)row["amount"]).Value);
        Assert.Equal(1, ((byte[])row["blob"])[0]);
        ((Money)row["amount"]).Value = 200;
        Assert.Equal(10m, ((Money)recorder.Retrieve("task", id, new ColumnSet("amount"))["amount"]).Value);
        Assert.Single(recorder.Retrieve("task", id, new ColumnSet("amount")).Attributes);
        Assert.Equal("Create,Update", string.Join(",", recorder.Writes.Select(write => write.Operation)));
        Assert.Equal("one", JObject.Parse(recorder.Writes[0].ProjectionJson)["fields"]!["new_status"]!.Value<string>());
        Assert.Equal("two", JObject.Parse(recorder.Writes[1].ProjectionJson)["fields"]!["new_status"]!.Value<string>());
        Assert.Empty(recorder.RetrieveMultiple(new QueryExpression("task")).Entities);
        Assert.Equal(1, inner.Calls["RetrieveMultiple"]);
        Assert.DoesNotContain(inner.Calls.Keys, key => key is "Create" or "Update" or "Delete" or "Execute");
    }

    /// <summary>未知寫入、偽裝成 Retrieve 的名稱及錯誤參數必須拒絕，不回顯秘密，不碰 inner。</summary>
    [Fact]
    public void Recording_UnknownWriteRequest_FailsClosed()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        foreach (var name in new[] { "CustomDangerousWrite", "RetrieveAndDelete", "ExecuteMultiple", "Upsert", "secret-request-value" })
        {
            var error = Assert.Throws<NotSupportedException>(() => recorder.Execute(new OrganizationRequest(name)));
            Assert.DoesNotContain(name, error.Message, StringComparison.Ordinal);
        }
        Assert.Throws<NotSupportedException>(() => recorder.Execute(new OrganizationRequest("Create")));
        Assert.Empty(inner.Calls);
        Assert.Empty(recorder.Writes);
        Assert.Equal(0, recorder.TotalCount);
    }

    /// <summary>大量合成寫入觸發預算截斷，完整行與所有 marker／CRLF 合計至多 8000 字元。</summary>
    [Fact]
    public void Recording_LargePayload_SplitIntoChunks()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        using var pushes = new ShadowPushRecorder();
        for (var index = 0; index < 1000; index++)
            recorder.Update(new Entity("task", Guid.NewGuid()) { ["new_status"] = new string('x', 120) });
        pushes.Record(Guid.NewGuid(), "Template.Test", Guid.NewGuid(), 1, new string('a', 64));
        var tracing = new FakeTracingService();
        new ShadowRecordWriter(tracing).Write(recorder, pushes);
        var rows = Parse(tracing);
        Assert.InRange(tracing.Entries.Sum(entry => entry.Length + 2), 1, 8000);
        Assert.True(rows.Count > 2);
        Assert.True(rows[rows.Count - 2]["overflow"]!.Value<bool>());
        Assert.Equal(1001, rows[rows.Count - 2]["total"]!.Value<long>());
        Assert.True(rows.Last()["end"]!.Value<bool>());
        Assert.Equal(rows.Count - 2, rows.Last()["count"]!.Value<int>());
        Assert.Equal(Enumerable.Range(1, rows.Count - 2), rows.Take(rows.Count - 2).Select(row => row["seq"]!.Value<int>()));
        Assert.True(recorder.Overflow);
        Assert.InRange(recorder.Writes.Count, 1, 128);
        Assert.True(recorder.Writes.Sum(write => write.ProjectionJson.Length) <= 16000);
        Assert.Empty(inner.Calls);
    }

    /// <summary>建立序號不受 Update／Delete 的意圖數影響，且不同 recorder 不分享可變序號。</summary>
    [Fact]
    public void Recording_CreateSequence_DeterministicAndIsolated()
    {
        using var inner = new FakeOrganizationService();
        using var first = new RecordingOrganizationService(inner);
        using var second = new RecordingOrganizationService(inner);
        var a = first.Create(new Entity("task"));
        first.Update(new Entity("task", a) { ["new_status"] = 1 });
        var b = first.Create(new Entity("task"));
        Assert.Equal(a, second.Create(new Entity("task")));
        Assert.Equal(b, second.Create(new Entity("task")));
        Assert.NotEqual(a, b);
        Assert.Throws<InvalidOperationException>(() => first.Create(new Entity("task", a)));
        first.Delete("task", a);
        Assert.Throws<InvalidOperationException>(() => first.Retrieve("task", a, new ColumnSet(true)));
        Assert.Empty(inner.Calls);
    }

    /// <summary>已知 CRUD／關聯 Execute 走記錄器，回應型別及真實 related reference 身分可比對。</summary>
    [Fact]
    public void Recording_KnownExecuteWrites_RecordedWithoutProviderWrites()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        var created = (CreateResponse)recorder.Execute(new CreateRequest { Target = new Entity("task") { ["new_status"] = 1 } });
        recorder.Execute(new UpdateRequest { Target = new Entity("task", created.id) { ["new_status"] = 2 } });
        var retrieved = (RetrieveResponse)recorder.Execute(new RetrieveRequest { Target = new EntityReference("task", created.id), ColumnSet = new ColumnSet("new_status") });
        Assert.Equal(2, retrieved.Entity["new_status"]);
        var relatedId = Guid.NewGuid();
        var related = new EntityReferenceCollection { new("contact", relatedId) { Name = "private-name" } };
        recorder.Execute(new AssociateRequest { Target = new EntityReference("task", created.id), Relationship = new Relationship("task_contact"), RelatedEntities = related });
        related[0].Id = Guid.NewGuid();
        var projection = JObject.Parse(recorder.Writes[recorder.Writes.Count - 1].ProjectionJson);
        Assert.Equal("Associate", projection["operation"]!.Value<string>());
        Assert.Equal("task_contact", projection["relationship"]!.Value<string>());
        Assert.Equal(relatedId, Guid.Parse(projection["related"]![0]!["id"]!.Value<string>()!));
        Assert.DoesNotContain("private-name", projection.ToString(), StringComparison.Ordinal);
        recorder.Execute(new DisassociateRequest { Target = new EntityReference("task", created.id), Relationship = new Relationship("task_contact"), RelatedEntities = related });
        recorder.Execute(new DeleteRequest { Target = new EntityReference("task", created.id) });
        Assert.Equal("Create,Update,Associate,Disassociate,Delete", string.Join(",", recorder.Writes.Select(write => write.Operation)));
        Assert.Empty(inner.Calls);
    }

    /// <summary>明列唯讀訊息才會轉送，回應保持 provider 契約；其他名稱已由拒絕案例保護。</summary>
    [Theory]
    [InlineData("WhoAmI")]
    [InlineData("RetrieveCurrentOrganization")]
    [InlineData("RetrievePrincipalAccess")]
    [InlineData("RetrieveEntity")]
    [InlineData("RetrieveAttribute")]
    [InlineData("RetrieveAllEntities")]
    [InlineData("RetrieveRelationship")]
    [InlineData("RetrieveMetadataChanges")]
    public void Recording_ReadAllowlist_Delegates(string name)
    {
        var response = new OrganizationResponse();
        using var inner = new FakeOrganizationService { ExecuteHandler = _ => response };
        using var recorder = new RecordingOrganizationService(inner);
        Assert.Same(response, recorder.Execute(new OrganizationRequest(name)));
        Assert.Equal(1, inner.Calls["Execute"]);
        Assert.Empty(recorder.Writes);
    }

    /// <summary>秘密／LINE ID／訊息原文省略；個資、長值、二進位只留正確長度與 SHA-256。</summary>
    [Fact]
    public void Recording_Privacy_FieldsAndNestedValuesSanitized()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        using var pushes = new ShadowPushRecorder();
        var longValue = new string('x', 121);
        var entity = new Entity("contact", Guid.NewGuid())
        {
            ["token"] = "synthetic-token", ["tokenKey"] = "synthetic-key", ["new_lineid"] = "synthetic-line",
            ["new_content"] = "synthetic-content", ["firstname"] = "synthetic-person", ["telephone1"] = "synthetic-phone",
            ["emailaddress1"] = "synthetic-email", ["address1_city"] = "synthetic-address", ["new_status"] = "ok",
            ["new_long"] = longValue, ["new_other"] = "U" + new string('a', 32), ["new_blob"] = new byte[] { 1, 2, 3 },
            ["new_alias"] = new AliasedValue("contact", "token", "synthetic-alias-secret"),
            ["new_nested"] = new EntityCollection(new[] { new Entity("task") { ["new_body"] = "nested-private", ["new_status"] = 5 } })
        };
        recorder.Update(entity);
        entity["new_status"] = "changed after record";
        var tracing = new FakeTracingService();
        new ShadowRecordWriter(tracing).Write(recorder, pushes);
        var output = string.Join("\r\n", tracing.Entries);
        foreach (var value in new[] { "synthetic-token", "synthetic-key", "synthetic-line", "synthetic-content", "synthetic-person", "synthetic-phone", "synthetic-email", "synthetic-address", "synthetic-alias-secret", "nested-private", longValue, "U" + new string('a', 32) })
            Assert.DoesNotContain(value, output, StringComparison.Ordinal);
        var fields = Parse(tracing)[0]["fields"]!;
        Assert.Equal("ok", fields["new_status"]!.Value<string>());
        Assert.Equal("5", fields["new_nested"]![0]!["fields"]!["new_status"]!.ToString());
        Assert.Equal(121, fields["new_long"]!["length"]!.Value<int>());
        Assert.Equal(Digest(longValue), fields["new_long"]!["sha256"]!.Value<string>());
        Assert.Equal(Digest("synthetic-person"), fields["firstname"]!["sha256"]!.Value<string>());
        Assert.Equal(3, fields["new_blob"]!["length"]!.Value<int>());
        Assert.Null(fields["token"]);
        Assert.Null(fields["new_alias"]);
        Assert.True(Parse(tracing).Last()["end"]!.Value<bool>());
    }

    /// <summary>短輸出仍為完整版本行，推播只含必要摘要；重複完成拒絕，避免整次預算重複。</summary>
    [Fact]
    public void Recording_Writer_SmallBatchAndPushHaveCompleteEndMarker()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        using var pushes = new ShadowPushRecorder();
        recorder.Update(new Entity("task", Guid.NewGuid()) { ["new_status"] = 1 });
        pushes.Record(Guid.NewGuid(), "Template.Test", Guid.NewGuid(), 2, new string('a', 64));
        var tracing = new FakeTracingService();
        var writer = new ShadowRecordWriter(tracing);
        writer.Write(recorder, pushes);
        var rows = Parse(tracing);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Push", rows[1]["operation"]!.Value<string>());
        Assert.Equal(2, rows[1]["messageCount"]!.Value<int>());
        Assert.Equal(new string('a', 64), rows[1]["contentSha256"]!.Value<string>());
        Assert.Equal(2, rows.Last()["count"]!.Value<int>());
        Assert.DoesNotContain(rows, row => row["overflow"] is not null);
        Assert.Throws<InvalidOperationException>(() => writer.Write(recorder, pushes));
        Assert.Equal(3, tracing.Entries.Count);
    }

    /// <summary>複製包含 reference key、集合、alias 及 SDK 可變 scalar 的圖形，caller 修改不回滲。</summary>
    [Fact]
    public void Recording_CloneEntity_DeepCopiesSdkValuesAndRejectsUnknownMutable()
    {
        var reference = new EntityReference("contact", Guid.NewGuid()) { Name = "display" };
        reference.KeyAttributes["externalid"] = new byte[] { 4 };
        var child = new Entity("task") { ["amount"] = new Money(7) };
        var source = new Entity("task", Guid.NewGuid())
        {
            ["reference"] = reference, ["options"] = new OptionSetValueCollection { new(1) },
            ["references"] = new EntityReferenceCollection { reference },
            ["alias"] = new AliasedValue("task", "amount", new Money(3)),
            ["children"] = new EntityCollection(new[] { child }), ["array"] = new[] { child }, ["option"] = new OptionSetValue(2)
        };
        source.KeyAttributes["externalid"] = new byte[] { 9 };
        var copy = RecordingOrganizationService.CloneEntity(source);
        ((byte[])((EntityReference)copy["reference"]).KeyAttributes["externalid"])[0] = 99;
        ((Money)((EntityCollection)copy["children"]).Entities[0]["amount"]).Value = 99;
        ((OptionSetValueCollection)copy["options"])[0].Value = 99;
        ((Money)((AliasedValue)copy["alias"]).Value).Value = 99;
        ((byte[])copy.KeyAttributes["externalid"])[0] = 99;
        Assert.Equal(4, ((byte[])reference.KeyAttributes["externalid"])[0]);
        Assert.Equal(7m, ((Money)child["amount"]).Value);
        Assert.Equal(1, ((OptionSetValueCollection)source["options"])[0].Value);
        Assert.Equal(3m, ((Money)((AliasedValue)source["alias"]).Value).Value);
        Assert.Equal(9, ((byte[])source.KeyAttributes["externalid"])[0]);
        source["unknown"] = new List<string> { "private" };
        Assert.Throws<NotSupportedException>(() => RecordingOrganizationService.CloneEntity(source));
        source.Attributes.Remove("unknown");
        source["cycle"] = source;
        Assert.Throws<NotSupportedException>(() => RecordingOrganizationService.CloneEntity(source));
    }

    /// <summary>overlay 容量超過時拒絕新列，保留既有讀回；Clear／Dispose 排空並拒絕已釋放 owner。</summary>
    [Fact]
    public void Recording_CapacityAndDispose_FailClosedAndDrain()
    {
        using var inner = new FakeOrganizationService();
        var recorder = new RecordingOrganizationService(inner);
        try
        {
            var first = recorder.Create(new Entity("task"));
            for (var index = 1; index < 64; index++) recorder.Create(new Entity("task"));
            Assert.Throws<InvalidOperationException>(() => recorder.Create(new Entity("task")));
            Assert.Equal(64, recorder.TotalCount);
            Assert.Equal(first, recorder.Retrieve("task", first, new ColumnSet(true)).Id);
            recorder.Clear();
            Assert.Empty(recorder.Writes);
            Assert.Equal(0, recorder.TotalCount);
            Assert.False(recorder.Overflow);
            Assert.Equal(first, recorder.Create(new Entity("task")));
            recorder.Dispose();
            Assert.Empty(recorder.Writes);
            Assert.Equal(0, recorder.TotalCount);
            Assert.Throws<ObjectDisposedException>(() => recorder.Update(new Entity("task", first)));
            Assert.Throws<ObjectDisposedException>(() => recorder.Retrieve("task", first, new ColumnSet(true)));
        }
        finally { recorder.Dispose(); }
    }

    /// <summary>反覆記錄大量意圖不突破保留上限，重設後回到零基線；不代表長時間實機 profiler。</summary>
    [Fact]
    public void Recording_RepeatedDrain_RetainsBoundedSummaries()
    {
        using var inner = new FakeOrganizationService();
        using var recorder = new RecordingOrganizationService(inner);
        using var pushes = new ShadowPushRecorder();
        var id = Guid.NewGuid();
        for (var run = 0; run < 10; run++)
        {
            for (var index = 0; index < 1000; index++)
            {
                recorder.Update(new Entity("task", id) { ["new_status"] = index });
                pushes.Record(id, "Template.Test", id, 1, new string('a', 64));
            }
            Assert.Equal(1000, recorder.TotalCount);
            Assert.Equal(1000, pushes.TotalCount);
            Assert.InRange(recorder.Writes.Count, 1, 128);
            Assert.Equal(128, pushes.Records.Count);
            Assert.True(recorder.Overflow);
            Assert.True(pushes.Overflow);
            recorder.Clear(); pushes.Clear();
            Assert.Empty(recorder.Writes); Assert.Empty(pushes.Records);
            Assert.Equal(0, recorder.TotalCount); Assert.Equal(0, pushes.TotalCount);
        }
        pushes.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pushes.Record(id, "Template.Test", id, 1, new string('a', 64)));
    }

    /// <summary>推播範本若包含 LINE ID／換行或雜湊格式錯誤，整筆拒絕且不增加計數。</summary>
    [Fact]
    public void Recording_PushInvalidMetadata_FailsClosed()
    {
        using var pushes = new ShadowPushRecorder();
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => pushes.Record(id, "U" + new string('a', 32), id, 1, new string('a', 64)));
        Assert.Throws<ArgumentException>(() => pushes.Record(id, "Template\r\nprivate", id, 1, new string('a', 64)));
        Assert.Throws<ArgumentException>(() => pushes.Record(id, "Template.Test", id, 1, new string('g', 64)));
        Assert.Equal(0, pushes.TotalCount);
        Assert.Empty(pushes.Records);
    }

    /// <summary>每個 trace 都必須有正確版本前綴及可獨立解析的完整 JSON，禁止字串切片充當行。</summary>
    private static List<JObject> Parse(FakeTracingService tracing)
    {
        const string prefix = "SMV-SHADOW|1|";
        return tracing.Entries.Select(entry =>
        {
            Assert.StartsWith(prefix, entry, StringComparison.Ordinal);
            return JObject.Parse(entry.Substring(prefix.Length));
        }).ToList();
    }

    /// <summary>測試獨立計算 UTF-8 SHA-256，驗證長值及個資投影不是任意遮蔽或原文。</summary>
    private static string Digest(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }
}
