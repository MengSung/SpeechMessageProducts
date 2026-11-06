/*
 * 檔案整體說明
 *
 * 目的與定位：實際執行既有 ContactService.AssignOwner 與 ToolUtilityClass／Facade／Core 委派鏈， 僅替代最末端 SDK I/O。保護 logical name、資料庫
 * ID、owner 與原始故障不被搬移改壞。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactOwnerRouteTests、OwnerRouteFixture、OwnerRouteSdkProxy。
 * - 既有聯絡人業務方法必須先讀 contact，再以相同資料庫 ID 指派。 注入不同 owner 與目標 GUID，防止測試只因兩個 ID 相同而漏掉錯接。
 * - SDK 回錯資料表時，既有業務方法經 Core 讀取驗證即失敗，不能向錯誤目標繼續指派。 決定性斷言為讀取一次、Assign 零次，避免以錯 Entity.LogicalName 繞過新檢查。
 * - 真實業務／Facade 鏈遇到 SDK 指派逾時時保留同一 Exception，且不隱藏重試。 此處不執行最終通知邊界，也不把 provider 的原始訊息當成安全輸出。
 * - 依既有 35 處呼叫的八種資料表，實際執行 RetrieveEntity→AssignOwner 公開相容路徑。 各案例只用合成資料，保留所有 EntityReference 名稱與 GUID；
 * 不假稱逐一執行全部業務分支。
 * 測試案例入口包含 ContactService_AssignOwner_TraversesActualFacadeWithMatchingIdentity、
 * ContactService_MismatchedRetrievedEntity_StopsBeforeAssignment、
 * ContactService_AssignmentFailure_PreservesOriginalExceptionWithoutRetry、
 * LegacyFacade_RetrieveThenAssign_PreservesEveryObservedEntityName、CreateContactService、AssertSingleAssignment、
 * Dispose、Invoke。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Services.Contact.Impl;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Integration;

/// <summary>
/// 實際執行既有 ContactService.AssignOwner 與 ToolUtilityClass／Facade／Core 委派鏈，
/// 僅替代最末端 SDK I/O。保護 logical name、資料庫 ID、owner 與原始故障不被搬移改壞。
/// </summary>
/// <remarks>
/// ContactService 建構式另建立 LINE client 並讀取設定，與本方法無關；
/// 測試明確略過該建構式，只注入此方法唯一使用的既有 ToolUtilityClass。
/// 因此證據限於已編譯業務方法及真實委派鏈，不代表 DI 啟動、授權或實機 D365 整合。
/// 每個案例獨占 SDK、設定與 utility；不讀應用設定、帳密、不發網路請求或 LINE。
/// </remarks>
public sealed class ContactOwnerRouteTests
{
    /// <summary>
    /// 既有聯絡人業務方法必須先讀 contact，再以相同資料庫 ID 指派。
    /// 注入不同 owner 與目標 GUID，防止測試只因兩個 ID 相同而漏掉錯接。
    /// </summary>
    [Fact]
    public void ContactService_AssignOwner_TraversesActualFacadeWithMatchingIdentity()
    {
        using var fixture = new OwnerRouteFixture();
        var service = CreateContactService(fixture.Utility);

        service.AssignOwner(fixture.RecordId, fixture.OwnerId);

        fixture.AssertSingleAssignment("contact");
    }

    /// <summary>
    /// SDK 回錯資料表時，既有業務方法經 Core 讀取驗證即失敗，不能向錯誤目標繼續指派。
    /// 決定性斷言為讀取一次、Assign 零次，避免以錯 Entity.LogicalName 繞過新檢查。
    /// </summary>
    [Fact]
    public void ContactService_MismatchedRetrievedEntity_StopsBeforeAssignment()
    {
        using var fixture = new OwnerRouteFixture();
        fixture.State.ReturnedEntityName = "account";

        Assert.Throws<InvalidOperationException>(() =>
            CreateContactService(fixture.Utility).AssignOwner(fixture.RecordId, fixture.OwnerId));

        Assert.Equal(1, fixture.State.RetrieveCount);
        Assert.Equal(0, fixture.State.AssignCount);
    }

    /// <summary>
    /// 真實業務／Facade 鏈遇到 SDK 指派逾時時保留同一 Exception，且不隱藏重試。
    /// 此處不執行最終通知邊界，也不把 provider 的原始訊息當成安全輸出。
    /// </summary>
    [Fact]
    public void ContactService_AssignmentFailure_PreservesOriginalExceptionWithoutRetry()
    {
        using var fixture = new OwnerRouteFixture();
        var failure = new TimeoutException("synthetic-assignment-fault");
        fixture.State.AssignmentFailure = failure;

        Assert.Same(failure, Assert.Throws<TimeoutException>(() =>
            CreateContactService(fixture.Utility).AssignOwner(fixture.RecordId, fixture.OwnerId)));

        fixture.AssertSingleAssignment("contact");
    }

    /// <summary>
    /// 依既有 35 處呼叫的八種資料表，實際執行 RetrieveEntity→AssignOwner 公開相容路徑。
    /// 各案例只用合成資料，保留所有 EntityReference 名稱與 GUID；不假稱逐一執行全部業務分支。
    /// </summary>
    /// <param name="entityName">從既有呼叫盤點取得的固定 logical name，不是外部授權資料。</param>
    [Theory]
    [InlineData("contact")]
    [InlineData("new_present_record")]
    [InlineData("new_stor_lessons")]
    [InlineData("new_fee")]
    [InlineData("new_group_present_weekly_report")]
    [InlineData("list")]
    [InlineData("appointment")]
    [InlineData("new_dedication_booking")]
    public void LegacyFacade_RetrieveThenAssign_PreservesEveryObservedEntityName(string entityName)
    {
        using var fixture = new OwnerRouteFixture();
        var entity = fixture.Utility.RetrieveEntity(entityName, fixture.RecordId);

        fixture.Utility.AssignOwner(entityName, entity, fixture.OwnerId);

        fixture.AssertSingleAssignment(entityName);
    }

    /// <summary>
    /// 只為測試隔離無關 LINE 建構副作用；取得真實已編譯 ContactService，
    /// 並明確設入 AssignOwner 唯一存取的依賴。欄位更名會直接令測試失敗。
    /// </summary>
    private static ContactService CreateContactService(ToolUtilityClass utility)
    {
        var service = (ContactService)RuntimeHelpers.GetUninitializedObject(typeof(ContactService));
        var dependency = typeof(ContactService).GetField("_toolUtility", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dependency);
        dependency.SetValue(service, utility);
        return service;
    }

    /// <summary>
    /// 擁有單一案例的 tracer／utility 與 SDK 替身，先釋放 utility 再驗證借入 SDK 未被 Dispose，
    /// 最後由 fixture 釋放自己的 tracer／替身。不存在 static 使用者狀態或外部連線。
    /// </summary>
    private sealed class OwnerRouteFixture : IDisposable
    {
        private readonly NullToolUtilityTracer tracer = new();

        /// <summary>固定合成資料庫 ID；與 owner 不同，能辨識錯誤參數接線。</summary>
        internal Guid RecordId { get; } = new("11111111-1111-1111-1111-111111111111");

        /// <summary>固定合成 owner ID，並不代表真實系統帳戶或授權。</summary>
        internal Guid OwnerId { get; } = new("22222222-2222-2222-2222-222222222222");

        /// <summary>本案例獨占的 SDK 呼叫觀察器，不能與其他案例共用。</summary>
        internal OwnerRouteSdkProxy State { get; }

        /// <summary>正式 ToolUtilityClass，由它建立真實 Facade 與 Core 服務。</summary>
        internal ToolUtilityClass Utility { get; }

        /// <summary>建立無秘密的空設定與純 SDK 邊界，未建立真實 CRM／LINE client。</summary>
        internal OwnerRouteFixture()
        {
            var sdk = DispatchProxy.Create<IOrganizationService, OwnerRouteSdkProxy>();
            State = (OwnerRouteSdkProxy)(object)sdk;
            Utility = new ToolUtilityClass(sdk, tracer, new ConfigurationBuilder().Build());
        }

        /// <summary>比對 SDK 最終請求的完整目標、owner 及唯一呼叫數，不從產品結果推導預期值。</summary>
        internal void AssertSingleAssignment(string entityName)
        {
            Assert.Equal(1, State.RetrieveCount);
            Assert.Equal(1, State.AssignCount);
            var request = Assert.IsType<AssignRequest>(State.Assignment);
            Assert.Equal(entityName, request.Target.LogicalName);
            Assert.Equal(RecordId, request.Target.Id);
            Assert.Equal("systemuser", request.Assignee.LogicalName);
            Assert.Equal(OwnerId, request.Assignee.Id);
            Assert.Equal(RecordId, State.RetrievedId);
        }

        /// <summary>utility 不接管借入 SDK；即使斷言失敗，finally 仍回收 fixture 自有物件。</summary>
        public void Dispose()
        {
            try
            {
                Utility.Dispose();
                Assert.Equal(0, State.DisposeCount);
            }
            finally
            {
                tracer.Dispose();
                State.Dispose();
            }
        }
    }
}

/// <summary>
/// 僅替代 SDK 的兩個 I/O 方法；不重作 logical-name／ID 驗證、不操作檔案或網路。
/// 所有觀察資料只屬單一測試 fixture，Dispose 確定性清除暫存請求與故障參照。
/// </summary>
public class OwnerRouteSdkProxy : DispatchProxy, IDisposable
{
    /// <summary>讀取呼叫次數；本替身僅供單執行緒的有界案例使用。</summary>
    public int RetrieveCount { get; private set; }

    /// <summary>指派呼叫次數，故障亦計入，藉此發現隱藏重試。</summary>
    public int AssignCount { get; private set; }

    /// <summary>測試可注入錯誤回應表名；null 表示沿用原請求值。</summary>
    public string? ReturnedEntityName { get; set; }

    /// <summary>本次讀取要求的資料庫 ID，不保存實體或 caller 服務。</summary>
    public Guid RetrievedId { get; private set; }

    /// <summary>捕捉 SDK 最終指派封包，fixture Dispose 時清除。</summary>
    public OrganizationRequest? Assignment { get; private set; }

    /// <summary>只屬本案例的合成故障，不包含 provider／使用者資料。</summary>
    public Exception? AssignmentFailure { get; set; }

    /// <summary>借入 SDK 被處置次數；utility 結束前應保持零。</summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// 只回傳原查詢名稱／ID 或記錄原 AssignRequest；未知呼叫立即拒絕，
    /// 避免業務新增 I/O 後被寬鬆替身默默接受。AllColumns 亦由獨立斷言保護。
    /// </summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Assert.NotNull(args);
        if (targetMethod?.Name == nameof(IOrganizationService.Retrieve))
        {
            RetrieveCount++;
            RetrievedId = Assert.IsType<Guid>(args[1]);
            Assert.True(Assert.IsType<Microsoft.Xrm.Sdk.Query.ColumnSet>(args[2]).AllColumns);
            return new Entity(ReturnedEntityName ?? Assert.IsType<string>(args[0]), RetrievedId);
        }
        if (targetMethod?.Name == nameof(IOrganizationService.Execute))
        {
            AssignCount++;
            Assignment = Assert.IsType<AssignRequest>(args[0]);
            if (AssignmentFailure is not null) throw AssignmentFailure;
            return new AssignResponse();
        }
        throw new InvalidOperationException("Unexpected SDK method in owner route test.");
    }

    /// <summary>只清本 fixture 的觀察參照；沒有需釋放的真實 SDK／連線。</summary>
    public void Dispose()
    {
        DisposeCount++;
        Assignment = null;
        AssignmentFailure = null;
    }
}
