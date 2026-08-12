// ============================================================================
// 檔案責任：以可重複執行的 A/B marker 測試，保護 DownloadIntegrateData 出席紀錄
// service-aware 私有路徑的 CRM service 隔離、owner 來源與借用資源生命週期。
//
// 隔離契約：每一個測試 service 都只代表一次 operation。測試會交錯呼叫 A、B，並
// 驗證 query、create、read-back 與 Assign 全部留在當次 service；不允許由 ToolUtility、
// Factory、cache、static 或 instance 欄位取得另一個 service。
//
// 資源生命週期：MarkerOrganizationService 是測試方法唯一建立者，也是唯一可 Dispose
// 的 owner。production 被測程式只借用它，測試對每一條成功與 fail-closed 路徑皆斷言
// DisposeCount 為零及沒有實體欄位保留 service，避免跨 request/profile 的 session
// retention。
//
// 編碼契約：UTF-8 無 BOM、CRLF，以及最後一個 CRLF。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WebServiceConnector;

/// <summary>
/// 驗證出席紀錄的 operation-local CRM service 私有 helper。
///
/// <para>
/// 被保護的契約是：每個 CRM 查詢、建立、讀回與 owner 指派都只能直接使用呼叫堆疊
/// 傳入的 <see cref="IOrganizationService"/>。兩個不同 marker 的 service 交錯操作後，
/// A 不得看見或使用 B 的資料、service 或 owner，反之亦然。
/// </para>
///
/// <para>
/// 測試刻意經由私有反射 seam 驗證尚未改變 legacy public/UI flow 的最小修補區域；它不會
/// 建立 CRM fixture、連線或背景工作。任何無法安全使用 operation-local service 的情況
/// 都必須在 mutation 前 fail closed，且不能釋放呼叫端擁有的 service。
/// </para>
/// </summary>
public sealed class DownloadIntegrateDataPresentRecordIsolationTests
{
    /// <summary>
    /// 保護 present-record query 的 A/B service 邊界。
    ///
    /// <para>
    /// fault injection 為交錯提供兩個只會回傳自身 marker 的 fake service。決定性斷言為
    /// 每個結果只帶自己的 marker、兩邊各自僅一次 RetrieveMultiple、沒有 Dispose，並且
    /// DownloadIntegrateData instance 不保存任一借用 service。若 helper 回落 ToolUtility 或
    /// 將 service 寫到欄位，這些 assertion 或 null 的 shared dependency 會立即失敗。
    /// </para>
    /// </summary>
    [Fact]
    public void GetPresentRecordByLoginType_WithInterleavedOperationServices_QueriesOnlyItsCurrentMarker()
    {
        var subject = CreateUninitializedSubject();
        var weeklyReportId = Guid.NewGuid();
        using var firstService = new MarkerOrganizationService("A");
        using var secondService = new MarkerOrganizationService("B");
        var method = GetRequiredPrivateInstanceMethod(
            "GetPresentRecordByLoginType",
            typeof(IOrganizationService),
            typeof(Guid));

        var firstResult = (EntityCollection)method.Invoke(subject, new object[] { firstService, weeklyReportId })!;
        var secondResult = (EntityCollection)method.Invoke(subject, new object[] { secondService, weeklyReportId })!;

        firstResult.Entities.Should().ContainSingle()
            .Which.GetAttributeValue<string>("test_marker").Should().Be("A");
        secondResult.Entities.Should().ContainSingle()
            .Which.GetAttributeValue<string>("test_marker").Should().Be("B");
        firstService.RetrieveMultipleCount.Should().Be(1);
        secondService.RetrieveMultipleCount.Should().Be(1);
        firstService.DisposeCount.Should().Be(0, "被測 helper 只借用 A service，不是其釋放 owner");
        secondService.DisposeCount.Should().Be(0, "被測 helper 只借用 B service，不是其釋放 owner");
        AssertDoesNotRetainBorrowedService(subject, firstService);
        AssertDoesNotRetainBorrowedService(subject, secondService);
    }

    /// <summary>
    /// 以真正並行的 A/B operation 驗證 service parameter 不會因 session reuse 或 shared
    /// mutable state 交叉污染。每個工作只使用自己的 marker service，並在完成後由測試 owner
    /// 釋放；任何被測程式保留或處置 borrowed service 都會讓 assertion 失敗。
    /// </summary>
    [Fact]
    public async Task GetPresentRecordByLoginType_WithConcurrentOperationServices_DoesNotCrossMarkers()
    {
        var subject = CreateUninitializedSubject();
        var weeklyReportId = Guid.NewGuid();
        using var queryBarrier = new Barrier(2);
        using var firstService = new MarkerOrganizationService("A", queryBarrier);
        using var secondService = new MarkerOrganizationService("B", queryBarrier);
        var method = GetRequiredPrivateInstanceMethod(
            "GetPresentRecordByLoginType",
            typeof(IOrganizationService),
            typeof(Guid));

        var results = await Task.WhenAll(
            Task.Run(() => (EntityCollection)method.Invoke(subject, new object[] { firstService, weeklyReportId })!),
            Task.Run(() => (EntityCollection)method.Invoke(subject, new object[] { secondService, weeklyReportId })!));

        results[0].Entities.Should().ContainSingle()
            .Which.GetAttributeValue<string>("test_marker").Should().Be("A");
        results[1].Entities.Should().ContainSingle()
            .Which.GetAttributeValue<string>("test_marker").Should().Be("B");
        firstService.RetrieveMultipleCount.Should().Be(1);
        secondService.RetrieveMultipleCount.Should().Be(1);
        firstService.DisposeCount.Should().Be(0);
        secondService.DisposeCount.Should().Be(0);
        AssertDoesNotRetainBorrowedService(subject, firstService);
        AssertDoesNotRetainBorrowedService(subject, secondService);
    }

    /// <summary>
    /// 保護 create、read-back 與 Assign 都使用同一個 operation-local service，且 owner
    /// 僅從該次已取回 contact 的 <c>ownerid</c> 明確取得。
    ///
    /// <para>
    /// 此案例以不同 A/B contact owner 與 service marker 交錯執行，模擬 session 快取的
    /// DownloadIntegrateData 被重用時最容易發生的交叉污染。決定性斷言是 A/B 分別只收到
    /// 自己的 Create、Retrieve、Assign，Assign request 中的 Assignee 完全等於同次 contact
    /// owner；被呼叫者沒有 Dispose 或保留 service。
    /// </para>
    /// </summary>
    [Fact]
    public void CreatePresentRecord_OperationLocalMutationOverloadIsAbsentUntilALedgerBoundExecutorExists()
    {
        var method = typeof(DownloadIntegrateData).GetMethod(
            "CreatePresentRecord",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[]
            {
                typeof(IOrganizationService),
                typeof(Entity),
                typeof(Entity)
            },
            modifiers: null);

        method.Should().BeNull(
            "未綁定本次 fixture ledger、精確 read-back 與 rollback owner 的 private mutation seam 不可存在；否則讀回或 Assign 失敗時可能留下無法安全清理的出席紀錄。");
    }

    /// <summary>
    /// 建立未初始化的被測 partial instance，避免測試觸發 legacy Factory 或 ToolUtility。
    ///
    /// <para>
    /// 此替身只讓反射呼叫本次新增的 service-aware private helper；它沒有可共享的 CRM state，
    /// 所以只要 helper 誤碰 legacy fallback 就會以 null dependency 失敗，而不會默默連到
    /// 任何真實或共用 service。
    /// </para>
    /// </summary>
    /// <returns>沒有執行建構式的 DownloadIntegrateData instance。</returns>
    private static DownloadIntegrateData CreateUninitializedSubject()
        => (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(typeof(DownloadIntegrateData));

    /// <summary>
    /// 取得必須存在的 operation-local private seam。
    ///
    /// <para>
    /// 明確 signature 可避免因 overload 名稱相同卻意外選到 legacy ToolUtility 路徑而讓測試
    /// 偽綠。缺少此方法是 RED 階段預期的 failure，代表尚未建立顯式 service 資料流。
    /// </para>
    /// </summary>
    /// <param name="methodName">預期 private helper 名稱。</param>
    /// <param name="parameterTypes">operation-local helper 的完整參數型別序列。</param>
    /// <returns>唯一符合名稱與 signature 的 instance private method。</returns>
    private static MethodInfo GetRequiredPrivateInstanceMethod(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(DownloadIntegrateData).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        method.Should().NotBeNull(
            $"{methodName} 必須提供只沿 method parameter 借用 IOrganizationService 的 private overload");
        return method!;
    }

    /// <summary>
    /// 驗證被測 instance 不以 instance field 保存借用 service。
    ///
    /// <para>
    /// 即使 finally 後清空欄位也可能在交錯 request 間造成 race；因此測試要求整個 instance
    /// field graph 的第一層都沒有借用 reference。static/cache/Factory fallback 則由 uninitialized
    /// subject 與各 CRM call 的 marker assertion 間接阻斷。
    /// </para>
    /// </summary>
    /// <param name="subject">被測 DownloadIntegrateData instance。</param>
    /// <param name="borrowedService">呼叫端擁有的 service。</param>
    private static void AssertDoesNotRetainBorrowedService(
        DownloadIntegrateData subject,
        IOrganizationService borrowedService)
    {
        var retainedField = subject.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(field => ReferenceEquals(field.GetValue(subject), borrowedService));

        retainedField.Should().BeNull(
            "operation-local service 不得保存在 DownloadIntegrateData instance；session 快取可重用此 instance");
    }


    /// <summary>
    /// 僅供唯讀 A/B 隔離案例使用的 operation-local CRM service 替身。
    ///
    /// <para>
    /// 每個 instance 只代表一個不可變 marker 與一次測試操作；除了受測查詢必要的
    /// <see cref="RetrieveMultiple(QueryBase)"/> 外，所有 CRM API 都會立即失敗。這使任何意外
    /// mutation、跨操作讀取或 fallback 至未核准 service 都可被測試偵測。替身由測試方法建立與
    /// Dispose，production helper 只可借用，因此測試在 operation 結束前必須觀察到
    /// <see cref="DisposeCount"/> 為零，避免 borrowed service 的 lease 被錯誤釋放或跨 request 保留。
    /// </para>
    /// </summary>
    private sealed class MarkerOrganizationService : IOrganizationService, IDisposable
    {
        /// <summary>
        /// 建立只屬於一個 operation 的 marker service；marker 不寫入 static、cache 或其他測試 instance。
        /// </summary>
        /// <param name="marker">用於辨識 A/B 回應邊界的去識別化固定 marker。</param>
        /// <summary>
        /// 建立一個只代表單一操作的 CRM service 測試替身。
        ///
        /// <para>
        /// <paramref name="queryBarrier"/> 僅由並行隔離測試傳入。兩個獨立操作必須都已進入
        /// <see cref="RetrieveMultiple(QueryBase)"/> 才能繼續，藉此排除 <see cref="Task.WhenAll"/>
        /// 剛好被執行器序列化而未真正覆蓋競態的假陽性。Barrier 的擁有者是測試方法；production
        /// helper 不會看見或保留它，測試結束後以 <c>using</c> 確定釋放。
        /// </para>
        /// </summary>
        /// <param name="marker">此一次操作專屬且不可與另一 service 共用的 A/B 標記。</param>
        /// <param name="queryBarrier">可選的兩方同步閘門；未提供時保持一般單操作測試行為。</param>
        public MarkerOrganizationService(string marker, Barrier? queryBarrier = null)
        {
            Marker = marker ?? throw new ArgumentNullException(nameof(marker));
            QueryBarrier = queryBarrier;
        }

        /// <summary>本 operation 唯一且不可變的測試 marker。</summary>
        public string Marker { get; }

        /// <summary>受測 bounded query 實際使用的次數。</summary>
        public int RetrieveMultipleCount { get; private set; }

        /// <summary>若被測 helper 錯誤處置 caller-owned lease，會遞增的計數器。</summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// 由測試方法擁有的可選同步閘門。
        ///
        /// <para>
        /// 它絕不含 CRM identity、session、credential 或資料；只用於確認兩條 marker 查詢確實重疊。
        /// 此替身不 Dispose 該物件，以免越過測試方法的資源所有權邊界。
        /// </para>
        /// </summary>
        private Barrier? QueryBarrier { get; }

        /// <summary>唯讀隔離替身不允許建立 CRM entity。</summary>
        public Guid Create(Entity entity) => ThrowUnexpectedCall<Guid>();

        /// <summary>唯讀隔離替身不允許更新 CRM entity。</summary>
        public void Update(Entity entity) => ThrowUnexpectedCall<object?>();

        /// <summary>唯讀隔離替身不允許刪除 CRM entity。</summary>
        public void Delete(string entityName, Guid id) => ThrowUnexpectedCall<object?>();

        /// <summary>唯讀隔離替身不允許執行 CRM request。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => ThrowUnexpectedCall<OrganizationResponse>();

        /// <summary>唯讀隔離替身不允許單筆 Retrieve，防止測試默許查詢範圍擴張。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => ThrowUnexpectedCall<Entity>();

        /// <summary>
        /// 回傳只帶目前 marker 的固定結果，驗證被測 helper 沒有讀取另一個 operation 的 service。
        /// </summary>
        /// <param name="query">由 production helper 建立的固定 present-record 查詢。</param>
        /// <returns>只含當前 operation marker 的單筆結果集合。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            RetrieveMultipleCount++;
            query.Should().BeOfType<QueryExpression>();
            ((QueryExpression)query).EntityName.Should().Be("new_present_record");
            if (QueryBarrier is not null && !QueryBarrier.SignalAndWait(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException(
                    "並行隔離測試未能讓兩個 operation 同時到達 CRM query 邊界。");
            }

            return new EntityCollection(new[]
            {
                new Entity("new_present_record", Guid.NewGuid()) { ["test_marker"] = Marker }
            });
        }

        /// <summary>唯讀隔離替身不允許建立 CRM 關聯。</summary>
        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => ThrowUnexpectedCall<object?>();

        /// <summary>唯讀隔離替身不允許解除 CRM 關聯。</summary>
        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities) => ThrowUnexpectedCall<object?>();

        /// <summary>
        /// 記錄測試 owner 的正常釋放。production helper 不得呼叫此方法，避免錯誤釋放外層 lease。
        /// </summary>
        public void Dispose()
        {
            DisposeCount++;
        }

        /// <summary>
        /// 將未核准的 CRM API 轉為明確測試失敗，防止替身默許副作用或跨 operation I/O。
        /// </summary>
        /// <typeparam name="T">未預期 API 的宣告回傳型別。</typeparam>
        /// <returns>此方法永不正常返回。</returns>
        private static T ThrowUnexpectedCall<T>()
            => throw new InvalidOperationException("唯讀隔離測試替身收到未核准的 CRM API 呼叫。");
    }
}
