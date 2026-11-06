/*
 * 檔案整體說明
 *
 * 目的與定位：八入口獨立故障矩陣驗正式ID／根表／整頁預算，所有資料是合成值；不把Mock通過當實機整合。 另驗結構名與GUID在I/O前拒絕、並行scope隔離、Dispose/drain與弱引用，不建立無主背景資源。
 *
 * 主要功能與流程：
 * 本檔宣告 PresentQuerySafetyTests。
 * - 八個實作和契約必須實際來自雙目標Core；不以同名host複本通過組件驗證。
 * - 獨立列出每入口／故障組合，不能讓第一個失敗掩蓋後面入口；固定清單不含scope資料。
 * - 5000列為單頁本地安全預算；超量先拒絕，不配置超量ID集合，也不回部分或靜默刪重。
 * - 兩種動態關聯查詢的四個metadata及父ID逐一拒絕；即使舊入口未使用部分參數也不能向SDK傳入非法結構。
 * 測試案例入口包含 ImplementationAndContract_AreInCore、InvalidPages、EveryEntrypoint_RejectsInvalidPage、
 * DynamicNames_RejectBeforeIo、EmptyIdentifier_RejectsBeforeIo、WeekRange_RejectsBeforeIo、
 * Fetch_EncodesNameAndRejectsWrongResponse、OtherParent_HasNoLessonLink；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Xml.Linq;
using Microsoft.Xrm.Sdk.Messages;
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 八入口獨立故障矩陣驗正式ID／根表／整頁預算，所有資料是合成值；不把Mock通過當實機整合。
    /// 另驗結構名與GUID在I/O前拒絕、並行scope隔離、Dispose/drain與弱引用，不建立無主背景資源。
    /// </summary>
    public sealed class PresentQuerySafetyTests
    {
        /// <summary>八個實作和契約必須實際來自雙目標Core；不以同名host複本通過組件驗證。</summary>
        [Fact]
        public void ImplementationAndContract_AreInCore()
        {
            Assert.Same(typeof(IPresentRecordQueryService).Assembly, typeof(PresentRecordQueryService).Assembly);
            Assert.Equal("ToolUtility.Core", typeof(PresentRecordQueryService).Assembly.GetName().Name);
            Assert.Equal(8, typeof(IPresentRecordQueryService).GetMethods().Length);
        }

        /// <summary>獨立列出每入口／故障組合，不能讓第一個失敗掩蓋後面入口；固定清單不含scope資料。</summary>
        public static IEnumerable<object[]> InvalidPages() => Entries.SelectMany(entry =>
            new[] { "null", "empty", "table", "duplicate", "over-budget" }.Select(mode => new object[] { entry, mode }));

        /// <summary>5000列為單頁本地安全預算；超量先拒絕，不配置超量ID集合，也不回部分或靜默刪重。</summary>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public void EveryEntrypoint_RejectsInvalidPage(string entry, string mode)
        {
            var sdk = Source(name =>
            {
                if (mode == "null") return null!;
                var row = new Entity(mode == "table" ? "account" : name,
                    mode == "empty" ? Guid.Empty : ContactCharacterizationTests.FirstId);
                return mode == "over-budget" ? new EntityCollection(Enumerable.Range(1, 5001).Select(index =>
                    new Entity(name, new Guid(index, 0, 0, new byte[8]))).ToArray())
                    : new EntityCollection(mode == "duplicate" ? new[] { row, row } : new[] { row });
            });
            Assert.Throws<InvalidOperationException>(() =>
                PresentQueryCharacterizationTests.Read(new PresentRecordQueryService(new object(), sdk.Object), entry));
        }

        /// <summary>兩種動態關聯查詢的四個metadata及父ID逐一拒絕；即使舊入口未使用部分參數也不能向SDK傳入非法結構。</summary>
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void DynamicNames_RejectBeforeIo(int index)
        {
            foreach (var entry in new[] { "sorted", "date" })
            {
                var names = new[] { "new_disciple_lessons", "new_disciple_lessonsid", "new_parent", "new_present_record" };
                if (index < 4) names[index] = "a<&\"'";
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                Assert.Throws<ArgumentException>(() => PresentQueryCharacterizationTests.Read(
                    new PresentRecordQueryService(new object(), sdk.Object), entry,
                    id: index == 4 ? "invalid" : ContactCharacterizationTests.FirstId.ToString("D"),
                    parent: names[0], idField: names[1], association: names[2], child: names[3]));
                sdk.VerifyNoOtherCalls();
            }
        }

        /// <summary>八入口空ID皆須在SDK前拒絕，不准轉為無條件查詢。</summary>
        [Theory]
        [InlineData("weeks")] [InlineData("sorted")] [InlineData("fetch")] [InlineData("in-weekly")]
        [InlineData("date")] [InlineData("weekly")] [InlineData("months")] [InlineData("list")]
        public void EmptyIdentifier_RejectsBeforeIo(string entry)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentException>(() => PresentQueryCharacterizationTests.Read(
                new PresentRecordQueryService(new object(), sdk.Object), entry, id: Guid.Empty.ToString("D")));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>週數不可為零或負數；含XML特殊字元名稱必須以值編碼，不能注入另一條件。</summary>
        [Theory]
        [InlineData("weeks", 0)] [InlineData("weeks", -1)] [InlineData("fetch", 0)] [InlineData("fetch", -1)]
        public void WeekRange_RejectsBeforeIo(string entry, int weeks)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentOutOfRangeException>(() => PresentQueryCharacterizationTests.Read(
                new PresentRecordQueryService(new object(), sdk.Object), entry, weeks: weeks));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>Execute錯型別是資料協定失敗；名稱經XElement仍保持原文字，不能藉替身預先編碼。</summary>
        [Fact]
        public void Fetch_EncodesNameAndRejectsWrongResponse()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            const string name = "a<&\"'";
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var xml = XElement.Parse(Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(input).Query).Query);
                Assert.Equal(name, (string?)xml.Descendants("condition").First().Attribute("uiname"));
                Assert.Equal(2, xml.Descendants("condition").Count());
                return new OrganizationResponse();
            });
            Assert.Throws<InvalidOperationException>(() => PresentQueryCharacterizationTests.Read(
                new PresentRecordQueryService(new object(), sdk.Object), "fetch", name: name));
        }

        /// <summary>非課程父實體不可多加課程結束日期link；caller日期保留Unspecified語義，不偷換UTC或本地。</summary>
        [Fact]
        public void OtherParent_HasNoLessonLink()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                Assert.Empty(Assert.IsType<QueryExpression>(input).LinkEntities);
                return new EntityCollection();
            });
            Assert.Empty(PresentQueryCharacterizationTests.Read(new PresentRecordQueryService(new object(), sdk.Object),
                "date", parent: "account", idField: "accountid").Entities);
        }

        /// <summary>每個入口保留真實故障同一物件；下一次可重試並只發布新完整頁，沒有快取或部分候選。</summary>
        [Fact]
        public void AllEntrypoints_PreserveFailureAndAllowExplicitRetry()
        {
            foreach (var entry in Entries)
            {
                var calls = 0;
                var failure = new TimeoutException("synthetic-private");
                var sdk = Source(name => ++calls == 1 ? throw failure
                    : new EntityCollection(new[] { new Entity(name, ContactCharacterizationTests.FirstId) }));
                var service = new PresentRecordQueryService(new object(), sdk.Object);
                Assert.Same(failure, Assert.Throws<TimeoutException>(() => PresentQueryCharacterizationTests.Read(service, entry)));
                Assert.Single(PresentQueryCharacterizationTests.Read(service, entry).Entities);
                Assert.Equal(2, calls);
            }
        }

        /// <summary>Dispose停止八入口且冪等，不處置host借入SDK；每次操作必須先取局部SDK才可drain。</summary>
        [Fact]
        public void Dispose_RejectsAllNewEntriesAndDoesNotDisposeBorrowedSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var service = new PresentRecordQueryService(new object(), sdk.Object);
            ((IDisposable)service).Dispose();
            foreach (var entry in Entries)
                Assert.Throws<ObjectDisposedException>(() => PresentQueryCharacterizationTests.Read(service, entry));
            ((IDisposable)service).Dispose();
            disposable.Verify(item => item.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>同步SDK已入場後Dispose只拒新呼叫，舊工作能排空；兩事件及Task在finally清理，不留下懸掛工作。</summary>
        [Fact]
        public async Task DisposeRace_DrainsStartedIoWithoutKeepingSdkOwner()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var sdk = Source(name =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return new EntityCollection(new[] { new Entity(name, ContactCharacterizationTests.FirstId) });
            });
            var service = new PresentRecordQueryService(new object(), sdk.Object);
            var work = Task.Run(() => PresentQueryCharacterizationTests.Read(service, "fetch"));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                ((IDisposable)service).Dispose();
                Assert.Throws<ObjectDisposedException>(() => PresentQueryCharacterizationTests.Read(service, "weeks"));
            }
            finally { release.Set(); await work; }
        }

        /// <summary>1000次八入口A/B並行只得到各SDK的列，caller mutation不留在服務；全部Task完成後active=0。</summary>
        [Fact]
        public async Task ConcurrentScopes_DoNotShareRowsAndReturnToBaseline()
        {
            var active = 0;
            var a = new PresentRecordQueryService(new object(), Scope(ContactCharacterizationTests.FirstId).Object);
            var b = new PresentRecordQueryService(new object(), Scope(ContactCharacterizationTests.SecondId).Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var row = Assert.Single(PresentQueryCharacterizationTests.Read(index % 2 == 0 ? a : b, Entries[index % Entries.Length]).Entities);
                Assert.Equal(index % 2 == 0 ? ContactCharacterizationTests.FirstId : ContactCharacterizationTests.SecondId, row.Id);
                Assert.Equal("synthetic", row["new_name"]);
                row["new_name"] = "caller";
            })));
            Assert.Equal(0, active);
            ((IDisposable)a).Dispose(); ((IDisposable)b).Dispose();

            Mock<IOrganizationService> Scope(Guid id) => Source(name =>
            {
                Interlocked.Increment(ref active);
                try { return new EntityCollection(new[] { new Entity(name, id) { ["new_name"] = "synthetic" } }); }
                finally { Interlocked.Decrement(ref active); }
            });
        }

        /// <summary>服務活著也不能保留無用logger；Dispose後清SDK強參照。有限GC觀測與壓力drain互補，不冒稱全程序profiling。</summary>
        [Fact]
        public void Ownership_ReleasesLoggerAndDisposedSdk()
        {
            var (service, logger, sdk) = CreateWeak();
            Collect(); Assert.False(logger.IsAlive); Assert.True(sdk.IsAlive);
            ((IDisposable)service).Dispose();
            Collect(); Assert.False(sdk.IsAlive);
            GC.KeepAlive(service);
        }

        /// <summary>私有固定入口清單不含使用者／租戶狀態，不公開可變集合給產品。</summary>
        private static readonly string[] Entries = { "weeks", "sorted", "fetch", "in-weekly", "date", "weekly", "months", "list" };

        /// <summary>替身只依實際查詢根表回合成候選；不替產品預驗ID或發布狀態。</summary>
        private static Mock<IOrganizationService> Source(Func<string, EntityCollection> read)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
                read(Assert.IsType<QueryExpression>(input).EntityName));
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var xml = XElement.Parse(Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(input).Query).Query);
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = read((string)xml.Element("entity")!.Attribute("name")!);
                return response;
            });
            return sdk;
        }

        /// <summary>NoInlining讓局部變數離開stack，唯一預期強owner是回傳受測服務，避免GC偽保留。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (PresentRecordQueryService Service, WeakReference Logger, WeakReference Sdk) CreateWeak()
        {
            var logger = new object();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            return (new PresentRecordQueryService(logger, sdk), new WeakReference(logger), new WeakReference(sdk));
        }

        /// <summary>固定三輪GC而非無界重試或timer，不把偶然單次記憶體數字當作有界生命週期證據。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }
    }
}
