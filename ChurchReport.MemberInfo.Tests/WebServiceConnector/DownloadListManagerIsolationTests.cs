// ============================================================================
// DownloadListManager 操作範圍 CRM service 隔離回歸測試
//
// 本檔案以未初始化的受測物件在第一個 CRM 呼叫前注入受控失敗，驗證呼叫端借用的
// IOrganizationService 不會被寫入 Factory 擁有的共用 ToolUtilityClass。測試刻意
// 不建立 CRM 連線、Session、快取、計時器、背景工作或檔案，因此每個測試的 service
// 與例外都只存在於該測試方法，結束後沒有可被其他測試或使用者重用的可變狀態。
// ============================================================================

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Core;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WebServiceConnector;

/// <summary>
/// 驗證名單下載流程的 CRM service 所有權與例外傳遞邊界。
///
/// <para>
/// 受保護的安全契約是：呼叫端借用的 service 僅可供當次操作使用，絕不能被寫入
/// Factory 共用的 ToolUtility，否則下一個使用者或不同 profile 的請求可能取得前一個
/// 操作的連線狀態。故障注入發生在任何 CRM I/O 之前，決定性斷言是兩個 service
/// 欄位保持空值，且上層重新擲回後仍保留下層呼叫框架。
/// </para>
/// </summary>
public sealed class DownloadListManagerIsolationTests
{
    /// <summary>
    /// 保護兩個交錯操作的 service 隔離：即使共用 ToolUtility 被兩個不同呼叫重複
    /// 使用，任何借用 service 都不得殘留在其中。受測流程在第一個 CRM I/O 前故意
    /// 失敗，以證明測試不會連到外部環境，且失敗路徑同樣不能造成跨操作保留。
    /// </summary>
    [Fact]
    public void GetListManager_WhenBorrowedServicesAreProvided_DoesNotRetainEitherServiceInSharedToolUtility()
    {
        var sharedToolUtility = (ToolUtilityClass)RuntimeHelpers.GetUninitializedObject(
            typeof(ToolUtilityClass));
        var firstOperationService = new ThrowingOrganizationService();
        var secondOperationService = new ThrowingOrganizationService();

        InvokeUntilFirstControlledFailure(sharedToolUtility, firstOperationService);

        sharedToolUtility.m_Crm2011OrganizationService.Should().BeNull(
            "Factory 擁有的共用工具不得保存第一個操作借用的 CRM service");
        sharedToolUtility.m_OrganizationService.Should().BeNull(
            "Factory 擁有的共用工具不得保存第一個操作的 proxy");

        InvokeUntilFirstControlledFailure(sharedToolUtility, secondOperationService);

        sharedToolUtility.m_Crm2011OrganizationService.Should().BeNull(
            "第二個操作也不得取得或覆寫第一個操作的 service 狀態");
        sharedToolUtility.m_OrganizationService.Should().BeNull(
            "不同操作不可透過共享 ToolUtility 保留 proxy 狀態");
    }

    /// <summary>
    /// 保護例外診斷能力：上層 ListManager 只能以 <c>throw;</c> 保留原始堆疊，
    /// 因此受控的下層 fault 必須仍顯示 DownloadListManager 呼叫框架。若改成
    /// <c>throw e;</c>，原始來源會被遮蔽，child-process 診斷也無法安全分類。
    /// </summary>
    [Fact]
    public void SetupListManager_WhenDownloadFails_PreservesDownloadListManagerStackFrame()
    {
        var sharedToolUtility = (ToolUtilityClass)RuntimeHelpers.GetUninitializedObject(
            typeof(ToolUtilityClass));
        var downloadListManager = CreateManagerWithSharedToolUtility(sharedToolUtility);
        var listManager = (ListManager)RuntimeHelpers.GetUninitializedObject(typeof(ListManager));
        SetPrivateField(listManager, "m_DownloadListManager", downloadListManager);

        var exception = Xunit.Record.Exception(() => listManager.SetupListManager(
            "account",
            "password",
            DateTime.UnixEpoch,
            new ThrowingOrganizationService()));

        exception.Should().NotBeNull("測試在任何 CRM I/O 前注入確定性失敗");
        exception!.StackTrace.Should().Contain(
            "DownloadListManager.GetListManager",
            "上層不得以 throw e; 重設原始 CRM 操作的堆疊");
    }

    /// <summary>
    /// 保護巢狀名單查詢的例外來源：FindListCollection 若取得 CRM 資料失敗，重新擲回
    /// 必須保留 ToolUtility 的原始框架。這個受控 fault 使用未初始化工具，因而在任何
    /// CRM I/O 前失敗；它驗證的是診斷與隔離邊界，而不是遠端服務行為。
    /// </summary>
    [Fact]
    public void FindListCollection_WhenToolUtilityFails_PreservesToolUtilityStackFrame()
    {
        var sharedToolUtility = (ToolUtilityClass)RuntimeHelpers.GetUninitializedObject(
            typeof(ToolUtilityClass));
        var manager = CreateManagerWithSharedToolUtility(sharedToolUtility);
        var method = typeof(DownloadListManager).GetMethod(
            "FindListCollection",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull("名單搜尋是 GetListManager 的既有例外傳遞路徑");
        var outer = Xunit.Record.Exception(() => method!.Invoke(manager, null));
        var invocation = outer.Should().BeOfType<TargetInvocationException>().Subject;

        invocation.InnerException.Should().NotBeNull("受控 ToolUtility fault 必須傳遞到上層");
        invocation.InnerException!.StackTrace.Should().Contain(
            "ToolUtilityClass.QueryListByContactId",
            "巢狀 catch 不得以 throw Exception; 遮蔽 CRM 查詢的真實來源");
    }

    /// <summary>
    /// 保護 façade 的 operation-scoped service 路由：兩個相鄰操作各自提供 CRM
    /// service 時，靜態名單查詢只能呼叫當次借用 service，不可退回建構時的共用 service。
    ///
    /// <para>
    /// 測試以不同 collection marker 模擬兩個 profile；決定性斷言是每次回傳值都屬於
    /// 當前 service，而共用 service 的查詢計數維持零。所有 fake 都由測試方法建立，
    /// 不建立外部連線、Session、cache 或非受控資源，並在方法結束後失去參考。
    /// </para>
    /// </summary>
    [Fact]
    public void RetrieveMemberListCollection_WhenOperationScopedServicesAreProvided_UsesOnlyCurrentService()
    {
        var sharedService = new RecordingOrganizationService();
        var firstResult = new EntityCollection(new[] { new Entity("listmember") { Id = Guid.NewGuid() } });
        var secondResult = new EntityCollection(new[] { new Entity("listmember") { Id = Guid.NewGuid() } });
        var firstOperationService = new RecordingOrganizationService(firstResult);
        var secondOperationService = new RecordingOrganizationService(secondResult);
        var facade = new ToolUtilityFacade(sharedService);
        IOrganizationService firstBorrowedService = firstOperationService;
        IOrganizationService secondBorrowedService = secondOperationService;

        var firstActual = facade.RetrieveMemberListCollectionByListIdCrm2011(
            ref firstBorrowedService,
            Guid.NewGuid());
        var secondActual = facade.RetrieveMemberListCollectionByListIdCrm2011(
            ref secondBorrowedService,
            Guid.NewGuid());

        firstActual.Should().BeSameAs(firstResult);
        secondActual.Should().BeSameAs(secondResult);
        firstOperationService.RetrieveMultipleCallCount.Should().Be(1);
        secondOperationService.RetrieveMultipleCallCount.Should().Be(1);
        sharedService.RetrieveMultipleCallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護第一個動態名單相容 overload 的 service 所有權：傳入的 service 必須是唯一的
    /// 查詢來源，不能被 façade 建構時保存的 service 取代。故障注入以三個不同 marker 的
    /// 本機 collection 模擬不同 profile；決定性斷言是回傳當前操作的 marker，且共享 service
    /// 完全沒有 Retrieve 或 RetrieveMultiple。這些 fake 僅存在於測試方法，沒有外部 I/O、
    /// Session、快取或長生命週期資源。
    /// </summary>
    [Fact]
    public void RetrieveDynamicMemberList_WhenOperationScopedServiceIsProvided_UsesOnlyCurrentService()
    {
        var sharedResult = new EntityCollection(new[] { new Entity("contact") { Id = Guid.NewGuid() } });
        var operationResult = new EntityCollection(new[] { new Entity("contact") { Id = Guid.NewGuid() } });
        var sharedService = new RecordingOrganizationService(sharedResult);
        var operationService = new RecordingOrganizationService(operationResult);
        var facade = new ToolUtilityFacade(sharedService);

        var actual = facade.RetrieveDynamicMemberList(operationService, Guid.NewGuid().ToString());

        actual.Should().BeSameAs(operationResult);
        operationService.RetrieveCallCount.Should().Be(1);
        operationService.RetrieveMultipleCallCount.Should().Be(1);
        sharedService.RetrieveCallCount.Should().Be(0);
        sharedService.RetrieveMultipleCallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 Dynamics 365 動態名單相容 overload 不會回退到 façade 的共享 service。此案例以
    /// 兩個互不相同的 operation-local fake 驗證 proxy 形式的參數仍是當次唯一所有者；
    /// 任一共享查詢都代表可能把前一個 request/profile 的連線狀態帶入目前操作。
    /// </summary>
    [Fact]
    public void RetrieveDynamicMemberListDynamics365_WhenOperationScopedServiceIsProvided_UsesOnlyCurrentService()
    {
        var sharedResult = new EntityCollection(new[] { new Entity("contact") { Id = Guid.NewGuid() } });
        var operationResult = new EntityCollection(new[] { new Entity("contact") { Id = Guid.NewGuid() } });
        var sharedService = new RecordingOrganizationService(sharedResult);
        var operationService = new RecordingOrganizationService(operationResult);
        var facade = new ToolUtilityFacade(sharedService);

        var actual = facade.RetrieveDynamicMemberListDynamics365(operationService, Guid.NewGuid());

        actual.Should().BeSameAs(operationResult);
        operationService.RetrieveCallCount.Should().Be(1);
        operationService.RetrieveMultipleCallCount.Should().Be(1);
        sharedService.RetrieveCallCount.Should().Be(0);
        sharedService.RetrieveMultipleCallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 CRM 2011 動態名單相容 overload 的 request-local service forwarding。測試以
    /// 不同結果 marker 模擬兩個隔離邊界；若實作改回共享 service，回傳值或查詢計數即會
    /// 立即失敗。fake 不保存原始 request 資料，且每個測試結束時均由 GC 回收。
    /// </summary>
    [Fact]
    public void RetrieveDynamicMemberListCrm2011_WhenOperationScopedServiceIsProvided_UsesOnlyCurrentService()
    {
        var sharedResult = new EntityCollection(new[] { new Entity("contact") { Id = Guid.NewGuid() } });
        var operationResult = new EntityCollection(new[] { new Entity("contact") { Id = Guid.NewGuid() } });
        var sharedService = new RecordingOrganizationService(sharedResult);
        var operationService = new RecordingOrganizationService(operationResult);
        var facade = new ToolUtilityFacade(sharedService);

        var actual = facade.RetrieveDynamicMemberListCrm2011(operationService, Guid.NewGuid().ToString());

        actual.Should().BeSameAs(operationResult);
        operationService.RetrieveCallCount.Should().Be(1);
        operationService.RetrieveMultipleCallCount.Should().Be(1);
        sharedService.RetrieveCallCount.Should().Be(0);
        sharedService.RetrieveMultipleCallCount.Should().Be(0);
    }

    /// <summary>
    /// 建立只用於測試的 DownloadListManager，並以共用 ToolUtility 的未初始化版本
    /// 取代其 Factory 欄位。此安排可在任何網路、認證或 CRM 查詢之前驗證 ownership
    /// 邊界；測試方法是唯一 owner，且不會 Dispose 或歸還非自己建立的物件。
    /// </summary>
    private static DownloadListManager CreateManagerWithSharedToolUtility(ToolUtilityClass sharedToolUtility)
    {
        var manager = (DownloadListManager)RuntimeHelpers.GetUninitializedObject(
            typeof(DownloadListManager));
        SetPrivateField(manager, "m_ToolUtilityClass", sharedToolUtility);
        return manager;
    }

    /// <summary>
    /// 呼叫流程直到受控的本機 NullReferenceException 發生。因為受測 manager 未初始化
    /// 其輸出集合，例外必定位於第一個 CRM API 前；若日後流程順序變更，測試應失敗而
    /// 不是意外地連線至 CRM 或吞掉 fault。
    /// </summary>
    private static void InvokeUntilFirstControlledFailure(
        ToolUtilityClass sharedToolUtility,
        IOrganizationService borrowedService)
    {
        var manager = CreateManagerWithSharedToolUtility(sharedToolUtility);
        MultiGroupList outputList = null!;
        MultiGroupChartDataList outputChart = null!;
        string loginType = null!;
        string userType = null!;
        string loginFullName = null!;
        string activeListId = null!;

        var action = () => manager.GetListManager(
            "account",
            "password",
            DateTime.UnixEpoch,
            ref outputList,
            ref outputChart,
            ref loginType,
            ref userType,
            ref loginFullName,
            ref activeListId,
            borrowedService);

        action.Should().Throw<Exception>("故障注入必須在任何外部 CRM I/O 前結束流程");
    }

    /// <summary>
    /// 以反射設定既有私有欄位，避免為測試擴大正式 API。欄位名稱若改變即立即失敗，
    /// 使 ownership 契約的測試不會靜默失效。
    /// </summary>
    private static void SetPrivateField<TInstance, TValue>(TInstance instance, string fieldName, TValue value)
    {
        var field = typeof(TInstance).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{fieldName} 是本測試保護的既有生命週期邊界");
        field!.SetValue(instance, value);
    }

    /// <summary>
    /// 不可呼叫的 CRM service 假物件。任何正式流程若在受控 fault 之前觸碰它，測試會
    /// 立即拋出，而不是產生網路 I/O、保存連線或把資料留在測試程序中。
    /// </summary>
    private sealed class ThrowingOrganizationService : IOrganizationService
    {
        /// <inheritdoc />
        public Guid Create(Entity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Update(Entity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        /// <inheritdoc />
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();

        /// <inheritdoc />
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();

        /// <inheritdoc />
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }

    /// <summary>
    /// 記錄名單查詢的最小 CRM service fake。它只允許 <see cref="RetrieveMultiple"/>
    /// 回傳測試指定 collection；其餘 API 一律失敗，避免測試意外越過資料流邊界而產生
    /// 外部 I/O。每個實例只記錄自己方法範圍內的計數，不使用 static 或跨測試狀態。
    /// </summary>
    private sealed class RecordingOrganizationService : IOrganizationService
    {
        private readonly EntityCollection? _retrieveMultipleResult;

        /// <summary>
        /// 初始化可選的查詢結果；未指定時，以受控例外表示不應使用該 service。
        /// </summary>
        /// <param name="retrieveMultipleResult">僅供目前 fake 回傳的本機 collection。</param>
        public RecordingOrganizationService(EntityCollection? retrieveMultipleResult = null)
        {
            _retrieveMultipleResult = retrieveMultipleResult;
        }

        /// <summary>
        /// 取得本實例接受的名單查詢次數，用於證明沒有跨 service 路由。
        /// </summary>
        public int RetrieveMultipleCallCount { get; private set; }

        /// <summary>
        /// 取得本實例接受的 list 實體讀取次數。動態名單必須先讀取 query，因此此計數與
        /// RetrieveMultiple 一起證明 query 及結果都沒有錯誤地跨操作借用共享 service。
        /// </summary>
        public int RetrieveCallCount { get; private set; }

        /// <inheritdoc />
        public Guid Create(Entity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Update(Entity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        /// <inheritdoc />
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();

        /// <inheritdoc />
        /// <summary>
        /// 僅模擬動態名單所需的 list query。回傳的 FetchXML 固定且不含任何真實 endpoint、
        /// CRM ID 或 request state；其他 entity 表示測試越界並立即失敗。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            RetrieveCallCount++;
            if (!string.Equals(entityName, "list", StringComparison.Ordinal))
            {
                throw new NotSupportedException();
            }

            return new Entity("list", id)
            {
                ["query"] = "<fetch><entity name='contact' /></fetch>"
            };
        }

        /// <summary>
        /// 僅回傳建構時綁定的 collection。缺少結果代表呼叫流程誤用共享 service，
        /// 立即以受控失敗停止，確保測試不會與 CRM 建立任何連線。
        /// </summary>
        /// <param name="query">由 façade 產生的名單查詢；不保存於 fake 外部。</param>
        /// <returns>此 operation-scoped fake 的固定 collection。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            RetrieveMultipleCallCount++;
            return _retrieveMultipleResult ?? throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
