/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-10 真實 Core adapter 的欄位投影、重跑與資料隔離契約。
 * 本檔使用嚴格投影的同步 SDK 替身，避免全欄回應掩蓋產品漏讀既有值的問題；
 * 不是退役 DLL 執行或 goodshepherdback 的影子比對證據。
 *
 * 主要流程：合成正式 contact、account 及 list 主鍵，每次 Retrieve 只回傳要求的
 * ColumnSet；Update 實際套用到案例資料，再次執行服務必須沒有第二次寫入。
 * 故障注入包含錯誤 logical name、原始 Update 例外及 A/B/A 順序交錯；
 * 斷言完整寫入次數、日期不推進、錯誤原實例及個案互不保留。
 *
 * 使用邊界與維護：替身由每個案例 using 擁有，Dispose 排空資料與寫入紀錄，
 * 產品僅借用 SDK，不得提前 Dispose。沒有網路、秘密、背景工作、timer 或共享快取。
 * 所有日期取教會時區，案例執行當日只用作同次服務觀察；跨日純規則另用固定值測試。
 */
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessageProducts.GoodShepherd.Core.Care;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Care;

/// <summary>保護服務真正讀寫資料的契約，替身不代算任何生日、年資或分組規則。</summary>
public sealed class CareServiceTests
{
    /// <summary>真實投影及回寫後再跑，必須零重複更新且下一次年資更新日不再前進。</summary>
    [Fact]
    public void Care_Anniversary_ServiceRerun_NoDuplicateWrite()
    {
        using var sdk = new CareSdk();
        sdk.Contact["birthdate"] = new DateTime(1990, 1, 1);
        sdk.Contact["new_onboard_date"] = new DateTime(2024, 1, 1);
        ContactAnniversaryService.Execute(sdk, sdk.Contact.Id);
        var next = sdk.Contact.GetAttributeValue<DateTime>("new_next_update_onboard_date");
        Assert.Single(sdk.Updates);
        ContactAnniversaryService.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Updates);
        Assert.Equal(next, sdk.Contact.GetAttributeValue<DateTime>("new_next_update_onboard_date"));
    }

    /// <summary>分組服務必須讀回已寫的日期與歷程；第二次執行不得產生另一筆 Update。</summary>
    [Fact]
    public void Care_NewPerson_ServiceRerun_NoDuplicateWrite()
    {
        using var sdk = new CareSdk();
        NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Updates);
        NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id);
        Assert.Single(sdk.Updates);
    }

    /// <summary>相同 ID 的其他表不代表合法 contact，必須在後續查詢與寫入前拒絕。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Care_ServiceWrongLogicalName_RejectsBeforeWrite(bool anniversary)
    {
        using var sdk = new CareSdk();
        sdk.WrongContactType = true;
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (anniversary) ContactAnniversaryService.Execute(sdk, sdk.Contact.Id);
            else NewPersonCareAssignmentService.Execute(sdk, sdk.Contact.Id);
        });
        Assert.Equal(1, sdk.Reads);
        Assert.Empty(sdk.Updates);
    }

    /// <summary>失敗保留原始例外，A/B/A 執行不把上一個來源寫入或例外狀態帶給下一個來源。</summary>
    [Fact]
    public void Care_ServiceFaultThenOtherScope_NoRetainedState()
    {
        using var first = new CareSdk();
        using var second = new CareSdk();
        var failure = new InvalidOperationException("Synthetic update failure.");
        first.UpdateFailure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            ContactAnniversaryService.Execute(first, first.Contact.Id)));
        ContactAnniversaryService.Execute(second, second.Contact.Id);
        Assert.Equal(second.Contact.Id, Assert.Single(second.Updates).Id);
        first.UpdateFailure = null;
        ContactAnniversaryService.Execute(first, first.Contact.Id);
        Assert.Equal(first.Contact.Id, Assert.Single(first.Updates).Id);
    }

    /// <summary>每案唯一資料擁有者；只接受精確主鍵及要求欄位，未知 SDK 操作拒絕。</summary>
    private sealed class CareSdk : IOrganizationService, IDisposable
    {
        private readonly Entity church = new("account", Guid.NewGuid()) { ["new_timezone"] = "Asia/Taipei" };
        private readonly Entity list = new("list", Guid.NewGuid()) { ["listname"] = "合成關懷小組" };
        /// <summary>本案例權威 contact，僅由測試設種子與 Update 改變。</summary>
        internal Entity Contact { get; }
        /// <summary>當案成功寫入的 detached patch，Dispose 清空。</summary>
        internal List<Entity> Updates { get; } = new();
        /// <summary>讀取次數用來證明錯誤主表身分立即拒絕。</summary>
        internal int Reads { get; private set; }
        /// <summary>注入同 ID 不同表回應，不能因為 GUID 相同就信任。</summary>
        internal bool WrongContactType { get; set; }
        /// <summary>只在本案例存活的寫入故障，不影響另一個 SDK。</summary>
        internal Exception? UpdateFailure { get; set; }

        /// <summary>建立有正式教會與名單 lookup 的合成 contact，不以顯示名稱授權。</summary>
        internal CareSdk() => Contact = new Entity("contact", Guid.NewGuid())
        {
            ["parentcustomerid"] = church.ToEntityReference(),
            ["new_cell_list_contact"] = list.ToEntityReference()
        };

        /// <summary>嚴格最小投影；未讀取欄位不會因測試便利而出現在回應。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Reads++;
            Assert.False(columnSet.AllColumns);
            var source = entityName == "contact" && id == Contact.Id ? Contact
                : entityName == "account" && id == church.Id ? church
                : entityName == "list" && id == list.Id ? list
                : throw new InvalidOperationException("Unknown synthetic identity.");
            var result = new Entity(WrongContactType && source == Contact ? "account" : source.LogicalName, id);
            foreach (var name in columnSet.Columns)
                if (source.Contains(name)) result[name] = Copy(source[name]);
            return result;
        }

        /// <summary>先故障再發布；成功後把獨立 patch 套用來源，以驗證第二次真實讀回。</summary>
        public void Update(Entity entity)
        {
            if (UpdateFailure is not null) throw UpdateFailure;
            Assert.Equal("contact", entity.LogicalName);
            Assert.Equal(Contact.Id, entity.Id);
            var patch = new Entity(entity.LogicalName, entity.Id);
            foreach (var pair in entity.Attributes)
            {
                Contact[pair.Key] = Copy(pair.Value);
                patch[pair.Key] = Copy(pair.Value);
            }
            Updates.Add(patch);
        }

        /// <summary>複製案例支援的可變 SDK 值，不讓產品持有來源字典內的 option 或 lookup。</summary>
        private static object Copy(object value) => value switch
        {
            EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
            OptionSetValue option => new OptionSetValue(option.Value),
            _ => value
        };
        /// <summary>案例結束清空所有可變來源、故障與記錄；無借用資源需要關閉。</summary>
        public void Dispose() { Updates.Clear(); Contact.Attributes.Clear(); church.Attributes.Clear(); list.Attributes.Clear(); UpdateFailure = null; }
        /// <summary>關懷欄位服務不建立資料列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不刪除資料列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不執行其他 SDK 訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不需集合查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不新增關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>關懷欄位服務不移除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
