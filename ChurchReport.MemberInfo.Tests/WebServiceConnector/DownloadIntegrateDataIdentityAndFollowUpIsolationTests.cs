// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadIntegrateDataIdentityAndFollowUpIsolationTests.cs
// 檔案責任：以可辨識的 A/B operation-local IOrganizationService fake，驗證 Identity 與
// FollowUp partial 的 CRM 讀寫 helper 僅使用當次呼叫傳入的 service，不會保存、交叉使用或
// Dispose 呼叫端借用的 service。測試不建立 CRM 連線、Session、快取、計時器或背景工作。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，且必須以 final CRLF 結尾。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WebServiceConnector;

/// <summary>
/// 保護 Identity 與新人 FollowUp 的 operation-local CRM service 隔離契約。
///
/// <para>
/// 故障模型是 A/B 兩個 service 交錯讀寫相同類型 CRM entity。決定性斷言為每次 SDK
/// 呼叫只到達自己的 marker、<see cref="DownloadIntegrateData"/> 不持有任一 service，且
/// 內層沒有 Dispose caller-owned service。若 helper 被改回 ToolUtility、Factory 或欄位
/// fallback，本測試必須先於真實 CRM I/O 偵測到隔離違反。
/// </para>
/// </summary>
public sealed class DownloadIntegrateDataIdentityAndFollowUpIsolationTests
{
    /// <summary>
    /// 保護 Identity 查詢與更新，以及 FollowUp contact／出席紀錄查詢與週次更新的完整
    /// A/B 路由。測試使用未初始化受測物，藉此證明 helper 不依賴或讀取共用 ToolUtility；
    /// injected fake 會記錄 SDK 呼叫與 Dispose 次數，讓資源所有權與零 retention 可被直接
    /// 驗證。
    /// </summary>
    [Fact]
    public void OperationLocalIdentityAndFollowUpHelpers_WhenMarkersAreInterleaved_UseOnlyCurrentServiceWithoutRetentionOrDisposal()
    {
        var subject = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(
            typeof(DownloadIntegrateData));
        var firstService = new MarkerOrganizationService("A");
        var secondService = new MarkerOrganizationService("B");
        var firstContactId = Guid.NewGuid();
        var secondContactId = Guid.NewGuid();

        var retrieveIdentityRecords = GetPrivateStaticMethod(
            "RetrieveIdentityPresentRecords",
            typeof(IOrganizationService),
            typeof(Guid),
            typeof(Guid));
        var updateIdentityContact = GetPrivateStaticMethod(
            "UpdateIdentityContact",
            typeof(IOrganizationService),
            typeof(Entity));
        var retrieveFollowUpContact = GetPrivateStaticMethod(
            "RetrieveFollowUpContact",
            typeof(IOrganizationService),
            typeof(Guid));
        var retrieveFollowUpRecords = GetPrivateStaticMethod(
            "RetrieveFollowUpPresentRecords",
            typeof(IOrganizationService),
            typeof(Guid));
        var updateFollowUpRecord = GetPrivateStaticMethod(
            "UpdateFollowUpPresentRecord",
            typeof(IOrganizationService),
            typeof(Entity));

        var firstIdentityRecords = (EntityCollection)retrieveIdentityRecords.Invoke(
            null,
            new object[] { firstService, Guid.NewGuid(), firstContactId })!;
        var secondIdentityRecords = (EntityCollection)retrieveIdentityRecords.Invoke(
            null,
            new object[] { secondService, Guid.NewGuid(), secondContactId })!;
        updateIdentityContact.Invoke(null, new object[] { firstService, new Entity("contact", firstContactId) });
        updateIdentityContact.Invoke(null, new object[] { secondService, new Entity("contact", secondContactId) });

        var firstContact = (Entity)retrieveFollowUpContact.Invoke(
            null,
            new object[] { firstService, firstContactId })!;
        var secondContact = (Entity)retrieveFollowUpContact.Invoke(
            null,
            new object[] { secondService, secondContactId })!;
        var firstFollowUpRecords = (EntityCollection)retrieveFollowUpRecords.Invoke(
            null,
            new object[] { firstService, firstContactId })!;
        var secondFollowUpRecords = (EntityCollection)retrieveFollowUpRecords.Invoke(
            null,
            new object[] { secondService, secondContactId })!;
        updateFollowUpRecord.Invoke(null, new object[] { firstService, new Entity("new_present_record", Guid.NewGuid()) });
        updateFollowUpRecord.Invoke(null, new object[] { secondService, new Entity("new_present_record", Guid.NewGuid()) });

        firstIdentityRecords.Entities.Should().ContainSingle().Which.GetAttributeValue<string>("marker")
            .Should().Be("A");
        secondIdentityRecords.Entities.Should().ContainSingle().Which.GetAttributeValue<string>("marker")
            .Should().Be("B");
        firstContact.GetAttributeValue<string>("marker").Should().Be("A");
        secondContact.GetAttributeValue<string>("marker").Should().Be("B");
        firstFollowUpRecords.Entities.Should().ContainSingle().Which.GetAttributeValue<string>("marker")
            .Should().Be("A");
        secondFollowUpRecords.Entities.Should().ContainSingle().Which.GetAttributeValue<string>("marker")
            .Should().Be("B");

        firstService.RetrieveMultipleCallCount.Should().Be(2);
        secondService.RetrieveMultipleCallCount.Should().Be(2);
        firstService.RetrieveCallCount.Should().Be(1);
        secondService.RetrieveCallCount.Should().Be(1);
        firstService.UpdatedEntityLogicalNames.Should().Equal("contact", "new_present_record");
        secondService.UpdatedEntityLogicalNames.Should().Equal("contact", "new_present_record");
        firstService.DisposeCount.Should().Be(0,
            "這些 helper 只借用 A 的 service，釋放責任仍屬於呼叫端 lease owner");
        secondService.DisposeCount.Should().Be(0,
            "這些 helper 只借用 B 的 service，釋放責任仍屬於呼叫端 lease owner");
        AssertDoesNotRetainBorrowedService(subject, firstService);
        AssertDoesNotRetainBorrowedService(subject, secondService);
    }

    /// <summary>
    /// 以受保護的名稱與精確參數型別取得 service-aware private helper。helper 消失、改為
    /// instance service 欄位或改成 ToolUtility façade 時，這個查找會讓測試失敗，而不是
    /// 靜默測試另一條 legacy 路徑。
    /// </summary>
    /// <param name="methodName">受保護的 private helper 名稱。</param>
    /// <param name="parameterTypes">operation-local 參數鏈的精確型別。</param>
    /// <returns>只允許 static 且 non-public 的受測 helper。</returns>
    private static MethodInfo GetPrivateStaticMethod(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(DownloadIntegrateData).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        method.Should().NotBeNull(
            $"{methodName} 必須以 operation-local IOrganizationService 參數執行，不能使用 instance、Factory 或 ToolUtility 保存的 service");
        return method!;
    }

    /// <summary>
    /// 巡檢 session 可能保留的 DownloadIntegrateData instance field，確認 A/B 借用 service
    /// 在同步 helper 返回後均已沒有參考。掃描不接觸 static 或 Factory，避免測試本身製造
    /// process-wide 可變狀態。
    /// </summary>
    /// <param name="instance">可能被長時間 Session 快取的整合下載物件。</param>
    /// <param name="borrowedService">本操作借用、且不可被 inner helper 保存的 service。</param>
    private static void AssertDoesNotRetainBorrowedService(
        DownloadIntegrateData instance,
        IOrganizationService borrowedService)
    {
        var retainedField = instance.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(field => ReferenceEquals(field.GetValue(instance), borrowedService));

        retainedField.Should().BeNull(
            "operation-local service 不得寫入 DownloadIntegrateData 的 instance 或 static field，否則同一 Session 的後續使用者可能重用前次連線狀態");
    }

    /// <summary>
    /// A/B marker CRM service fake。每個 instance 只保存自身測試方法的短生命期呼叫紀錄；
    /// 不建立外部資源。<see cref="Dispose"/> 只增加 sentinel，讓測試能明確拒絕被借用者
    /// 提前釋放的行為。
    /// </summary>
    private sealed class MarkerOrganizationService : IOrganizationService, IDisposable
    {
        /// <summary>
        /// 建立具有隔離 marker 的本機 CRM fake。
        /// </summary>
        /// <param name="marker">僅供本測試辨識 A/B 操作的短字串。</param>
        public MarkerOrganizationService(string marker)
        {
            Marker = marker;
        }

        /// <summary>
        /// 取得目前 fake 所屬的操作 marker；它不會被寫入正式程式、快取或診斷管道。
        /// </summary>
        public string Marker { get; }

        /// <summary>
        /// 取得 direct <see cref="IOrganizationService.Retrieve"/> 呼叫次數。
        /// </summary>
        public int RetrieveCallCount { get; private set; }

        /// <summary>
        /// 取得 direct <see cref="IOrganizationService.RetrieveMultiple"/> 呼叫次數。
        /// </summary>
        public int RetrieveMultipleCallCount { get; private set; }

        /// <summary>
        /// 取得 direct <see cref="IOrganizationService.Update"/> 接收的 entity logical name，
        /// 用以證明 Identity 與 FollowUp 寫入沒有交叉到另一個 service。
        /// </summary>
        public IList<string> UpdatedEntityLogicalNames { get; } = new List<string>();

        /// <summary>
        /// 取得 Dispose sentinel 計數；正確的借用 helper 必須始終維持零。
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// 此範圍不允許建立 CRM entity，故任何 Create 都代表 helper 擴大了既定權限。
        /// </summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();

        /// <summary>
        /// 記錄 direct Update 的 entity 類型；不保存 entity 參考，以免 fake 自己隱藏 retention。
        /// </summary>
        public void Update(Entity entity)
        {
            UpdatedEntityLogicalNames.Add(entity.LogicalName);
        }

        /// <summary>
        /// 此範圍不允許刪除 CRM entity，故任何 Delete 都代表 helper 擴大了既定權限。
        /// </summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        /// <summary>
        /// 此範圍不允許執行任意 CRM request，直接 SDK 讀寫必須使用 Retrieve、RetrieveMultiple
        /// 或 Update。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();

        /// <summary>
        /// 回傳只含本 fake marker 的 Contact，驗證呼叫端沒有回落到共用 ToolUtility service。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            RetrieveCallCount++;
            var entity = new Entity(entityName, id);
            entity["marker"] = Marker;
            return entity;
        }

        /// <summary>
        /// 回傳只含本 fake marker 的集合。query 不保存於 fake，避免測試自己製造跨操作參考；
        /// logical name 驗證由正式 helper 的 direct SDK 呼叫與回傳 marker 共同保護。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            RetrieveMultipleCallCount++;
            var entity = new Entity("new_present_record", Guid.NewGuid());
            entity["marker"] = Marker;
            return new EntityCollection(new[] { entity });
        }

        /// <summary>
        /// 此範圍不允許建立關聯，故任何 Associate 都代表 helper 超出 identity/follow-up 權限。
        /// </summary>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        /// <summary>
        /// 此範圍不允許移除關聯，故任何 Disassociate 都代表 helper 超出 identity/follow-up 權限。
        /// </summary>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        /// <summary>
        /// 僅遞增 sentinel，模擬 caller owner 可觀察的釋放行為；production helper 不得呼叫它。
        /// </summary>
        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
