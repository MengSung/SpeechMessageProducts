/*
 * 檔案整體說明
 *
 * 目的與定位：六入口獨立故障矩陣驗正式ID／根表／整頁預算，所有資料是合成值；不把Mock通過當實機整合。 另驗結構名與GUID在I/O前拒絕、並行scope隔離、Dispose/drain與弱引用，不建立無主背景資源。
 *
 * 主要功能與流程：
 * 本檔宣告 RelationshipQuerySafetyTests。
 * - 六個實作和契約必須實際來自雙目標Core；不以同名host複本通過組件驗證。
 * - 獨立列出每入口／故障組合，不能讓第一個失敗掩蓋後面入口；固定清單不含scope資料。
 * - 5000列為單頁本地安全預算；超量先拒絕，不配置超量ID集合，也不回部分或靜默刪重。
 * - 五個動態N:1結構名稱逐個注入XML/SQL樣式字元；不得送SDK，錯誤不回顯輸入。
 * 測試案例入口包含 ImplementationAndContract_AreInCore、InvalidPages、EveryEntrypoint_RejectsInvalidPage、
 * DynamicNames_RejectUnsafeStructureBeforeIo、ManyToManyNames_RejectUnsafeStructureBeforeIo、
 * EmptyIdentifier_RejectsBeforeIo、AllEntrypoints_PreserveFailureAndAllowExplicitRetry、
 * Dispose_RejectsAllNewEntriesAndDoesNotDisposeBorrowedSdk；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 六入口獨立故障矩陣驗正式ID／根表／整頁預算，所有資料是合成值；不把Mock通過當實機整合。
    /// 另驗結構名與GUID在I/O前拒絕、並行scope隔離、Dispose/drain與弱引用，不建立無主背景資源。
    /// </summary>
    public sealed class RelationshipQuerySafetyTests
    {
        /// <summary>六個實作和契約必須實際來自雙目標Core；不以同名host複本通過組件驗證。</summary>
        [Fact]
        public void ImplementationAndContract_AreInCore()
        {
            Assert.Same(typeof(IRelationshipQueryService).Assembly, typeof(RelationshipQueryService).Assembly);
            Assert.Equal("ToolUtility.Core", typeof(RelationshipQueryService).Assembly.GetName().Name);
            Assert.Equal(6, typeof(IRelationshipQueryService).GetMethods().Length);
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
                RelationshipQueryCharacterizationTests.Read(new RelationshipQueryService(new object(), sdk.Object), entry));
        }

        /// <summary>五個動態N:1結構名稱逐個注入XML/SQL樣式字元；不得送SDK，錯誤不回顯輸入。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void DynamicNames_RejectUnsafeStructureBeforeIo(int index)
        {
            foreach (var entry in new[] { "plain", "ordered", "weekly" })
            {
                var values = new[] { "contact", "contactid", "new_contact_family_leader_list", "list" };
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                var service = new RelationshipQueryService(new object(), sdk.Object);
                if (index < 4) values[index] = "a<&\"'";
                Assert.Throws<ArgumentException>(() => RelationshipQueryCharacterizationTests.Read(service, entry,
                    values[0], values[1], index == 4 ? "bad-id" : ContactCharacterizationTests.FirstId.ToString("D"), values[2], values[3]));
                sdk.VerifyNoOtherCalls();
            }
        }

        /// <summary>N:N七個動態結構名稱各自拒絕，不因用QueryExpression而略過根表／關聯欄位驗證。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void ManyToManyNames_RejectUnsafeStructureBeforeIo(int index)
        {
            var names = new[] { "new_app_named", "list", "list", "listid", "listmember", "listid", "entityid" };
            names[index] = "a<&\"'";
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new RelationshipQueryService(new object(), sdk.Object);
            Assert.Throws<ArgumentException>(() => service.QueryManyToMany(names[0], names[1], names[2], names[3],
                names[4], names[5], names[6], ContactCharacterizationTests.FirstId));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>無論字串或Guid入口都拒絕零資料庫ID；借入SDK不會因空ID而讀取更廣範圍。</summary>
        [Theory]
        [InlineData("plain")]
        [InlineData("ordered")]
        [InlineData("weekly")]
        [InlineData("many")]
        [InlineData("contact")]
        public void EmptyIdentifier_RejectsBeforeIo(string entry)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentException>(() => RelationshipQueryCharacterizationTests.Read(
                new RelationshipQueryService(new object(), sdk.Object), entry, id: Guid.Empty.ToString("D")));
            sdk.VerifyNoOtherCalls();
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
                var service = new RelationshipQueryService(new object(), sdk.Object);
                Assert.Same(failure, Assert.Throws<TimeoutException>(() => RelationshipQueryCharacterizationTests.Read(service, entry)));
                Assert.Single(RelationshipQueryCharacterizationTests.Read(service, entry).Entities);
                Assert.Equal(2, calls);
            }
        }

        /// <summary>Dispose停止六入口且冪等，不處置host借入SDK；每次操作必須先取局部SDK才可drain。</summary>
        [Fact]
        public void Dispose_RejectsAllNewEntriesAndDoesNotDisposeBorrowedSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var service = new RelationshipQueryService(new object(), sdk.Object);
            ((IDisposable)service).Dispose();
            foreach (var entry in Entries)
                Assert.Throws<ObjectDisposedException>(() => RelationshipQueryCharacterizationTests.Read(service, entry));
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
            var service = new RelationshipQueryService(new object(), sdk.Object);
            var work = Task.Run(() => service.QueryListOfContactManyToMany(ContactCharacterizationTests.FirstId));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                ((IDisposable)service).Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.RetrieveManyToOneWithLinkEntity());
            }
            finally { release.Set(); await work; }
        }

        /// <summary>1000次六入口A/B並行只得到各SDK的列，caller mutation不留在服務；全部Task完成後active=0。</summary>
        [Fact]
        public async Task ConcurrentScopes_DoNotShareRowsAndReturnToBaseline()
        {
            var active = 0;
            var a = new RelationshipQueryService(new object(), Scope(ContactCharacterizationTests.FirstId).Object);
            var b = new RelationshipQueryService(new object(), Scope(ContactCharacterizationTests.SecondId).Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var row = Assert.Single(RelationshipQueryCharacterizationTests.Read(index % 2 == 0 ? a : b, Entries[index % Entries.Length]).Entities);
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
        private static readonly string[] Entries = { "plain", "ordered", "linked", "weekly", "many", "contact" };

        /// <summary>替身只依實際查詢根表回合成候選；不替產品預驗ID或發布狀態。</summary>
        private static Mock<IOrganizationService> Source(Func<string, EntityCollection> read)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
                read(Assert.IsType<QueryExpression>(input).EntityName));
            return sdk;
        }

        /// <summary>NoInlining讓局部變數離開stack，唯一預期強owner是回傳受測服務，避免GC偽保留。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (RelationshipQueryService Service, WeakReference Logger, WeakReference Sdk) CreateWeak()
        {
            var logger = new object();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            return (new RelationshipQueryService(logger, sdk), new WeakReference(logger), new WeakReference(sdk));
        }

        /// <summary>固定三輪GC而非無界重試或timer，不把偶然單次記憶體數字當作有界生命週期證據。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }
    }
}
