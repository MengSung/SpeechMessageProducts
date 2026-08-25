// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs
// 測試責任：驗證背景上傳使用獨佔深拷貝，不會與前景 Session 快取物件圖共享可變集合或 Member 實例。
// 保護契約：背景清理只能修改快照；原始三組名單在高頻列舉時必須保持完整且不可擲出集合競態例外。
// 資源生命週期：測試僅使用記憶體內模型與受控執行緒同步原語，不建立 CRM、網路、Session 或長生命週期背景資源。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;
using ToolUtilityNameSpace.Factory;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Models;

/// <summary>
/// 驗證 SmallGroupDataList 與 ListSmallGroupWeeklyReport 的背景快照隔離契約。
/// </summary>
[Collection("LegacyToolUtilityFactory")]
public sealed class SmallGroupDataListSnapshotIsolationTests
{
    /// <summary>
    /// 保護三組清單及其中成員均屬背景副本所有的契約。
    ///
    /// 此案例建立含相同語意資料的前景週報後製作快照，並以參考相異與值相同的斷言，
    /// 偵測任何把原始集合或 Member 實例帶入背景工作的回歸。
    /// </summary>
    [Fact]
    public void CreateBackgroundUploadCopy_DeepCopiesAllMemberCollectionsAndRequiredUploadMetadata()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var source = CreateReportWithMembers(3);

        var copy = source.CreateBackgroundUploadCopy();

        copy.Should().NotBeSameAs(source);
        copy.m_SmallGroupDataList.Should().NotBeSameAs(source.m_SmallGroupDataList);
        copy.m_SmallGroupDataList.m_SmallGroupData.Members.Should().NotBeSameAs(source.m_SmallGroupDataList.m_SmallGroupData.Members);
        copy.m_SmallGroupDataList.m_NewPersonFollowUpData.Members.Should().NotBeSameAs(source.m_SmallGroupDataList.m_NewPersonFollowUpData.Members);
        copy.m_SmallGroupDataList.m_AllMemeberData.Members.Should().NotBeSameAs(source.m_SmallGroupDataList.m_AllMemeberData.Members);
        copy.m_UploadIntegrateData.Should().NotBeSameAs(source.m_UploadIntegrateData);

        foreach (var pair in MemberPairs(source, copy))
        {
            pair.Copy.Should().NotBeSameAs(pair.Source);
            pair.Copy.Should().BeEquivalentTo(pair.Source);
        }

        source.ListEntityId = "group-id";
        source.GroupType = "幸福小組";
        source.SundayPrayers = new DateTime(2026, 8, 22);
        source.WeeklyReportData = "上傳日誌";
        source.WeeklyReportAnalysis = "上傳分析";

        copy = source.CreateBackgroundUploadCopy();

        copy.ListEntityId.Should().Be(source.ListEntityId);
        copy.GroupType.Should().Be(source.GroupType);
        copy.SundayPrayers.Should().Be(source.SundayPrayers);
        copy.WeeklyReportData.Should().Be(source.WeeklyReportData);
        copy.WeeklyReportAnalysis.Should().Be(source.WeeklyReportAnalysis);

        source.GroupArray.Add("只屬於前景選單");
        source.m_PersonalReportViewModel.FullName = "只屬於前景表單";

        copy = source.CreateBackgroundUploadCopy();

        copy.GroupArray.Should().BeEmpty();
        copy.m_PersonalReportViewModel.FullName.Should().BeNull();
    }

    /// <summary>
    /// 保護來源週報圖的寫入端與 SaveIntegrate 快照必須共同持有同一同步根。
    ///
    /// 故障注入是由測試先持有來源 <see cref="SmallGroupDataList"/> 的內部同步根，再啟動會以
    /// JSON 原地改寫姓名與電話兩個欄位的前景更新。修正前 <c>UpdateMember</c> 不持有該同步根，
    /// 因而會在鎖仍被持有時完成；快照逐欄複製便可能取得不同時間點的欄位。決定性斷言是寫入在
    /// 鎖釋放前必須等待，鎖內快照保持完整舊值，釋放後的新快照保持完整新值，不能出現混合狀態。
    /// 測試只使用記憶體模型與受控同步原語，不建立 CRM、HTTP、Session 或長生命週期背景資源。
    /// </summary>
    [Fact]
    public async Task UpdateMember_WaitsForSourceGraphLock_AndSnapshotsRemainWhole()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var report = CreateReportWithMembers(1);
        var dataList = report.m_SmallGroupDataList;
        var sourceMember = dataList.m_AllMemeberData.Members[0];
        sourceMember.PresentRecordId = "present-record-1";
        sourceMember.FullName = "完整舊姓名";
        sourceMember.Phone = "完整舊電話";

        var syncRoot = GetSourceGraphSyncRoot(dataList);
        using var writerEntered = new ManualResetEventSlim(false);
        using var writerCompleted = new ManualResetEventSlim(false);
        var writer = Task.Run(() =>
        {
            writerEntered.Set();
            try
            {
                dataList.m_AllMemeberData.UpdateMember(
                    "present-record-1",
                    "{\"FullName\":\"完整新姓名\",\"Phone\":\"完整新電話\"}");
            }
            finally
            {
                writerCompleted.Set();
            }
        });

        Monitor.Enter(syncRoot);
        try
        {
            writerEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(
                "測試必須先確認前景寫入已進入受測路徑，才能判定它是否遵守來源資料圖同步根");

            writerCompleted.Wait(TimeSpan.FromMilliseconds(250)).Should().BeFalse(
                "前景寫入在來源同步根被持有時必須等待，否則快照可與 Json.NET 原地多欄位更新重疊");

            var oldSnapshot = report.CreateBackgroundUploadCopy();
            AssertWholeMember(
                oldSnapshot.m_SmallGroupDataList.m_AllMemeberData.Members[0],
                "完整舊姓名",
                "完整舊電話");
        }
        finally
        {
            Monitor.Exit(syncRoot);
        }

        await writer;

        var newSnapshot = report.CreateBackgroundUploadCopy();
        AssertWholeMember(
            newSnapshot.m_SmallGroupDataList.m_AllMemeberData.Members[0],
            "完整新姓名",
            "完整新電話");
    }

    /// <summary>
    /// 保護下載／初始化流程加入全部成員時也必須遵守來源資料圖同步根，避免快照在
    /// 成員物件加入途中觀察到不完整的集合。故障注入是先持有同步根，再啟動新增工作；
    /// 決定性斷言是工作在鎖內等待，釋放後才完成，且快照只看到完整加入前或完整加入後狀態。
    /// </summary>
    [Fact]
    public async Task AddMemberToAllMemberData_WaitsForSourceGraphLock()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var report = CreateReportWithMembers(1);
        var dataList = report.m_SmallGroupDataList;
        var syncRoot = GetSourceGraphSyncRoot(dataList);
        var addedMember = new Member { PresentRecordId = "added", FullName = "新加入成員" };
        using var completed = new ManualResetEventSlim(false);

        Monitor.Enter(syncRoot);
        try
        {
            var task = Task.Run(() =>
            {
                dataList.AddMemberToAllMemberData(addedMember);
                completed.Set();
            });

            completed.Wait(TimeSpan.FromMilliseconds(250)).Should().BeFalse(
                "全部成員加入必須與快照共用同步根，不能在來源鎖持有期間完成");
            report.CreateBackgroundUploadCopy().m_SmallGroupDataList.m_AllMemeberData.Members
                .Should().HaveCount(1);

            Monitor.Exit(syncRoot);
            await task;
        }
        finally
        {
            if (Monitor.IsEntered(syncRoot))
            {
                Monitor.Exit(syncRoot);
            }
        }

        report.CreateBackgroundUploadCopy().m_SmallGroupDataList.m_AllMemeberData.Members
            .Should().ContainSingle(member => member.PresentRecordId == "added");
    }

    /// <summary>
    /// 保護下載完成後的排序與狀態正規化也必須加入來源資料圖同步協定。
    ///
    /// 故障注入先持有同一份 <see cref="SmallGroupDataList"/> 的同步根，再以反射呼叫下載流程
    /// 的私有排序／正規化步驟；修正前該步驟直接排序並逐一改寫 <see cref="Member.Status"/>，會在
    /// 鎖仍被持有時結束。決定性斷言是工作必須等待鎖釋放，且釋放後所有成員才完成正規化，防止
    /// SaveIntegrate 快照取得一部分已排序或已清理、另一部分仍舊值的資料圖。測試不建立 CRM、
    /// HTTP、Session 或背景長生命週期資源。
    /// </summary>
    [Fact]
    public async Task SortAndCleanMemberStatus_WaitsForSourceGraphLock()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var report = CreateReportWithMembers(2);
        foreach (var members in new[]
                 {
                     report.m_SmallGroupDataList.m_SmallGroupData.Members,
                     report.m_SmallGroupDataList.m_NewPersonFollowUpData.Members,
                     report.m_SmallGroupDataList.m_AllMemeberData.Members
                 })
        {
            members[0].Status = "2. 已委身";
            members[1].Status = "1. 新朋友";
        }

        var sortAndClean = typeof(DownloadIntegrateData).GetMethod(
            "SortAndCleanMemberStatus",
            BindingFlags.Static | BindingFlags.NonPublic);
        sortAndClean.Should().NotBeNull("下載完成後的狀態正規化是已發布資料圖的前景寫入端");

        var syncRoot = GetSourceGraphSyncRoot(report.m_SmallGroupDataList);
        using var completed = new ManualResetEventSlim(false);
        Task task;
        Monitor.Enter(syncRoot);
        try
        {
            task = Task.Run(() =>
            {
                try
                {
                    sortAndClean!.Invoke(null, new object[] { report });
                }
                finally
                {
                    completed.Set();
                }
            });

            completed.Wait(TimeSpan.FromMilliseconds(250)).Should().BeFalse(
                "排序與狀態正規化必須與快照共用來源資料圖同步根，不能在來源鎖持有期間完成");

            Monitor.Exit(syncRoot);
        }
        finally
        {
            if (Monitor.IsEntered(syncRoot))
            {
                Monitor.Exit(syncRoot);
            }
        }

        await task;

        report.m_SmallGroupDataList.m_AllMemeberData.Members
            .Select(member => member.Status)
            .Should().ContainInOrder("新朋友", "已委身");
    }

    /// <summary>
    /// 保護背景清理不能使前景列舉失敗或讀到半清空集合的契約。
    ///
    /// 故障注入是在背景 Task 內同時重複 Clear/Add 三組快照集合各 1,000 次；前景同步
    /// 列舉三組原始集合各 1,000 次。決定性斷言是每一次都保有五筆原始順序資料，且
    /// Task 完成時沒有 InvalidOperationException 或其他集合競態錯誤。
    /// </summary>
    [Fact]
    public async Task BackgroundMutationOfSnapshot_DoesNotBreakConcurrentEnumerationOfOriginalMembers()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var source = CreateReportWithMembers(5);
        var copy = source.CreateBackgroundUploadCopy();
        var originalSmallGroupMembers = source.m_SmallGroupDataList.m_SmallGroupData.Members;
        var originalNewPersonMembers = source.m_SmallGroupDataList.m_NewPersonFollowUpData.Members;
        var originalAllMembers = source.m_SmallGroupDataList.m_AllMemeberData.Members;
        var snapshotCollections = new[]
        {
            copy.m_SmallGroupDataList.m_SmallGroupData.Members,
            copy.m_SmallGroupDataList.m_NewPersonFollowUpData.Members,
            copy.m_SmallGroupDataList.m_AllMemeberData.Members
        };
        using var backgroundStarted = new ManualResetEventSlim(false);
        using var foregroundReady = new ManualResetEventSlim(false);

        var mutation = Task.Run(() =>
        {
            backgroundStarted.Set();
            if (!foregroundReady.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("前景列舉沒有在背景快照改寫前開始，無法完成並行隔離故障注入。");
            }

            for (var iteration = 0; iteration < 1000; iteration++)
            {
                foreach (var members in snapshotCollections)
                {
                    members.Clear();
                    members.Add(new Member { FullName = $"背景-{iteration}" });
                }
            }
        });

        backgroundStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(
            "背景工作必須已準備好，才能保證前景列舉和快照改寫處在同一個故障注入視窗");
        foregroundReady.Set();
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            AssertCompleteMembers(originalSmallGroupMembers);
            AssertCompleteMembers(originalNewPersonMembers);
            AssertCompleteMembers(originalAllMembers);
        }

        await mutation;
    }

    /// <summary>
    /// 以固定大小及順序驗證前景集合未受背景快照清理影響。
    /// </summary>
    /// <param name="members">要列舉的前景成員集合。</param>
    private static void AssertCompleteMembers(IEnumerable<Member> members)
    {
        members.Should().HaveCount(5);
        members.Select(member => member.FullName)
            .Should().ContainInOrder("成員-0", "成員-1", "成員-2", "成員-3", "成員-4");
    }

    /// <summary>
    /// 驗證快照中的兩個可同時被 JSON 更新的欄位屬於同一個完整版本；若姓名與電話分屬舊、新
    /// 版本，代表同步邊界失效，背景上傳可能取得不曾存在於任何前景時間點的混合資料。
    /// </summary>
    private static void AssertWholeMember(Member member, string fullName, string phone)
    {
        member.FullName.Should().Be(fullName);
        member.Phone.Should().Be(phone);
    }

    /// <summary>
    /// 取得來源資料圖唯一的內部同步根，讓測試能以 lock ownership 而非不可靠的高頻迴圈，
    /// 決定性驗證所有前景寫入端是否真的加入與快照相同的協定。
    /// </summary>
    private static object GetSourceGraphSyncRoot(SmallGroupDataList dataList)
    {
        var syncRootProperty = typeof(SmallGroupDataList).GetProperty(
            "SyncRoot",
            BindingFlags.Instance | BindingFlags.NonPublic);
        syncRootProperty.Should().NotBeNull(
            "SmallGroupDataList 必須維持每份來源資料圖私有的同步根，避免跨使用者共用鎖");

        var syncRoot = syncRootProperty!.GetValue(dataList);
        syncRoot.Should().NotBeNull();
        return syncRoot!;
    }

    /// <summary>
    /// 保護不同來源與不同快照不會互相保留可變 Member 狀態的契約。
    ///
    /// 故障注入是改寫第一份背景副本的姓名；斷言第二份背景副本與兩個前景來源皆維持
    /// 原值，確保同步範圍只服務各自的資料圖，且不產生跨使用者污染。
    /// </summary>
    [Fact]
    public void CreatingTwoSnapshots_DoesNotCrossContaminateSources()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var first = CreateReportWithMembers(1);
        var second = CreateReportWithMembers(1);

        var firstCopy = first.CreateBackgroundUploadCopy();
        var secondCopy = second.CreateBackgroundUploadCopy();

        firstCopy.m_SmallGroupDataList.m_AllMemeberData.Members[0].FullName = "只屬於第一份快照";

        firstCopy.m_SmallGroupDataList.m_AllMemeberData.Members[0].FullName.Should().Be("只屬於第一份快照");
        secondCopy.m_SmallGroupDataList.m_AllMemeberData.Members[0].FullName.Should().Be("成員-0");
        first.m_SmallGroupDataList.m_AllMemeberData.Members[0].FullName.Should().Be("成員-0");
        second.m_SmallGroupDataList.m_AllMemeberData.Members[0].FullName.Should().Be("成員-0");
    }

    /// <summary>
    /// 建立三組成員數一致的前景週報，讓測試可判定快照是否完整保留原始集合。
    /// </summary>
    /// <param name="count">每一組要建立的成員數。</param>
    /// <returns>只由目前測試持有的週報物件圖。</returns>
    private static ListSmallGroupWeeklyReport CreateReportWithMembers(int count)
    {
        var report = new ListSmallGroupWeeklyReport();
        report.m_SmallGroupDataList.m_SmallGroupData.Members = CreateMembers(count);
        report.m_SmallGroupDataList.m_NewPersonFollowUpData.Members = CreateMembers(count);
        report.m_SmallGroupDataList.m_AllMemeberData.Members = CreateMembers(count);
        return report;
    }

    /// <summary>
    /// 產生具有可辨識順序與可變欄位的測試成員，供深拷貝及競態斷言使用。
    /// </summary>
    /// <param name="count">要建立的成員數。</param>
    /// <returns>每次呼叫皆為新的 List 與 Member 實例。</returns>
    private static List<Member> CreateMembers(int count) => Enumerable.Range(0, count)
        .Select(index => new Member
        {
            Id = index,
            FullName = $"成員-{index}",
            Group = "測試小組",
            ModifyFlag = true,
            PrayItem = "原始祈禱事項"
        })
        .ToList();

    /// <summary>
    /// 將三組來源與副本的第一位成員配對，以明確驗證每個集合都沒有共享 Member 實例。
    /// </summary>
    /// <param name="source">原始前景週報。</param>
    /// <param name="copy">背景副本週報。</param>
    /// <returns>三組對應的來源與副本成員。</returns>
    private static IEnumerable<(Member Source, Member Copy)> MemberPairs(
        ListSmallGroupWeeklyReport source,
        ListSmallGroupWeeklyReport copy)
    {
        yield return (source.m_SmallGroupDataList.m_SmallGroupData.Members[0], copy.m_SmallGroupDataList.m_SmallGroupData.Members[0]);
        yield return (source.m_SmallGroupDataList.m_NewPersonFollowUpData.Members[0], copy.m_SmallGroupDataList.m_NewPersonFollowUpData.Members[0]);
        yield return (source.m_SmallGroupDataList.m_AllMemeberData.Members[0], copy.m_SmallGroupDataList.m_AllMemeberData.Members[0]);
    }

    /// <summary>
    /// 在模型建構期間提供不建立真實 CRM 連線的 ToolUtilityFactory 最小設定。
    /// </summary>
    /// <remarks>
    /// ListSmallGroupWeeklyReport 為相容性而在建構時建立 UploadIntegrateData；本 scope 重用
    /// F2 測試已驗證的無操作 tracer 與 ambient gateway，並在 Dispose 中清除每一個 static
    /// 參考，避免測試組態、服務提供者或背景 owner 遺留給下一個測試。
    /// </remarks>
    private sealed class LegacyToolUtilityFactoryScope : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly NullToolUtilityTracer _tracer;
        private bool _disposed;

        /// <summary>
        /// 設定只供模型建構使用的記憶體組態與無連線的 ambient gateway。
        /// </summary>
        public LegacyToolUtilityFactoryScope()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
                    ["CrmConnection:Username"] = "test-user",
                    ["CrmConnection:Password"] = "test-secret"
                })
                .Build();

            _provider = new ServiceCollection().BuildServiceProvider();
            _tracer = new NullToolUtilityTracer();
            ToolUtilityFactory.SetConfiguration(configuration);
            ToolUtilityFactory.SetTracer(_tracer);
            ToolUtilityFactory.SetAmbientService(new AmbientGatewayOrganizationService(
                static () => null,
                _provider.GetRequiredService<IServiceScopeFactory>()));
        }

        /// <summary>
        /// 確定性清除 Factory 的單例、組態、ambient service 與 tracer，避免 static 狀態跨測試洩漏。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ResetLegacyToolUtilityFactory();
            ClearLegacyToolUtilityFactoryStatics();
            _tracer.Dispose();
            _provider.Dispose();
        }

        /// <summary>
        /// 呼叫 Factory 的測試內部重設入口，清空可能保存舊 scope 的單例。
        /// </summary>
        private static void ResetLegacyToolUtilityFactory()
        {
            var reset = typeof(ToolUtilityFactory).GetMethod(
                "ResetInstance",
                BindingFlags.Static | BindingFlags.NonPublic);
            reset.Should().NotBeNull();
            reset!.Invoke(null, null);
        }

        /// <summary>
        /// 將測試注入的 static 參考歸零，確保已釋放的服務提供者不會被後續流程重新使用。
        /// </summary>
        private static void ClearLegacyToolUtilityFactoryStatics()
        {
            foreach (var fieldName in new[] { "_configuration", "_ambientService", "_tracer" })
            {
                var field = typeof(ToolUtilityFactory).GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                field.Should().NotBeNull();
                field!.SetValue(null, null);
            }
        }
    }
}
