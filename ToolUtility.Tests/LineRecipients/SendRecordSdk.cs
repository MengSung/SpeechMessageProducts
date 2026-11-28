/*
檔案整體說明
目的與定位：F5 原子發送紀錄的離線 SDK 故障替身，僅供各案例獨佔，不連 D365 或 LINE。
主要功能與流程：以有界字典模擬資料庫唯一鍵、正式 GUID、遞增版本及 CAS，保留真 adapter
送出的查詢與請求供斷言；可注入非唯一錯誤、重複列、缺版本及寫入後取消。
使用邊界與維護：此處鎖只模擬單一資料庫原子性，不是產品跨程序唯一性的實機證明。
每案最多 64 列、任務全部由案例 await；沒有連線／計時器／背景 owner。Dispose 清字典並
拒新 SDK 呼叫，adapter 不得 Dispose 借用 SDK。合成資料不含秘密與真實 userId。
*/
using System.Globalization;
using System.ServiceModel;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtility.Tests.LineRecipients;

/// <summary>單案擁有的原子資料庫模型；公開欄位只供案例明確注入故障。</summary>
internal sealed class SendRecordSdk : IOrganizationService, IDisposable
{
    internal const int DuplicateKey = unchecked((int)0x80060892);
    internal const int VersionConflict = unchecked((int)0x80060882);
    private readonly object sync = new();
    private readonly Dictionary<string, Entity> rows = new(StringComparer.Ordinal);
    private bool disposed;
    internal Guid OrganizationId { get; } = Guid.NewGuid();
    internal EntityMetadata Metadata { get; } = MakeMetadata();
    internal QueryExpression? LastQuery { get; private set; }
    internal UpdateRequest? LastUpdate { get; private set; }
    internal int Creates { get; private set; }
    internal int Updates { get; private set; }
    internal int Queries { get; private set; }
    internal int Disposals { get; private set; }
    internal int? CreateFault { get; set; }
    internal int? UpdateFault { get; set; }
    internal bool DuplicateQueryRow { get; set; }
    internal bool MissingVersion { get; set; }
    internal Action? AfterCreate { get; set; }
    internal Action? AfterUpdate { get; set; }
    internal Action<Entity>? MutateRead { get; set; }

    /// <summary>固定、無個資的合成業務事件；鍵必須走與 Gateway 相同的 canonical helper。</summary>
    internal static SendBatchIdentity Identity(string correlation = "event-1", Guid? tenant = null)
    {
        var id = tenant ?? new Guid("11111111-1111-1111-1111-111111111111");
        var key = SendBatchKeys.Create("GoodShepherd2", "2012804957", id, correlation, 0);
        return new SendBatchIdentity("GoodShepherd2", "2012804957", id, correlation, 0, 1, key,
            new string('a', 64), new string('b', 64), new string('c', 64), SendBatchKeys.RetryKey(key),
            MessageCategory.Transactional, 1, 1, null);
    }

    /// <summary>metadata 讀取必須是 published 精確表；其他請求僅接受 WhoAmI 與 CAS Update。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        lock (sync)
        {
            CheckOpen();
            if (request is WhoAmIRequest)
            {
                var response = new WhoAmIResponse();
                response.Results["OrganizationId"] = OrganizationId;
                return response;
            }
            if (request is RetrieveEntityRequest metadata)
            {
                Xunit.Assert.Equal("new_sendrecord", metadata.LogicalName);
                Xunit.Assert.False(metadata.RetrieveAsIfPublished);
                Xunit.Assert.True((metadata.EntityFilters & EntityFilters.Entity) != 0);
                var response = new RetrieveEntityResponse();
                response.Results["EntityMetadata"] = Metadata;
                return response;
            }
            if (request is not UpdateRequest update) throw new NotSupportedException();
            Updates++;
            LastUpdate = update;
            if (UpdateFault is int fault) throw Fault(fault);
            var row = rows.Values.Single(r => r.Id == update.Target.Id);
            if (update.ConcurrencyBehavior != ConcurrencyBehavior.IfRowVersionMatches ||
                row.RowVersion != update.Target.RowVersion) throw Fault(VersionConflict);
            foreach (var pair in update.Target.Attributes) row[pair.Key] = pair.Value;
            row.RowVersion = (long.Parse(row.RowVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            AfterUpdate?.Invoke();
            return new UpdateResponse();
        }
    }

    /// <summary>同鍵最多一筆；即使兩個 store 同時 Create，也只有一個得到正式新 ID。</summary>
    public Guid Create(Entity entity)
    {
        lock (sync)
        {
            CheckOpen();
            Creates++;
            if (CreateFault is int fault) throw Fault(fault);
            var key = entity.GetAttributeValue<string>("new_batchkey");
            if (rows.ContainsKey(key)) throw Fault(DuplicateKey);
            if (rows.Count >= 64) throw new InvalidOperationException("Synthetic row budget exceeded.");
            var row = Copy(entity);
            row.Id = Guid.NewGuid();
            row["new_sendrecordid"] = row.Id;
            row.RowVersion = "1";
            rows.Add(key, row);
            AfterCreate?.Invoke();
            return row.Id;
        }
    }

    /// <summary>返回獨立候選，故障注入不會不小心改動資料庫模型的完整列。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        lock (sync)
        {
            CheckOpen();
            Xunit.Assert.Equal("new_sendrecord", entityName);
            Xunit.Assert.False(columnSet.AllColumns);
            var row = Copy(rows.Values.Single(r => r.Id == id));
            if (MissingVersion) row.RowVersion = null;
            MutateRead?.Invoke(row);
            return row;
        }
    }

    /// <summary>唯一鍵衝突後必須 TopCount2 及精確 key 條件，不可掃全表或吞掉重複正式 ID。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        lock (sync)
        {
            CheckOpen();
            Queries++;
            var expression = Xunit.Assert.IsType<QueryExpression>(query);
            LastQuery = expression;
            Xunit.Assert.Equal("new_sendrecord", expression.EntityName);
            Xunit.Assert.Equal(2, expression.TopCount);
            var filter = Xunit.Assert.Single(expression.Criteria.Conditions);
            Xunit.Assert.Equal("new_batchkey", filter.AttributeName);
            Xunit.Assert.Equal(ConditionOperator.Equal, filter.Operator);
            var key = Xunit.Assert.IsType<string>(Xunit.Assert.Single(filter.Values));
            var result = new EntityCollection();
            if (rows.TryGetValue(key, out var source))
            {
                var row = Copy(source);
                MutateRead?.Invoke(row);
                result.Entities.Add(row);
                if (DuplicateQueryRow) result.Entities.Add(Copy(row));
            }
            return result;
        }
    }

    /// <summary>明確 SDK fault code 不包含任何遠端文字，便於驗原樣保留及精確分類。</summary>
    internal static FaultException<OrganizationServiceFault> Fault(int code) =>
        new(new OrganizationServiceFault { ErrorCode = code, Message = "synthetic-fault" });

    /// <summary>測試可改 SDK 唯讀 metadata 屬性；正式 adapter 不以反射繞過服務端能力。</summary>
    internal static void SetMetadata(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    /// <summary>發布能力是案例的明確前提，停用 key／concurrency 必須使產品拒絕 Create。</summary>
    private static EntityMetadata MakeMetadata()
    {
        var metadata = new EntityMetadata { LogicalName = "new_sendrecord" };
        SetMetadata(metadata, "IsOptimisticConcurrencyEnabled", true);
        var key = new EntityKeyMetadata { LogicalName = "new_ak_sendrecord", KeyAttributes = new[] { "new_batchkey" } };
        SetMetadata(key, "EntityKeyIndexStatus", EntityKeyIndexStatus.Active);
        SetMetadata(metadata, "Keys", new[] { key });
        return metadata;
    }

    /// <summary>模型複製只含本測試安全純值；lookup 與 choice 也複製以防測試偶然共享可變屬性。</summary>
    private static Entity Copy(Entity source)
    {
        var result = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
        foreach (var pair in source.Attributes) result[pair.Key] = pair.Value switch
        {
            EntityReference lookup => new EntityReference(lookup.LogicalName, lookup.Id),
            OptionSetValue option => new OptionSetValue(option.Value),
            _ => pair.Value
        };
        return result;
    }

    /// <summary>借用方關閉後不得再發起操作；不建立外部或背景資源。</summary>
    private void CheckOpen()
    {
        if (disposed) throw new ObjectDisposedException(nameof(SendRecordSdk));
    }
    /// <summary>不接受無版本普通 Update，迫使真 adapter 使用可觀察的 CAS request。</summary>
    public void Update(Entity entity) => throw new NotSupportedException("CAS UpdateRequest required.");
    /// <summary>本契約不刪除紀錄；測試不能藉清理資料庫模型迴避同事件衝突。</summary>
    public void Delete(string entityName, Guid id) => throw new NotSupportedException();
    /// <summary>發送紀錄沒有角色或關聯寫入，本替身拒絕所有額外授權變動。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>不允許拆除資料庫關聯掩蓋錯租戶，沒有借用資源需要此路徑清理。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

    /// <summary>只由案例最外層釋放，確定全部 await 後才清除模型。</summary>
    public void Dispose() { lock (sync) { Disposals++; disposed = true; rows.Clear(); } }
}
