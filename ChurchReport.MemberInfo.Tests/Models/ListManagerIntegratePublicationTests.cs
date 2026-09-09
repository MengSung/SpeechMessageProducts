// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/Models/ListManagerIntegratePublicationTests.cs
// 檔案責任：驗證小組整合資料在同一 Session holder 內的 single-flight、完整發布、
//           exact row-key 防重與 detached read 隔離契約。
// 測試策略：以可控制的區域 loader 取代 CRM I/O，透過 barrier、故障注入與快照改寫，
//           精確證明競態修正；測試不建立 HttpContext、Session、CRM client、Timer 或背景工作。
// 編碼要求：UTF-8 without BOM、CRLF，並以 final CRLF 結尾。
// ============================================================================
using ChurchReport.Models;
using FluentAssertions;
using System.Collections.Concurrent;
using System.Threading;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using System.Reflection;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Dataverse;

namespace ChurchReport.MemberInfo.Tests.Models;

/// <summary>
/// 保護 <see cref="ListManager"/> 的整合資料發布邊界，避免同一 Session 的 AJAX 併發請求
/// 同時改寫共享週報，或讓序列化端看見半完成、跨小組及重複 row-key 資料。
/// </summary>
/// <remarks>
/// 每個測試建立自己的 manager、loader 與同步原語，測試結束後不留下 static collection、
/// wait handle、取消註冊或長壽命工作，因此不會把某個測試的使用者資料保留到下一個測試。
/// </remarks>
[Collection("LegacyToolUtilityFactory")]
public sealed class ListManagerIntegratePublicationTests
{
    /// <summary>
    /// 驗證同一載入鍵的 32 個並行讀取只執行一次 loader，且所有呼叫只取得完成後的獨立快照。
    /// 故障注入以 barrier 暫停唯一 loader，決勝斷言為 invocation count 等於一、每份快照內容完整，
    /// 並且任兩份回傳集合不共享可變參考。
    /// </summary>
    [Fact]
    public async Task EnsureAndGetIntegrateDetachedRead_ConcurrentSameKey_LoadsOnceAndReturnsIsolatedSnapshots()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        using var loaderEntered = new ManualResetEventSlim(false);
        using var releaseLoader = new ManualResetEventSlim(false);
        var invocationCount = 0;
        var manager = CreateManager(_ =>
        {
            Interlocked.Increment(ref invocationCount);
            loaderEntered.Set();
            releaseLoader.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            return CreateReport("list-a", ("row-1", "王小明"), ("row-2", "王小明"));
        });

        var reads = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => manager.EnsureAndGetIntegrateDetachedRead("list-a")))
            .ToArray();

        loaderEntered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        releaseLoader.Set();
        var snapshots = await Task.WhenAll(reads);

        invocationCount.Should().Be(1);
        snapshots.Should().OnlyContain(snapshot =>
            snapshot.LoadFlag &&
            snapshot.ListEntityId == "list-a" &&
            snapshot.m_SmallGroupDataList.m_SmallGroupData.Members.Count == 2);
        snapshots.SelectMany(snapshot => snapshot.m_SmallGroupDataList.m_SmallGroupData.Members)
            .Select(member => member.FullName)
            .Should().OnlyContain(name => name == "王小明", "同名但 row key 不同的兩位會友都必須保留");
        snapshots[0].m_SmallGroupDataList.Should().NotBeSameAs(snapshots[1].m_SmallGroupDataList);
    }

    /// <summary>
    /// 驗證 loader 產生相同非空 PresentRecordId 時會拒絕發布，且既有完整快照不被污染。
    /// 故障注入是第二次載入回傳重複 key；決勝斷言為擲出明確例外，之後原小組仍可讀到原資料。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_DuplicateStableRowKey_DoesNotPublishCandidate()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var reports = new ConcurrentQueue<ListSmallGroupWeeklyReport>(new[]
        {
            CreateReport("list-a", ("row-1", "原始會友")),
            CreateReport("list-b", ("duplicate", "第一筆"), ("duplicate", "第二筆"))
        });
        var manager = CreateManager(_ => reports.TryDequeue(out var report) ? report : throw new InvalidOperationException());

        manager.EnsureAndGetIntegrateDetachedRead("list-a");
        var action = () => manager.EnsureAndGetIntegrateDetachedRead("list-b");

        action.Should().Throw<InvalidOperationException>().WithMessage("*PresentRecordId*duplicate*");
        manager.EnsureAndGetIntegrateDetachedRead("list-a")
            .m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.FullName == "原始會友");
    }

    /// <summary>
    /// 驗證候選成功發布後，即使 legacy 寫入路徑把相同 PresentRecordId 再次放入活的 Session
    /// 物件圖，下一次快取命中也必須重新驗證實際交付集合並 fail closed。故障注入先完成一次
    /// 正常發布，再直接模擬舊 CRUD 的 append；決勝斷言為 loader 不會重跑、讀取會擲出重複
    /// ID 例外，而且 guard 不會用姓名或內容選擇其中一列來掩蓋衝突。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_CacheHitAfterDuplicateWrite_RejectsConflictingSnapshot()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var invocationCount = 0;
        var manager = CreateManager(_ =>
        {
            Interlocked.Increment(ref invocationCount);
            return CreateReport("list-a", ("record-a", "原始列"));
        });

        manager.EnsureAndGetIntegrateDetachedRead("list-a");

        // 這一筆只模擬已知 legacy 寫入缺口，不代表允許正式程式直接修改公開欄位。
        // 測試刻意使用相同資料庫 ID、不同顯示內容，確保判定依據只有 PresentRecordId。
        manager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members.Add(
            new Member { PresentRecordId = "record-a", FullName = "意外重複列" });

        var action = () => manager.EnsureAndGetIntegrateDetachedRead("list-a");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*PresentRecordId*record-a*");
        invocationCount.Should().Be(1, "相同 scope 的 cache hit 不應為了驗證輸出而重新執行 CRM loader");
    }

    /// <summary>
    /// 驗證呼叫端改寫 detached snapshot 不會反向污染 Session holder 中已發布的資料。
    /// 故障注入直接清空第一份回傳集合；決勝斷言為第二次讀取仍保有原始 row 與名稱。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_CallerMutatesResult_PublishedSnapshotRemainsUnchanged()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var manager = CreateManager(_ => CreateReport("list-a", ("row-1", "隔離會友")));
        var first = manager.EnsureAndGetIntegrateDetachedRead("list-a");

        first.m_SmallGroupDataList.m_SmallGroupData.Members.Clear();
        var second = manager.EnsureAndGetIntegrateDetachedRead("list-a");

        second.m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.PresentRecordId == "row-1" && member.FullName == "隔離會友");
    }

    /// <summary>
    /// 驗證同一小組切換日期後不會只因 ListEntityId 相同而誤用舊快照。
    /// 故障注入是在第一次發布後改變 SelectDate；決勝斷言為 loader 再執行一次並回傳新世代名稱。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_DateChanges_RebuildsCompleteScope()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var invocationCount = 0;
        var manager = CreateManager(_ =>
        {
            var generation = Interlocked.Increment(ref invocationCount);
            return CreateReport("list-a", ($"row-{generation}", $"世代-{generation}"));
        });

        manager.EnsureAndGetIntegrateDetachedRead("list-a");
        manager.m_SelectDate = manager.m_SelectDate.AddDays(7);
        var second = manager.EnsureAndGetIntegrateDetachedRead("list-a");

        invocationCount.Should().Be(2);
        second.m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.FullName == "世代-2");
    }

    /// <summary>
    /// 驗證同一帳號更新 credential 後，完整隔離鍵一定失效並建立新的候選快照。
    /// 故障注入是在第一次發布後替換 holder 的密碼；決勝斷言為 loader 再執行一次，
    /// 且第二次讀取只包含新 credential 世代的資料，禁止沿用舊登入狀態或舊授權結果。
    /// 測試不讀取或輸出 fingerprint，也不把測試 credential 保存到 static 狀態。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_CredentialChanges_RebuildsCompleteScope()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var invocationCount = 0;
        var manager = CreateManager(_ =>
        {
            var generation = Interlocked.Increment(ref invocationCount);
            return CreateReport("list-a", ($"credential-row-{generation}", $"憑證世代-{generation}"));
        });

        manager.EnsureAndGetIntegrateDetachedRead("list-a");
        manager.m_Password = "credential-b";
        var second = manager.EnsureAndGetIntegrateDetachedRead("list-a");

        invocationCount.Should().Be(2, "credential 改變必須讓舊快照鍵失效");
        second.m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.FullName == "憑證世代-2");
    }

    /// <summary>
    /// 驗證兩個 Session 各自建立的 ListManager 不會共用 snapshot、gate、credential 或 Member。
    /// 故障注入讓 A/B 使用相同 list id 但不同帳號及資料；決勝斷言為兩邊只看見自己的姓名，
    /// 且改寫 A 的 detached result 不影響 A 的下一次讀取或 B 的任何讀取。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_DifferentSessionOwners_DoNotShareMutableState()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var managerA = CreateManager(_ => CreateReport("list-a", ("record-a", "使用者 A")));
        var managerB = CreateManager(_ => CreateReport("list-a", ("record-b", "使用者 B")));
        managerB.m_Account = "account-b";
        managerB.m_Password = "credential-b";

        var readA = managerA.EnsureAndGetIntegrateDetachedRead("list-a");
        var readB = managerB.EnsureAndGetIntegrateDetachedRead("list-a");
        readA.m_SmallGroupDataList.m_SmallGroupData.Members[0].FullName = "只改 detached A";

        managerA.EnsureAndGetIntegrateDetachedRead("list-a")
            .m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.FullName == "使用者 A");
        readB.m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.FullName == "使用者 B");
        managerA.m_ListSmallGroupWeeklyReport.Should().NotBeSameAs(managerB.m_ListSmallGroupWeeklyReport);
    }

    /// <summary>
    /// 驗證 CRM loader 的暫時性例外不會發布半成品或永久卡住 holder，下一次呼叫可以重試成功。
    /// 故障注入為第一次 loader 主動擲出 timeout；決勝斷言為第二次 loader 被呼叫且成功發布完整資料。
    /// </summary>
    [Fact]
    public void EnsureAndGetIntegrateDetachedRead_LoaderFails_RetrySucceedsWithoutStickyState()
    {
        using var factoryScope = new LegacyToolUtilityFactoryScope();
        var invocationCount = 0;
        var manager = CreateManager(_ =>
        {
            if (Interlocked.Increment(ref invocationCount) == 1)
            {
                throw new TimeoutException("模擬 CRM timeout");
            }

            return CreateReport("list-a", ("row-ok", "重試成功"));
        });

        var first = () => manager.EnsureAndGetIntegrateDetachedRead("list-a");
        first.Should().Throw<TimeoutException>();
        var recovered = manager.EnsureAndGetIntegrateDetachedRead("list-a");

        invocationCount.Should().Be(2);
        recovered.m_SmallGroupDataList.m_SmallGroupData.Members.Should()
            .ContainSingle(member => member.PresentRecordId == "row-ok");
    }

    /// <summary>
    /// 建立只供本測試擁有的 ListManager，並配置兩個可見小組以驗證跨 key 發布行為。
    /// loader 不得保存 manager、Session 或測試同步原語到方法生命週期之外。
    /// </summary>
    /// <param name="loader">以 list id 建立候選週報的同步測試 loader。</param>
    /// <returns>具有固定登入 scope 與可見小組的 manager。</returns>
    private static ListManager CreateManager(Func<string, ListSmallGroupWeeklyReport> loader)
    {
        var manager = new ListManager(loader)
        {
            m_Account = "account-a",
            m_Password = "credential-a",
            LoginType = "小組長",
            m_SelectDate = new DateTime(2026, 9, 8),
            m_MultiGroupList = new MultiGroupList
            {
                m_WeeklyReportRecordListData = new List<WeeklyReportRecord>
                {
                    new() { ListEntityId = "list-a", WeeklyReportEntityId = "weekly-a" },
                    new() { ListEntityId = "list-b", WeeklyReportEntityId = "weekly-b" }
                }
            }
        };

        return manager;
    }

    /// <summary>
    /// 建立內容完整、LoadFlag 已完成且所有 Member 均由此候選週報獨占的測試資料。
    /// </summary>
    /// <param name="listId">候選週報所屬可見小組。</param>
    /// <param name="members">手工推導的 row key 與姓名，不重用正式驗證器產生期望值。</param>
    /// <returns>可供發布驗證的候選週報。</returns>
    private static ListSmallGroupWeeklyReport CreateReport(
        string listId,
        params (string PresentRecordId, string FullName)[] members)
    {
        return new ListSmallGroupWeeklyReport
        {
            LoadFlag = true,
            ListEntityId = listId,
            m_SmallGroupDataList = new SmallGroupDataList
            {
                m_SmallGroupData = new SmallGroupData
                {
                    Members = members.Select(member => new Member
                    {
                        PresentRecordId = member.PresentRecordId,
                        FullName = member.FullName
                    }).ToList()
                }
            },
            m_WeeklyReportChart = new ChartDataList { m_ChartDataList = new List<ChartData>() }
        };
    }

    /// <summary>
    /// 為相容模型建構子的舊 ToolUtilityFactory 提供無操作、無連線的測試組態。
    /// Scope 結束時清除 Factory 的 static configuration、ambient service 與 tracer，
    /// 避免測試 credential、ServiceProvider 或 CRM client 參考滲入下一個 Session 測試。
    /// </summary>
    private sealed class LegacyToolUtilityFactoryScope : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IDisposable tracer;
        private bool disposed;

        /// <summary>
        /// 建立僅用於模型建構的記憶體組態；不連線正式 CRM，也不保存 request state。
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
            provider = new ServiceCollection().BuildServiceProvider();
            tracer = new ToolUtilityNameSpace.Diagnostics.NullToolUtilityTracer();
            ToolUtilityFactory.SetConfiguration(configuration);
            ToolUtilityFactory.SetTracer((ToolUtilityNameSpace.Diagnostics.IToolUtilityTracer)tracer);
            ToolUtilityFactory.SetAmbientService(new AmbientGatewayOrganizationService(
                static () => null,
                provider.GetRequiredService<IServiceScopeFactory>()));
        }

        /// <summary>
        /// 確定性釋放測試 tracer／DI provider，並以既有 private reset 入口清空程序級 Factory 狀態。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            typeof(ToolUtilityFactory).GetMethod("ResetInstance", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);
            foreach (var fieldName in new[] { "_configuration", "_ambientService", "_tracer" })
            {
                typeof(ToolUtilityFactory).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
                    ?.SetValue(null, null);
            }
            tracer.Dispose();
            provider.Dispose();
        }
    }

}
