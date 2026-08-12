// ============================================================================
// AI-繁體中文檔案註解
// 檔案責任：驗證 DownloadIntegrateData 的 operation-local 入口只使用本次傳入的 CRM service，
// 並且只在整個讀取流程成功後才提交輸出報表，避免 Session 快取物件殘留前一位使用者的連線或資料。
// 資源生命週期：fake service 由測試呼叫端持有；被測程式不得 Dispose、快取或寫入該 service。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.WebServiceConnector;

/// <summary>
/// 驗證下載整合資料的 operation-local CRM service 入口。
///
/// <para>
/// 此測試以兩個不可互換的 A/B marker service 模擬同一個 Session 快取管理物件的連續操作。
/// 保護的契約是：每次呼叫只能從當次 service 讀取資料、不得保存或釋放借用 service，且失敗前
/// 不可覆寫既有報表引用。這能防止 service、CRM entity 或半成品輸出跨使用者、profile 或
/// connector generation 洩漏。
/// </para>
/// </summary>
public sealed class DownloadIntegrateDataOperationServiceIntegrationTests
{
    /// <summary>
    /// 驗證純唯讀報表輸出 DTO 的建構不會初始化 legacy ToolUtility 上傳鏈。
    ///
    /// <para>
    /// 此回歸測試保護讀取與寫入責任分離：下載流程尚未要求 CRM mutation 時，不得因 DTO 建構
    /// 就建立會取得 shared Factory service 的 UploadIntegrateData。修正前，在未設定 Factory 的
    /// 測試 host 會直接失敗，證明隱性依賴確實存在。
    /// </para>
    /// </summary>
    [Fact]
    public void ListSmallGroupWeeklyReport_WhenCreatedForReadOnlyOperation_DoesNotInitializeLegacyUploadConnector()
    {
        var report = new ListSmallGroupWeeklyReport();

        report.m_UploadIntegrateData.Should().BeNull(
            "純讀取 DTO 不可在建構階段取得 legacy ToolUtility；上傳 connector 必須由實際 mutation 方法延遲建立。");
    }

    /// <summary>
    /// 驗證 service-aware 入口會完成最小唯讀流程，並在 A/B 交錯時只提交各自的輸出。
    ///
    /// <para>
    /// 故障注入以兩個回傳不同名單名稱的 service 執行；決定性斷言包含輸出 marker、各 service
    /// 的獨立呼叫紀錄、零 Dispose，以及 DownloadIntegrateData instance 不保存借用 service。
    /// 此測試在修正前會因入口一律 fail-closed 而失敗，防止只建立 private helper 卻未接上
    /// 真正 operation path 的假性完成。
    /// </para>
    /// </summary>
    [Fact]
    public void SetupIntegrateData_WhenOperationServicesAreInterleaved_UsesOnlyCurrentServiceAndCommitsOnlyCompletedReport()
    {
        var subject = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(typeof(DownloadIntegrateData));
        var firstService = new OperationMarkerOrganizationService("A");
        var secondService = new OperationMarkerOrganizationService("B");
        var firstReport = CreateUninitializedReport();
        var secondReport = CreateUninitializedReport();
        var listId = Guid.NewGuid().ToString();

        subject.SetupIntegrateData(
            "account-A",
            "password-A",
            "小組長",
            new DateTime(2026, 8, 9),
            listId,
            string.Empty,
            ref firstReport,
            firstService);

        subject.SetupIntegrateData(
            "account-B",
            "password-B",
            "小組長",
            new DateTime(2026, 8, 9),
            listId,
            string.Empty,
            ref secondReport,
            secondService);

        firstReport.LoadFlag.Should().BeTrue();
        firstReport.ListEntityName.Should().Be("list-A");
        firstReport.LoginType.Should().Be("小組長",
            "operation-local header 必須只使用本次明確傳入的登入型態，不可讀取 session 可重用的 instance state");
        secondReport.LoadFlag.Should().BeTrue();
        secondReport.ListEntityName.Should().Be("list-B");
        secondReport.LoginType.Should().Be("小組長",
            "第二個操作也不得繼承第一個操作或舊 session 的登入型態");
        firstService.ShouldNotContainForeignMarker("B");
        secondService.ShouldNotContainForeignMarker("A");
        firstService.DisposeCount.Should().Be(0);
        secondService.DisposeCount.Should().Be(0);
        AssertDoesNotRetainBorrowedService(subject, firstService);
        AssertDoesNotRetainBorrowedService(subject, secondService);
    }

    /// <summary>
    /// 驗證 public entry 在遠端讀取失敗時不覆寫呼叫端已持有的完整報表。
    ///
    /// <para>
    /// 此案例代表 timeout、傳輸中斷或 SDK fault：內層不得回退 ToolUtility、不得以半成品取代
    /// 舊資料、也不得處置 caller-owned service。例外必須回到外層 lease owner，以便淘汰不確定
    /// transport，而不是讓下一個 request 重用它。
    /// </para>
    /// </summary>
    [Fact]
    public void SetupIntegrateData_WhenCurrentServiceFaults_PreservesPriorOutputAndDoesNotReuseOrDisposeTheFaultedService()
    {
        var subject = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(typeof(DownloadIntegrateData));
        var faultedService = new OperationMarkerOrganizationService("fault", failOnFirstCall: true);
        var priorReport = CreateUninitializedReport();
        priorReport.ListEntityName = "prior-complete-report";
        priorReport.LoadFlag = true;
        var expectedReference = priorReport;

        Action action = () => subject.SetupIntegrateData(
            "account",
            "password",
            "小組長",
            new DateTime(2026, 8, 9),
            Guid.NewGuid().ToString(),
            string.Empty,
            ref priorReport,
            faultedService);

        action.Should().Throw<InvalidOperationException>();
        priorReport.Should().BeSameAs(expectedReference);
        priorReport.ListEntityName.Should().Be("prior-complete-report");
        faultedService.DisposeCount.Should().Be(0);
        AssertDoesNotRetainBorrowedService(subject, faultedService);
    }

    /// <summary>
    /// 驗證未完成隔離驗證的登入型態，必須在任何 operation-local CRM 呼叫之前遭到拒絕。
    ///
    /// <para>
    /// 此案例以會在第一個 CRM API 呼叫時失敗的 fake service 表示未授權的登入型態。
    /// 決定性斷言是 <see cref="IOrganizationService"/> 零呼叫、零釋放及輸出 reference
    /// 不變；因此未來擴充新的登入型態時，不能先讀取登入者、名單或週報後才回退到
    /// legacy ToolUtility。借用 service 的 lease 仍完全由上層 owner 管理。
    /// </para>
    /// </summary>
    [Fact]
    public void SetupIntegrateData_WhenLoginTypeIsNotOperationLocalLeader_RejectsBeforeAnyCrmIo()
    {
        var subject = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(typeof(DownloadIntegrateData));
        var rejectedService = new OperationMarkerOrganizationService("個人回報");
        var priorReport = CreateUninitializedReport();
        priorReport.ListEntityName = "prior-complete-report";
        var expectedReference = priorReport;

        Action action = () => subject.SetupIntegrateData(
            "小組長",
            "password",
            "個人回報",
            new DateTime(2026, 8, 9),
            Guid.NewGuid().ToString(),
            string.Empty,
            ref priorReport,
            rejectedService);

        action.Should().Throw<InvalidOperationException>();
        rejectedService.CallCount.Should().Be(0);
        rejectedService.DisposeCount.Should().Be(0);
        priorReport.Should().BeSameAs(expectedReference);
        priorReport.ListEntityName.Should().Be("prior-complete-report");
        AssertDoesNotRetainBorrowedService(subject, rejectedService);
    }

    /// <summary>
    /// 驗證小組長登入成功但不屬於指定名單時，下載入口會拒絕讀取該名單的成員、週報或圖表。
    ///
    /// <para>
    /// 此測試以拒絕名單－小組長關係的 fake service 模擬呼叫端竄改 list ID。決定性斷言是
    /// 操作只停留在已驗證聯絡人與精確關係查詢，既有輸出 reference 保持不變，且借用 service
    /// 未被 Dispose 或保存。這保護的不是 client 傳入 list ID，而是伺服器從登入聯絡人與 CRM
    /// 關係投影出的授權邊界。
    /// </para>
    /// </summary>
    [Fact]
    public void SetupIntegrateData_WhenLeaderDoesNotOwnRequestedList_RejectsBeforeMemberOrWeeklyReportRead()
    {
        var subject = (DownloadIntegrateData)RuntimeHelpers.GetUninitializedObject(typeof(DownloadIntegrateData));
        var unauthorizedService = new OperationMarkerOrganizationService("unauthorized", allowRequestedList: false);
        var priorReport = CreateUninitializedReport();
        priorReport.ListEntityName = "prior-complete-report";
        var expectedReference = priorReport;

        Action action = () => subject.SetupIntegrateData(
            "account-unauthorized",
            "password-unauthorized",
            "小組長",
            new DateTime(2026, 8, 9),
            Guid.NewGuid().ToString(),
            string.Empty,
            ref priorReport,
            unauthorizedService);

        action.Should().Throw<InvalidOperationException>();
        unauthorizedService.MemberOrWeeklyReportReadCount.Should().Be(0);
        unauthorizedService.DisposeCount.Should().Be(0);
        priorReport.Should().BeSameAs(expectedReference);
        priorReport.ListEntityName.Should().Be("prior-complete-report");
        AssertDoesNotRetainBorrowedService(subject, unauthorizedService);
    }

    /// <summary>
    /// 以反射檢查 DownloadIntegrateData 的 instance/static 欄位不引用借用 service。
    /// </summary>
    private static void AssertDoesNotRetainBorrowedService(
        DownloadIntegrateData subject,
        IOrganizationService borrowedService)
    {
        var retainedField = subject.GetType()
            .GetFields(System.Reflection.BindingFlags.Instance |
                       System.Reflection.BindingFlags.Static |
                       System.Reflection.BindingFlags.Public |
                       System.Reflection.BindingFlags.NonPublic)
            .FirstOrDefault(field => ReferenceEquals(field.GetValue(subject), borrowedService));

        retainedField.Should().BeNull("借用 service 的唯一 owner 是外層 lease；任何長生命週期欄位保存它都會造成跨操作洩漏。");
    }

    /// <summary>
    /// 建立不執行 legacy UploadIntegrateData 建構鏈的純輸出 DTO。
    ///
    /// <para>
    /// 被測 operation-local 讀取流程只需要報表輸出欄位；正式建構式目前會建立另一個 legacy
    /// ToolUtility 相依物件，這正是本測試要排除的隱性跨 service 邊界。以未初始化 DTO 驗證
    /// 可確定所有 CRM I/O 都來自本次傳入 service，而非測試環境中的 Factory。
    /// </para>
    /// </summary>
    private static ListSmallGroupWeeklyReport CreateUninitializedReport()
    {
        var report = (ListSmallGroupWeeklyReport)RuntimeHelpers.GetUninitializedObject(typeof(ListSmallGroupWeeklyReport));
        report.m_SmallGroupDataList = new SmallGroupDataList();
        report.GroupArray = new List<string>();
        report.m_WeeklyReportChart = new ChartDataList { m_ChartDataList = new List<ChartData>() };
        return report;
    }

    /// <summary>
    /// 具備 A/B marker 的最低限度 CRM fake，僅實作本測試唯讀流程所允許的 Retrieve 與 RetrieveMultiple。
    /// </summary>
    private sealed class OperationMarkerOrganizationService : IOrganizationService, IDisposable
    {
        private readonly bool _failOnFirstCall;
        private readonly bool _allowRequestedList;
        private readonly Guid _loginContactId = Guid.NewGuid();
        private readonly List<string> _observedMarkers = new();
        private int _callCount;
        private int _memberOrWeeklyReportReadCount;

        /// <summary>
        /// 建立帶有固定 marker 的 operation-local fake service。
        /// </summary>
        public OperationMarkerOrganizationService(
            string marker,
            bool failOnFirstCall = false,
            bool allowRequestedList = true)
        {
            Marker = marker;
            _failOnFirstCall = failOnFirstCall;
            _allowRequestedList = allowRequestedList;
        }

        /// <summary>取得此 fake 所代表的隔離主體 marker。</summary>
        public string Marker { get; }

        /// <summary>取得不應由內層呼叫的 Dispose 次數。</summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// 取得 fake service 實際收到的 CRM 呼叫數，供 fail-closed 順序斷言使用。
        /// </summary>
        public int CallCount => _callCount;

        /// <summary>
        /// 取得關係驗證後的名單成員或週報讀取次數；未授權名單必須維持零。
        /// </summary>
        public int MemberOrWeeklyReportReadCount => _memberOrWeeklyReportReadCount;

        /// <summary>驗證此 service 未被拿去處理另一個 marker 的資料。</summary>
        public void ShouldNotContainForeignMarker(string foreignMarker)
            => _observedMarkers.Should().NotContain(foreignMarker);

        /// <inheritdoc />
        public Guid Create(Entity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Update(Entity entity) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        /// <inheritdoc />
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();

        /// <summary>回傳固定 marker 的名單或週報 entity，供 operation path 建立輸出。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ThrowIfFaulted();
            _observedMarkers.Add(Marker);

            if (string.Equals(entityName, "list", StringComparison.Ordinal))
            {
                return new Entity("list", id)
                {
                    ["listname"] = $"list-{Marker}"
                };
            }

            return new Entity(entityName, id);
        }

        /// <summary>回傳登入 contact、空成員集合與空圖表集合，避免測試未允許的 CRM I/O。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ThrowIfFaulted();
            _observedMarkers.Add(Marker);

            if (query is QueryByAttribute queryByAttribute &&
                string.Equals(queryByAttribute.EntityName, "contact", StringComparison.Ordinal))
            {
                return new EntityCollection(new[]
                {
                    new Entity("contact", _loginContactId)
                    {
                        ["fullname"] = $"contact-{Marker}",
                        // service-aware login 會在記憶體中驗證既有帳密欄位；fake 必須
                        // 提供與本測試輸入相同的值，才能讓後續 assertion 驗證真正的
                        // operation-local 資料流，而不是測到未登入時的安全早退。
                        ["new_app_pass"] = _allowRequestedList
                            ? $"password-{Marker}"
                            : "password-unauthorized"
                    }
                });
            }

            if (query is QueryExpression queryExpression &&
                string.Equals(queryExpression.EntityName, "list", StringComparison.Ordinal))
            {
                if (!_allowRequestedList)
                {
                    return new EntityCollection();
                }

                var requestedListId = ExtractConditionGuid(queryExpression, "listid");
                var requestedLeaderId = ExtractConditionGuid(
                    queryExpression,
                    "new_contact_family_leader_list");
                return requestedLeaderId == _loginContactId
                    ? new EntityCollection(new[]
                    {
                        new Entity("list", requestedListId)
                        {
                            ["listname"] = $"list-{Marker}",
                            ["new_contact_family_leader_list"] = new EntityReference("contact", _loginContactId)
                        }
                    })
                    : new EntityCollection();
            }

            if (query is QueryExpression memberOrWeeklyReportQuery &&
                (string.Equals(memberOrWeeklyReportQuery.EntityName, "listmember", StringComparison.Ordinal) ||
                 string.Equals(memberOrWeeklyReportQuery.EntityName, "new_group_present_weekly_report", StringComparison.Ordinal)))
            {
                _memberOrWeeklyReportReadCount++;
            }

            return new EntityCollection();
        }

        /// <summary>
        /// 從 service-aware 名單授權查詢取出指定的唯一 GUID 條件。
        /// fake 僅接受精確條件，避免測試因未驗證或擴大查詢而誤判為成功。
        /// </summary>
        private static Guid ExtractConditionGuid(QueryExpression query, string attributeName)
        {
            var condition = query.Criteria.Conditions.SingleOrDefault(candidate =>
                string.Equals(candidate.AttributeName, attributeName, StringComparison.Ordinal));
            condition.Should().NotBeNull();
            condition!.Values.Should().ContainSingle();
            return (Guid)condition.Values.Single();
        }

        /// <inheritdoc />
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        /// <inheritdoc />
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        /// <summary>記錄不應由 production inner layer 呼叫的 Dispose。</summary>
        public void Dispose() => DisposeCount++;

        /// <summary>在第一筆 SDK 呼叫注入 fault，模擬不可重用的傳輸狀態。</summary>
        private void ThrowIfFaulted()
        {
            _callCount++;
            if (_failOnFirstCall && _callCount == 1)
            {
                throw new InvalidOperationException("synthetic-operation-service-fault");
            }
        }
    }
}
