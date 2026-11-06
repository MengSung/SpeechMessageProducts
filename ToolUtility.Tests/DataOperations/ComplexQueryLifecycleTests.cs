/*
 * 檔案整體說明
 *
 * 目的與定位：驗證真正借用邊界、host路徑及資源生命週期；不開連線，不以Mock自己的驗證代替產品行為。
 *
 * 主要功能與流程：
 * 本檔宣告 ComplexQueryLifecycleTests。
 * - 1000次A/B並行實際走十六個Core入口，各回自己的身份與新候選；caller修改不得影響下一次。
 * - 同步SDK已入場時Dispose只拒新工作；finally釋放gate且await舊工作，確證active歸零而非遺留背景呼叫。
 * - Dispose後服務不再留executor、從未保存logger；有限GC檢查保持服務本身存活，避免假陽性。
 * - 純SDK轉接三路徑原樣通過，再由Core驗證；不更換Execute路線或轉移Dispose所有權。
 * 測試案例入口包含 ConcurrentScopes_PreserveIdentityAndDrain、DisposeRace_DrainsBorrowedWork、
 * Dispose_ReleasesExecutorAndUnusedLogger、OrganizationAdapter_PreservesAllThreeRoutes、
 * LegacyHostAdapter_PreservesAllThreeRoutes、Collect。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>驗證真正借用邊界、host路徑及資源生命週期；不開連線，不以Mock自己的驗證代替產品行為。</summary>
    public sealed class ComplexQueryLifecycleTests
    {
        /// <summary>1000次A/B並行實際走十六個Core入口，各回自己的身份與新候選；caller修改不得影響下一次。</summary>
        [Fact]
        public async Task ConcurrentScopes_PreserveIdentityAndDrain()
        {
            var active = 0;
            var a = Scope(ContactCharacterizationTests.FirstId);
            var b = Scope(ContactCharacterizationTests.SecondId);
            using var first = new ComplexQueryDataService(null, a.Object);
            using var second = new ComplexQueryDataService(null, b.Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var output = ComplexQueryCharacterizationTests.Read(index % 2 == 0 ? first : second,
                    ComplexQueryCharacterizationTests.Entries[index % 16]);
                var row = output is Entity entity ? entity : Assert.Single(Assert.IsType<EntityCollection>(output).Entities);
                Assert.Equal(index % 2 == 0 ? ContactCharacterizationTests.FirstId : ContactCharacterizationTests.SecondId, row.Id);
                Assert.Equal("same-name", row["new_name"]);
                row["new_name"] = "caller-owned";
            })));
            Assert.Equal(0, active);

            Mock<ICrmQueryExecutor> Scope(Guid id) => ComplexQuerySafetyTests.Source(table =>
            {
                Interlocked.Increment(ref active);
                try { return new EntityCollection(new[] { new Entity(table, id) { ["new_name"] = "same-name" } }) { TotalRecordCount = 1 }; }
                finally { Interlocked.Decrement(ref active); }
            });
        }

        /// <summary>同步SDK已入場時Dispose只拒新工作；finally釋放gate且await舊工作，確證active歸零而非遺留背景呼叫。</summary>
        [Fact]
        public async Task DisposeRace_DrainsBorrowedWork()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var active = 0;
            var client = ComplexQuerySafetyTests.Source(_ =>
            {
                Interlocked.Increment(ref active);
                try { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new EntityCollection(); }
                finally { Interlocked.Decrement(ref active); }
            });
            using var service = new ComplexQueryDataService(null, client.Object);
            var work = Task.Run(() => service.RetrieveManyToOneCollection());
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                service.Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.RetrieveManyToOneCollection());
            }
            finally { release.Set(); await work; }
            Assert.Equal(0, active);
        }

        /// <summary>Dispose後服務不再留executor、從未保存logger；有限GC檢查保持服務本身存活，避免假陽性。</summary>
        [Fact]
        public void Dispose_ReleasesExecutorAndUnusedLogger()
        {
            var (service, executor, logger) = Weak();
            Collect(); Assert.False(logger.IsAlive); Assert.True(executor.IsAlive);
            service.Dispose(); Collect(); Assert.False(executor.IsAlive);
            Assert.Same(typeof(IComplexQueryService).Assembly, typeof(ComplexQueryDataService).Assembly);
            GC.KeepAlive(service);
        }

        /// <summary>純SDK轉接三路徑原樣通過，再由Core驗證；不更換Execute路線或轉移Dispose所有權。</summary>
        [Fact]
        public void OrganizationAdapter_PreservesAllThreeRoutes()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            sdk.Setup(item => item.Retrieve("list", ComplexQueryCharacterizationTests.InputId, It.IsAny<ColumnSet>()))
                .Returns(new Entity("list", ComplexQueryCharacterizationTests.InputId) { ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
            var response = new RetrieveMultipleResponse(); response.Results["EntityCollection"] = new EntityCollection();
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns(response);
            using (var service = new ComplexQueryDataService(null, new OrganizationQueryExecutor(sdk.Object)))
            {
                Assert.Empty(service.RetrieveDynamicMemberListDynamics365(ComplexQueryCharacterizationTests.InputId).Entities);
                Assert.Empty(service.QueryListByContactId(ComplexQueryCharacterizationTests.InputId, "new_parent").Entities);
            }
            sdk.Verify(item => item.Retrieve("list", ComplexQueryCharacterizationTests.InputId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.Verify(item => item.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
            disposable.Verify(item => item.Dispose(), Times.Never);
        }

#if NET10_0
        /// <summary>保留原host constructor的真正ICrmClient轉接，三個SDK入口皆實際呼叫，未讀IsReady或Dispose。</summary>
        [Fact]
        public void LegacyHostAdapter_PreservesAllThreeRoutes()
        {
            var client = new Mock<ToolUtilityNameSpace.Interfaces.ICrmClient>(MockBehavior.Strict);
            client.Setup(item => item.Retrieve("list", ComplexQueryCharacterizationTests.InputId, It.IsAny<ColumnSet>()))
                .Returns(new Entity("list", ComplexQueryCharacterizationTests.InputId) { ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            client.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
            var response = new RetrieveMultipleResponse(); response.Results["EntityCollection"] = new EntityCollection();
            client.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns(response);
            using (var service = new ComplexQueryService(null, client.Object))
            {
                Assert.Empty(service.RetrieveDynamicMemberListDynamics365(ComplexQueryCharacterizationTests.InputId).Entities);
                Assert.Empty(service.QueryListByContactId(ComplexQueryCharacterizationTests.InputId, "new_parent").Entities);
            }
            client.Verify(item => item.Retrieve("list", ComplexQueryCharacterizationTests.InputId, It.IsAny<ColumnSet>()), Times.Once);
            client.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            client.Verify(item => item.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
            client.VerifyNoOtherCalls();
        }
#endif

        /// <summary>禁止JIT延長區域變數的存活範圍，唯一executor強參照應由服務持有。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (ComplexQueryDataService, WeakReference, WeakReference) Weak()
        {
            var executor = new Mock<ICrmQueryExecutor>(MockBehavior.Strict).Object;
            var logger = new object();
            return (new ComplexQueryDataService(logger, executor), new WeakReference(executor), new WeakReference(logger));
        }

        /// <summary>固定三輪GC，不無界輪詢；僅檢查Dispose後是否仍有產品持有者。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }
    }
}
