/*
 * 檔案整體說明
 *
 * 目的與定位：驗證產品組合根真正傳入連線池的完整隔離鍵，而非只檢查註冊文字。 外部連線由合成 SDK 替身取代；容器是唯一 pool owner，每案結束必確定性 Dispose。
 *
 * 主要功能與流程：
 * 本檔宣告 ProductRegistrationTests。
 * - 兩個產品使用相同合成組織／身分時，實際租約仍進入不同 Product 鍵。 若 factory 仍寫死 ChurchReport，本案必失敗；釋放後租約／等待者歸零，容器再關閉 client。
 * - 原本無參數的公開 overload 必須保留 ChurchReport 鍵與 DI 組態讀取， 不能在註冊期間另建容器或連線，亦不能讓未解析服務逃過容器關閉。
 * - 同一容器先後註冊不同產品必須拒絕，不能讓 TryAddSingleton 靜默沿用第一個產品。 失敗前後服務描述數相等，避免部分註冊污染現有組合根。
 * - 完全相同組態與產品的重複註冊必須冪等，不能建立第二份 singleton owner 或 scoped provider。
 * 測試案例入口包含 AddToolUtility_ProductParameter_PartitionsConnectionKey、AddToolUtility_LegacyOverload_UsesChurchReport、
 * AddToolUtility_DifferentProductsInOneContainer_RejectsWithoutMutation、
 * AddToolUtility_SameRegistration_IsIdempotent、AddToolUtility_InvalidProduct_RejectsBeforeRegistration、
 * AddToolUtility_ConflictingConfiguration_RejectsWithoutMutation、Configuration、Client；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ToolUtility.Tests.DependencyInjection
{
    /// <summary>
    /// 驗證產品組合根真正傳入連線池的完整隔離鍵，而非只檢查註冊文字。
    /// 外部連線由合成 SDK 替身取代；容器是唯一 pool owner，每案結束必確定性 Dispose。
    /// </summary>
    public sealed class ProductRegistrationTests
    {
        /// <summary>
        /// 兩個產品使用相同合成組織／身分時，實際租約仍進入不同 Product 鍵。
        /// 若 factory 仍寫死 ChurchReport，本案必失敗；釋放後租約／等待者歸零，容器再關閉 client。
        /// </summary>
        [Fact]
        public void AddToolUtility_ProductParameter_PartitionsConnectionKey()
        {
            var aClient = Client();
            var bClient = Client();
            var aServices = Services(aClient);
            var bServices = Services(bClient);
            aServices.AddToolUtility(Configuration(), "ProductA");
            bServices.AddToolUtility(Configuration(), "ProductB");

            using (var a = aServices.BuildServiceProvider())
            using (var b = bServices.BuildServiceProvider())
            {
                var aManager = a.GetRequiredService<IDataverseConnectionManager>();
                var bManager = b.GetRequiredService<IDataverseConnectionManager>();
                using (var first = aManager.Acquire())
                using (var second = bManager.Acquire())
                {
                    Assert.NotSame(first.Service, second.Service);
                    var aKey = ActualKey(aManager);
                    var bKey = ActualKey(bManager);
                    Assert.Equal("ProductA", aKey.Product);
                    Assert.Equal("ProductB", bKey.Product);
                    Assert.NotEqual(aKey, bKey);
                    Assert.Equal(aKey.OrganizationUrl, bKey.OrganizationUrl);
                    Assert.Equal(aKey.EffectiveIdentity, bKey.EffectiveIdentity);
                }
                Assert.Equal(0, aManager.GetMetrics().Leased);
                Assert.Equal(0, bManager.GetMetrics().Waiting);
                Assert.Equal(1, aManager.GetMetrics().TotalReleases);
                Assert.Equal(1, bManager.GetMetrics().TotalReleases);
                aClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
                bClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
            }
            aClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Once);
            bClient.As<IDisposable>().Verify(client => client.Dispose(), Times.Once);
        }

        /// <summary>
        /// 原本無參數的公開 overload 必須保留 ChurchReport 鍵與 DI 組態讀取，
        /// 不能在註冊期間另建容器或連線，亦不能讓未解析服務逃過容器關閉。
        /// </summary>
        [Fact]
        public void AddToolUtility_LegacyOverload_UsesChurchReport()
        {
            var client = Client();
            var services = Services(client);
            services.AddSingleton<IConfiguration>(Configuration());
            services.AddToolUtility();
            using (var provider = services.BuildServiceProvider())
            {
                var manager = provider.GetRequiredService<IDataverseConnectionManager>();
                using (manager.Acquire())
                    Assert.Equal("ChurchReport", ActualKey(manager).Product);
                Assert.Equal(0, manager.GetMetrics().Leased);
            }
            client.As<IDisposable>().Verify(service => service.Dispose(), Times.Once);
        }

        /// <summary>
        /// 同一容器先後註冊不同產品必須拒絕，不能讓 TryAddSingleton 靜默沿用第一個產品。
        /// 失敗前後服務描述數相等，避免部分註冊污染現有組合根。
        /// </summary>
        [Fact]
        public void AddToolUtility_DifferentProductsInOneContainer_RejectsWithoutMutation()
        {
            var services = Services(Client());
            var configuration = Configuration();
            services.AddToolUtility(configuration, "ProductA");
            var count = services.Count;

            Assert.Throws<InvalidOperationException>(() => services.AddToolUtility(configuration, "ProductB"));
            Assert.Equal(count, services.Count);
        }

        /// <summary>
        /// 完全相同組態與產品的重複註冊必須冪等，不能建立第二份 singleton owner 或 scoped provider。
        /// </summary>
        [Fact]
        public void AddToolUtility_SameRegistration_IsIdempotent()
        {
            var services = Services(Client());
            var configuration = Configuration();
            services.AddToolUtility(configuration, "ProductA");
            var count = services.Count;

            services.AddToolUtility(configuration, "ProductA");

            Assert.Equal(count, services.Count);
        }

        /// <summary>
        /// 產品名稱是受信任組合根的必要邊界；空白不得回退另一產品或改動容器。
        /// </summary>
        /// <param name="product">合成的無效產品值。</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void AddToolUtility_InvalidProduct_RejectsBeforeRegistration(string? product)
        {
            var services = new ServiceCollection();
            Assert.Throws<ArgumentException>(() => services.AddToolUtility(Configuration(), product!));
            Assert.Empty(services);
        }

        /// <summary>
        /// 同產品使用不同組態物件再註冊也須明確拒絕，避免 manager 與 facade 各自讀不同的身分／組織。
        /// </summary>
        [Fact]
        public void AddToolUtility_ConflictingConfiguration_RejectsWithoutMutation()
        {
            var services = Services(Client());
            services.AddToolUtility(Configuration(), "ProductA");
            var count = services.Count;
            Assert.Throws<InvalidOperationException>(() => services.AddToolUtility(Configuration(), "ProductA"));
            Assert.Equal(count, services.Count);
        }

        /// <summary>合成組態不含實際連線或機密；容量設一避免產生無用預熱 client。</summary>
        private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://synthetic.invalid/organization",
                ["CrmConnection:Username"] = "synthetic-service",
                ["Dataverse:Pool:MinSize"] = "1",
                ["Dataverse:Pool:MaxN"] = "1",
                ["DOTNET_ENVIRONMENT"] = "P1OfflineTest"
            }).Build();

        /// <summary>建立只有確定性 Dispose 的合成 SDK；不啟動外部服務、不保存使用者資料。</summary>
        private static Mock<IOrganizationService> Client()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Loose);
            client.As<IDisposable>();
            return client;
        }

        /// <summary>只替換真正外部 I/O factory，保留 manager／pool 的完整產品鍵與租約流程。</summary>
        private static ServiceCollection Services(Mock<IOrganizationService> client)
        {
            var factory = new Mock<ICrmConnectionService>(MockBehavior.Strict);
            factory.Setup(service => service.CreateOnPremiseClient(
                "https://synthetic.invalid/organization", "synthetic-service", string.Empty)).Returns(client.Object);
            var services = new ServiceCollection();
            services.AddSingleton(factory.Object);
            return services;
        }

        /// <summary>
        /// 白箱只讀已建立子池的真正鍵，避免為測試擴大會揭露組織／服務身分的公開診斷 API。
        /// 讀取只發生於本案已取得租約、無並行新增的固定時點，不保留任何 pool 參照於案外。
        /// </summary>
        private static DataverseConnectionKey ActualKey(IDataverseConnectionManager manager)
        {
            var pool = Assert.IsType<DataverseConnectionManager>(manager).Pool;
            var field = typeof(BoundedClientPool).GetField("_subPools", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var dictionary = Assert.IsAssignableFrom<IDictionary>(field!.GetValue(pool));
            return Assert.IsType<DataverseConnectionKey>(Assert.Single(dictionary.Keys.Cast<object>()));
        }
    }
}
