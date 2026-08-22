// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs
// 測試責任：驗證背景上傳使用獨佔深拷貝，不會與前景 Session 快取物件圖共享可變集合或 Member 實例。
// 保護契約：背景清理只能修改快照；原始三組名單在高頻列舉時必須保持完整且不可擲出集合競態例外。
// 資源生命週期：測試僅使用記憶體內模型與受控執行緒同步原語，不建立 CRM、網路、Session 或長生命週期背景資源。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using ChurchReport.Models;
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
