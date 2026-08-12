// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadIntegrateDataSetupAndMembersIsolationTests.cs
// 檔案責任：以可區分的 A/B operation-local CRM service fake 驗證 DownloadIntegrateData
// 的設定、名單、週報與批次聯絡人查詢 helper 必須只使用目前呼叫提供的 service。測試不建立
// 外部連線、Session、快取或背景工作；所有 fake 只存在於測試方法，並在方法結束後失去參考。
// 受保護契約：借用 service 不可寫入 instance、static、Factory、ToolUtility 或 cache，亦不可由
// 下載流程 Dispose。共享 ToolUtility 的 service 計數必須保持零，才能避免 A/B 使用者、profile
// 或 connector generation 透過遺留的可變連線狀態交叉讀取資料。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，且必須以 final CRLF 結尾。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WebServiceConnector;

/// <summary>
/// 驗證尚未接入 Core 入口的 service-aware 讀取 helper 維持嚴格的 operation-local 邊界。
///
/// <para>
/// 本測試的 fault/marker 注入把 A 與 B 的所有回傳實體設為不同名稱；若任一 helper 回落至
/// Factory 共用 ToolUtility 或保存先前 service，回傳 marker 或共享 service 計數會立即失敗。
/// 這同時保護借用資源所有權：假 service 實作 <see cref="IDisposable"/>，但受測流程不可呼叫
/// <see cref="IDisposable.Dispose"/>，因為 lease owner 才是唯一可釋放者。
/// </para>
/// </summary>
public sealed class DownloadIntegrateDataSetupAndMembersIsolationTests
{
    /// <summary>
    /// 保護 setup/header、list/weekly 與 member/batch-contact service-aware helper 的 A/B 隔離。
    /// 故障注入以具有不同 marker 的本機 service 模擬兩個交錯 profile；決定性斷言是每個
    /// 回傳值只含目前 marker、共享 ToolUtility 沒有 CRM 呼叫，且借用 service 沒有被保存或
    /// Dispose。測試經由 private reflection 呼叫尚未由 Core 開啟的 helper，因此不改變既有
    /// 一參數 legacy UI 流程。
    /// </summary>
    [Fact]
    public void ServiceAwareReadHelpers_WhenOperationsAreInterleaved_UseOnlyTheCurrentBorrowedService()
    {
        var sharedToolService = new MarkerOrganizationService("shared");
        var sharedToolUtility = (ToolUtilityClass)RuntimeHelpers.GetUninitializedObject(typeof(ToolUtilityClass));
        sharedToolUtility.m_Crm2011OrganizationService = sharedToolService;

        var subject = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(typeof(DownloadIntegrateData));
        SetPrivateField(subject, "m_ToolUtilityClass", sharedToolUtility);
        SetPrivateField(subject, "m_Sunday", new DateTime(2026, 8, 9));
        SetPrivateField(subject, "m_ListEntity", new Entity("list", Guid.NewGuid()));

        var firstService = new MarkerOrganizationService("A");
        var secondService = new MarkerOrganizationService("B");
        var listId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var setupHeader = RequirePrivateMethod(
            "SetupHeaderData",
            typeof(string),
            typeof(string),
            typeof(DateTime),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(ListSmallGroupWeeklyReport).MakeByRefType(),
            typeof(IOrganizationService));
        var getMemberCollection = RequirePrivateMethod(
            "GetMemberCollection",
            typeof(Guid),
            typeof(bool),
            typeof(IOrganizationService));
        var batchRetrieveContacts = RequirePrivateMethod(
            "BatchRetrieveContacts",
            typeof(List<Guid>),
            typeof(IOrganizationService));
        var setupWeeklyChart = RequirePrivateMethod(
            "SetupWeeklyReportChartData",
            typeof(ListSmallGroupWeeklyReport).MakeByRefType(),
            typeof(IOrganizationService));

        var firstHeader = InvokeHeader(subject, setupHeader, listId, firstService);
        var secondHeader = InvokeHeader(subject, setupHeader, listId, secondService);
        firstHeader.ListEntityName.Should().Be("A-小組");
        secondHeader.ListEntityName.Should().Be("B-小組");
        firstHeader.SmallGroupLeaderFullName.Should().Be("A-使用者");
        secondHeader.SmallGroupLeaderFullName.Should().Be("B-使用者");

        var firstMembers = (EntityCollection)getMemberCollection.Invoke(subject, new object[] { listId, false, firstService })!;
        var secondMembers = (EntityCollection)getMemberCollection.Invoke(subject, new object[] { listId, false, secondService })!;
        firstMembers.Entities.Single().GetAttributeValue<string>("marker").Should().Be("A");
        secondMembers.Entities.Single().GetAttributeValue<string>("marker").Should().Be("B");

        var firstContacts = (Dictionary<Guid, Entity>)batchRetrieveContacts.Invoke(
            subject,
            new object[] { new List<Guid> { contactId }, firstService })!;
        var secondContacts = (Dictionary<Guid, Entity>)batchRetrieveContacts.Invoke(
            subject,
            new object[] { new List<Guid> { contactId }, secondService })!;
        firstContacts[contactId].GetAttributeValue<string>("marker").Should().Be("A");
        secondContacts[contactId].GetAttributeValue<string>("marker").Should().Be("B");

        var firstChart = InvokeWeeklyChart(subject, setupWeeklyChart, listId, firstService);
        var secondChart = InvokeWeeklyChart(subject, setupWeeklyChart, listId, secondService);
        firstChart.m_WeeklyReportChart.m_ChartDataList.Single().WeeklyReportEntityId.Should().Be(firstService.WeeklyReportId.ToString());
        secondChart.m_WeeklyReportChart.m_ChartDataList.Single().WeeklyReportEntityId.Should().Be(secondService.WeeklyReportId.ToString());

        sharedToolService.CallCount.Should().Be(0,
            "service-aware helper 不得回落至 Factory 共用 ToolUtility 的 CRM service");
        firstService.DisposeCount.Should().Be(0,
            "A 的 service 由外層 lease owner 借出，DownloadIntegrateData 不能釋放它");
        secondService.DisposeCount.Should().Be(0,
            "B 的 service 由外層 lease owner 借出，DownloadIntegrateData 不能釋放它");
        AssertDoesNotRetainBorrowedService(subject, firstService);
        AssertDoesNotRetainBorrowedService(subject, secondService);
    }

    /// <summary>
    /// 取得受保護的 private overload。若 helper 不存在，測試會在任何 CRM 呼叫前紅燈，表示
    /// 尚未建立可安全接入上層的 operation-local 邊界，而不是把借用 service 偷渡到 legacy
    /// ToolUtility 路徑。
    /// </summary>
    /// <param name="methodName">預期的 private helper 名稱。</param>
    /// <param name="parameterTypes">用來區分 legacy overload 的完整參數型別。</param>
    /// <returns>已確認存在的 private helper。</returns>
    private static MethodInfo RequirePrivateMethod(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(DownloadIntegrateData).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        method.Should().NotBeNull(
            $"{methodName} 必須提供 operation-local IOrganizationService overload，並且不能經由共用 ToolUtility 轉送");
        return method!;
    }

    /// <summary>
    /// 呼叫 header helper 並取回 ref 輸出模型。輸入帳密只屬於測試方法的短生命期字串，不會
    /// 寫入 service、cache 或診斷輸出；fake 只以固定值模擬成功登入。
    /// </summary>
    /// <param name="subject">未快取的受測下載器。</param>
    /// <param name="setupHeader">service-aware header helper。</param>
    /// <param name="listId">目前操作已授權的名單 ID。</param>
    /// <param name="service">目前操作唯一可使用的借用 service。</param>
    /// <returns>只含目前 service marker 的 header 模型。</returns>
    private static ListSmallGroupWeeklyReport InvokeHeader(
        DownloadIntegrateData subject,
        MethodInfo setupHeader,
        Guid listId,
        IOrganizationService service)
    {
        var report = CreateOperationReport();
        var arguments = new object[]
        {
            "test-account",
            "test-password",
            new DateTime(2026, 8, 10),
            listId.ToString(),
            string.Empty,
            "小組長",
            report,
            service
        };

        setupHeader.Invoke(subject, arguments);
        return (ListSmallGroupWeeklyReport)arguments[6];
    }

    /// <summary>
    /// 呼叫週報圖表 helper 並取回 ref 輸出模型。此 helper 只允許當前 service 的 read-only
    /// RetrieveMultiple；測試沒有建立快取，因此每個結果都可直接驗證 A/B marker。
    /// </summary>
    /// <param name="subject">未快取的受測下載器。</param>
    /// <param name="setupWeeklyChart">service-aware 週報圖表 helper。</param>
    /// <param name="service">目前操作唯一可使用的借用 service。</param>
    /// <returns>只含目前 service 週報 ID 的圖表模型。</returns>
    private static ListSmallGroupWeeklyReport InvokeWeeklyChart(
        DownloadIntegrateData subject,
        MethodInfo setupWeeklyChart,
        Guid listId,
        IOrganizationService service)
    {
        var report = CreateOperationReport();
        report.ListEntityId = listId.ToString();
        report.SundayPrayers = new DateTime(2026, 8, 10);
        var arguments = new object[] { report, service };

        setupWeeklyChart.Invoke(subject, arguments);
        return (ListSmallGroupWeeklyReport)arguments[0];
    }

    /// <summary>
    /// 以反射設定既有私有欄位，讓測試可隔離 Factory 初始化與外部連線。欄位名稱若改變，測試
    /// 立即失敗，迫使維護者重新審查此 service ownership 邊界。
    /// </summary>
    /// <typeparam name="TInstance">持有私有欄位的既有型別。</typeparam>
    /// <typeparam name="TValue">要指派的測試值型別。</typeparam>
    /// <param name="instance">受測物件。</param>
    /// <param name="fieldName">受保護的私有欄位名稱。</param>
    /// <param name="value">只屬於本測試的短生命期值。</param>
    private static void SetPrivateField<TInstance, TValue>(TInstance instance, string fieldName, TValue value)
    {
        var field = typeof(TInstance).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{fieldName} 是此隔離測試需要控制的既有欄位");
        field!.SetValue(instance, value);
    }

    /// <summary>
    /// 建立不觸發 legacy Factory 初始化的短生命期輸出模型。正式建構式目前會建立舊版上傳器，
    /// 而本隔離測試只覆蓋 read-only helper，若允許它初始化共享 ToolUtility 會破壞「零 fallback
    /// CRM call」的測試前提。必要集合由本測試明確建立，且不會跨方法保存。
    /// </summary>
    /// <returns>只供目前測試 operation 使用的未快取報表模型。</returns>
    private static ListSmallGroupWeeklyReport CreateOperationReport()
    {
        var report = (ListSmallGroupWeeklyReport)RuntimeHelpers.GetUninitializedObject(
            typeof(ListSmallGroupWeeklyReport));
        report.GroupArray = new List<string>();
        return report;
    }

    /// <summary>
    /// 掃描 instance fields，確認借用 service 沒有被下載器保存。此檢查不讀取 static、Factory
    /// 或 cache，以免測試自己建立跨測試可變狀態；共享 ToolUtility 的零呼叫斷言另行保護回落。
    /// </summary>
    /// <param name="instance">可能被 Session 或上層保留的下載器。</param>
    /// <param name="borrowedService">本次操作傳入、必須在返回後失去參考的 service。</param>
    private static void AssertDoesNotRetainBorrowedService(object instance, IOrganizationService borrowedService)
    {
        var retainedField = instance.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(field => ReferenceEquals(field.GetValue(instance), borrowedService));

        retainedField.Should().BeNull(
            "DownloadIntegrateData 不得保存 caller-owned CRM service，否則後續使用者可能重用前次 operation state");
    }

    /// <summary>
    /// 可追蹤的 operation-local CRM service fake。它以 marker 決定每個 CRM 回傳值，並把所有
    /// 計數保存於自身，不使用 static、Session 或 cache。任何非預期的寫入或關聯 API 都會立刻
    /// 失敗，確保此測試只證明安全的 read-only routing。
    /// </summary>
    private sealed class MarkerOrganizationService : IOrganizationService, IDisposable
    {
        /// <summary>
        /// 初始化本 fake 的 A/B marker 與固定週報識別。這些 ID 只用於本機斷言，絕不代表或
        /// 記錄真實 CRM 資料、使用者資料或 connector 設定。
        /// </summary>
        /// <param name="marker">用來識別當前 operation 的短生命期測試標記。</param>
        public MarkerOrganizationService(string marker)
        {
            Marker = marker;
            WeeklyReportId = Guid.NewGuid();
        }

        /// <summary>
        /// 取得此 fake 專屬的 A/B marker。
        /// </summary>
        public string Marker { get; }

        /// <summary>
        /// 取得此 fake 回傳的週報 ID，用於驗證週報查詢沒有跨 operation。
        /// </summary>
        public Guid WeeklyReportId { get; }

        /// <summary>
        /// 取得所有 CRM API 呼叫總數；共享 ToolUtility 的此值必須永遠為零。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得借用者呼叫 Dispose 的次數；下載器不是 owner，因此必須保持零。
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// 本 read-only 測試不允許建立 CRM 實體。
        /// </summary>
        /// <param name="entity">不應寫入的實體。</param>
        /// <returns>此方法一律擲回。</returns>
        public Guid Create(Entity entity) => ThrowUnexpectedCall<Guid>();

        /// <summary>
        /// 本 read-only 測試不允許更新 CRM 實體。
        /// </summary>
        /// <param name="entity">不應更新的實體。</param>
        public void Update(Entity entity) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 本 read-only 測試不允許刪除 CRM 實體。
        /// </summary>
        /// <param name="entityName">不應刪除的實體邏輯名稱。</param>
        /// <param name="id">不應刪除的實體識別。</param>
        public void Delete(string entityName, Guid id) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 本測試不允許使用 Execute 包裝查詢，以便直接證明 helper 的 read-only CRM API 邊界。
        /// </summary>
        /// <param name="request">不應執行的 CRM request。</param>
        /// <returns>此方法一律擲回。</returns>
        public OrganizationResponse Execute(OrganizationRequest request) => ThrowUnexpectedCall<OrganizationResponse>();

        /// <summary>
        /// 模擬 header 所需的 list 讀取。非 list 實體表示 helper 超出其宣告的 read-only 範圍，
        /// 因而以受控例外停止；回傳值僅含本 fake marker，不含外部資料。
        /// </summary>
        /// <param name="entityName">受測 helper 要讀取的實體邏輯名稱。</param>
        /// <param name="id">本測試提供的本機實體識別。</param>
        /// <param name="columnSet">受測 helper 選取的欄位集合。</param>
        /// <returns>帶有目前 marker 的 list 實體。</returns>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            CallCount++;
            if (!string.Equals(entityName, "list", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{Marker} 不預期讀取 {entityName}。");
            }

            return new Entity("list", id)
            {
                ["listname"] = $"{Marker}-小組",
                ["new_contact_family_leader_list"] = new EntityReference("contact", Guid.NewGuid())
                {
                    Name = $"{Marker}-族長"
                }
            };
        }

        /// <summary>
        /// 模擬 header 登入、靜態名單、批次 contact 與週報圖表的查詢。所有回傳資料都帶有當前
        /// marker，使交錯 A/B 操作可證明沒有讀到共享 ToolUtility 或另一個借用 service 的資料。
        /// </summary>
        /// <param name="query">helper 建立的受控 read-only CRM query。</param>
        /// <returns>只由此 fake 產生的 marker collection。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            CallCount++;

            return query switch
            {
                QueryByAttribute queryByAttribute when string.Equals(queryByAttribute.EntityName, "contact", StringComparison.Ordinal) =>
                    new EntityCollection(new[]
                    {
                        new Entity("contact", Guid.NewGuid())
                        {
                            ["fullname"] = $"{Marker}-使用者",
                            ["new_app_pass"] = "test-password"
                        }
                    }),
                QueryByAttribute queryByAttribute when string.Equals(queryByAttribute.EntityName, "listmember", StringComparison.Ordinal) =>
                    new EntityCollection(new[]
                    {
                        new Entity("listmember", Guid.NewGuid()) { ["marker"] = Marker }
                    }),
                QueryExpression queryExpression when string.Equals(queryExpression.EntityName, "contact", StringComparison.Ordinal) =>
                    new EntityCollection(new[]
                    {
                        new Entity("contact", ExtractRequestedContactId(queryExpression)) { ["marker"] = Marker }
                    }),
                QueryExpression queryExpression when string.Equals(queryExpression.EntityName, "list", StringComparison.Ordinal) =>
                    new EntityCollection(new[]
                    {
                        new Entity("list", ExtractRequestedListId(queryExpression))
                        {
                            ["listname"] = $"{Marker}-小組",
                            ["new_contact_family_leader_list"] = new EntityReference("contact", ExtractRequestedLeaderId(queryExpression))
                        }
                    }),
                QueryExpression queryExpression when string.Equals(queryExpression.EntityName, "new_group_present_weekly_report", StringComparison.Ordinal) =>
                    new EntityCollection(new[]
                    {
                        new Entity("new_group_present_weekly_report", WeeklyReportId)
                        {
                            ["new_sunday_date"] = new DateTime(2026, 8, 9),
                            ["new_sunday_present_number"] = 1,
                            ["new_small_group_number"] = 2
                        }
                    }),
                _ => throw new InvalidOperationException($"{Marker} 收到不在測試 allowlist 的查詢型別。")
            };
        }

        /// <summary>
        /// 本 read-only 測試不允許建立 CRM 關聯。
        /// </summary>
        /// <param name="entityName">不應關聯的實體名稱。</param>
        /// <param name="entityId">不應關聯的實體識別。</param>
        /// <param name="relationship">不應建立的關聯。</param>
        /// <param name="relatedEntities">不應寫入的關聯集合。</param>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 本 read-only 測試不允許移除 CRM 關聯。
        /// </summary>
        /// <param name="entityName">不應解除關聯的實體名稱。</param>
        /// <param name="entityId">不應解除關聯的實體識別。</param>
        /// <param name="relationship">不應移除的關聯。</param>
        /// <param name="relatedEntities">不應變更的關聯集合。</param>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 記錄錯誤的借用者釋放行為。fake 不持有外部資源，因此不實際釋放任何物件；計數只用來
        /// 證明 DownloadIntegrateData 沒有越過 lease owner 的唯一資源所有權。
        /// </summary>
        public void Dispose()
        {
            DisposeCount++;
        }

        /// <summary>
        /// 從 batch contact QueryExpression 擷取唯一請求的 contact ID。測試僅傳一個 ID；若
        /// helper 改成不受控的掃描或遺失條件，立即失敗以防止測試誤把效能/授權回歸當成成功。
        /// </summary>
        /// <param name="query">受測 helper 所建立的 contact 查詢。</param>
        /// <returns>測試要求的唯一 contact ID。</returns>
        private static Guid ExtractRequestedContactId(QueryExpression query)
        {
            var condition = query.Criteria.Conditions.SingleOrDefault(candidate =>
                string.Equals(candidate.AttributeName, "contactid", StringComparison.Ordinal));
            condition.Should().NotBeNull("批次 contact 查詢必須以明確 ID allowlist 限縮範圍");
            condition!.Values.Should().ContainSingle("本測試只允許一次 operation-local contact 查詢");
            return (Guid)condition.Values.Single();
        }

        private static Guid ExtractRequestedListId(QueryExpression query)
        {
            var condition = query.Criteria.Conditions.Single(candidate =>
                string.Equals(candidate.AttributeName, "listid", StringComparison.Ordinal));
            return (Guid)condition.Values.Single();
        }

        private static Guid ExtractRequestedLeaderId(QueryExpression query)
        {
            var condition = query.Criteria.Conditions.Single(candidate =>
                string.Equals(candidate.AttributeName, "new_contact_family_leader_list", StringComparison.Ordinal));
            return (Guid)condition.Values.Single();
        }

        /// <summary>
        /// 集中處理所有不允許的 CRM API，避免 fake 意外保留 request 或 response。例外只含本機
        /// marker，沒有 endpoint、credential、token、CRM 真實 ID 或其他可跨使用者資料。
        /// </summary>
        /// <typeparam name="T">遭錯誤呼叫之 CRM API 的回傳型別。</typeparam>
        /// <returns>此方法一律擲回，不會回傳。</returns>
        private T ThrowUnexpectedCall<T>()
        {
            CallCount++;
            throw new InvalidOperationException($"{Marker} 不預期執行寫入或非 allowlist CRM API。");
        }
    }
}
