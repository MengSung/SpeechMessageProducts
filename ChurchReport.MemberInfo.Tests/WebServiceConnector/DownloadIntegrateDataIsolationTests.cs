// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadIntegrateDataIsolationTests.cs
// 檔案責任：鎖定 ListManager 至 DownloadIntegrateData 的借用 CRM service 只能存在於單次
// 操作呼叫堆疊，並在例外後立即失去參考，防止 session 快取的 ListManager 對下一位使用者、
// profile 或 connector generation 洩漏可變連線狀態。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，且必須以 final CRLF 結尾。
// ============================================================================

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WebServiceConnector;

/// <summary>
/// 驗證整合資料下載流程的 operation-local CRM service 隔離與借用生命週期。
///
/// <para>
/// 此測試以 A/B marker 的本機 <see cref="IOrganizationService"/> fake 模擬不同使用者或
/// profile。這個舊的 ListManager 過渡 overload 仍會讀取 session-cache instance state，
/// 因此必須在 CRM I/O 前 fail closed；決定性斷言是 A 不會讓 B 使用 A、任一 service 不會被
/// Dispose，且 session 可能快取的 <see cref="ListManager"/> 與
/// <see cref="DownloadIntegrateData"/> 均不保留借用 service。完整 operation-local 流程
/// 由 DownloadIntegrateDataOperationServiceIntegrationTests 另行驗證。
/// </para>
/// </summary>
public sealed class DownloadIntegrateDataIsolationTests
{
    /// <summary>
    /// 保護 service-aware 整合下載 API 的 fail-closed 邊界。先以 A 觸發受控例外，再以 B
    /// 重試；此 fault injection 證明前一次借用 service 不會被寫進 ListManager、
    /// DownloadIntegrateData、Factory、ToolUtility 或快取，也不會被借用者 Dispose。
    /// </summary>
    [Fact]
    public void SetupIntegrateData_WhenBorrowedServiceCannotBeSafelyForwarded_FailsClosedWithoutRetainingOrDisposingEitherMarker()
    {
        var listManager = (ListManager)RuntimeHelpers.GetUninitializedObject(typeof(ListManager));
        var downloadIntegrateData = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(
            typeof(DownloadIntegrateData));
        SetPrivateField(listManager, "m_DownloadIntegrateData", downloadIntegrateData);
        var firstOperationService = new MarkerOrganizationService("A");
        var secondOperationService = new MarkerOrganizationService("B");

        var setupMethod = typeof(ListManager).GetMethod(
            "SetupIntegrateData",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string), typeof(IOrganizationService) },
            modifiers: null);

        setupMethod.Should().NotBeNull(
            "借用 service 必須由 ListManager 明確傳給 DownloadIntegrateData，不能回落至共享 ToolUtility");

        InvokeAndAssertFailClosed(listManager, setupMethod!, firstOperationService);
        InvokeAndAssertFailClosed(listManager, setupMethod!, secondOperationService);

        firstOperationService.CallCount.Should().Be(0,
            "ListManager 過渡 overload 必須在讀取 session state 或 CRM I/O 前拒絕");
        secondOperationService.CallCount.Should().Be(0,
            "service-aware 路徑在不能完整保證 operation-local forwarding 時必須在 CRM I/O 前拒絕");
        firstOperationService.DisposeCount.Should().Be(0,
            "DownloadIntegrateData 只是借用 A，釋放仍屬於原 lease owner");
        secondOperationService.DisposeCount.Should().Be(0,
            "DownloadIntegrateData 只是借用 B，釋放仍屬於原 lease owner");

        AssertDoesNotRetainBorrowedService(listManager, firstOperationService);
        AssertDoesNotRetainBorrowedService(listManager, secondOperationService);
        AssertDoesNotRetainBorrowedService(downloadIntegrateData, firstOperationService);
        AssertDoesNotRetainBorrowedService(downloadIntegrateData, secondOperationService);
    }

    /// <summary>
    /// 呼叫 ListManager 過渡 overload 並驗證它以固定例外停止。這個測試刻意不建立真實 CRM
    /// 連線；完整 operation-local 流程改由 DownloadIntegrateData 的明確輸入 API 驗證。
    /// </summary>
    /// <param name="listManager">模擬 session 快取容器的測試專用 manager。</param>
    /// <param name="setupMethod">受測的 service-aware overload。</param>
    /// <param name="service">本操作借用且不可由受測物釋放的 marker service。</param>
    private static void InvokeAndAssertFailClosed(
        ListManager listManager,
        MethodInfo setupMethod,
        IOrganizationService service)
    {
        var outer = Xunit.Record.Exception(() => setupMethod.Invoke(
            listManager,
            new object[] { "operation-local-list", service }));

        var invocation = outer.Should().BeOfType<TargetInvocationException>().Subject;
        invocation.InnerException.Should().BeOfType<InvalidOperationException>(
            "在 legacy partial 仍可能讀取共用 ToolUtility service 欄位時，借用 service 不可被靜默回落或保存");
    }

    /// <summary>
    /// 掃描物件的 instance fields，確認任何 service reference 都未被保存。掃描只檢查目前
    /// 受測物，不接觸 static 或 Factory 全域物件，避免測試本身建立跨測試可變狀態。
    /// </summary>
    /// <param name="instance">可能被 session 保留的容器。</param>
    /// <param name="borrowedService">本次操作傳入的借用 service。</param>
    private static void AssertDoesNotRetainBorrowedService(object instance, IOrganizationService borrowedService)
    {
        var retainedField = instance.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(field => ReferenceEquals(field.GetValue(instance), borrowedService));

        retainedField.Should().BeNull(
            $"{instance.GetType().Name} 不得把借用 CRM service 寫入 instance field，否則下一個操作可能跨使用者或 profile 重用它");
    }

    /// <summary>
    /// 以反射設定既有私有欄位，避免為隔離測試擴大正式 API。若欄位改名，測試會立即失敗，
    /// 使 session 容器與 DownloadIntegrateData 的持有關係必須被重新審查。
    /// </summary>
    private static void SetPrivateField<TInstance, TValue>(TInstance instance, string fieldName, TValue value)
    {
        var field = typeof(TInstance).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{fieldName} 是此測試保護的既有 session 容器邊界");
        field!.SetValue(instance, value);
    }

    /// <summary>
    /// 可追蹤的 operation-local CRM service fake。任何 CRM API 呼叫都會使測試失敗，確保
    /// fail-closed 行為發生在網路、認證、Session 或可重用連線建立之前；<see cref="Dispose"/>
    /// 計數用來保護借用者絕不釋放呼叫端 owner 的資源。
    /// </summary>
    private sealed class MarkerOrganizationService : IOrganizationService, IDisposable
    {
        /// <summary>
        /// 建立只供本測試診斷使用的 marker；不寫入 static、快取或外部記錄。
        /// </summary>
        /// <param name="marker">可區分 A/B 操作的短生命期測試標記。</param>
        public MarkerOrganizationService(string marker)
        {
            Marker = marker;
        }

        /// <summary>
        /// 取得 A/B marker，僅讓失敗訊息能辨別本機 fake，不對外傳輸。
        /// </summary>
        public string Marker { get; }

        /// <summary>
        /// 取得任一 CRM API 被呼叫的次數；安全路徑必須維持零。
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// 取得假物件被釋放的次數；借用流程必須維持零。
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// 若 fail-closed 邊界錯誤地建立 CRM 實體，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public Guid Create(Entity entity) => ThrowUnexpectedCall<Guid>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地更新 CRM 實體，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public void Update(Entity entity) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地刪除 CRM 實體，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public void Delete(string entityName, Guid id) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地執行 CRM request，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request) => ThrowUnexpectedCall<OrganizationResponse>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地讀取 CRM entity，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => ThrowUnexpectedCall<Entity>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地執行 CRM 集合查詢，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => ThrowUnexpectedCall<EntityCollection>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地建立 CRM 關聯，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 若 fail-closed 邊界錯誤地移除 CRM 關聯，立即記錄本 fake 被誤用並停止測試。
        /// </summary>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 記錄不應到達的 CRM 呼叫後停止測試。沒有外部資源可釋放，因此此方法只驗證
        /// 呼叫端的借用契約，並不模擬或取代正式 lease owner。
        /// </summary>
        public void Dispose()
        {
            DisposeCount++;
        }

        /// <summary>
        /// 將所有意外 CRM 呼叫集中為相同的本機失敗，避免 fake 各自保存 request state。
        /// </summary>
        /// <typeparam name="T">遭到錯誤呼叫之 CRM API 的回傳型別。</typeparam>
        /// <returns>此方法一律擲回，不會回傳。</returns>
        private T ThrowUnexpectedCall<T>()
        {
            CallCount++;
            throw new InvalidOperationException($"marker {Marker} 不應在 fail-closed 前執行 CRM I/O。");
        }
    }
}
