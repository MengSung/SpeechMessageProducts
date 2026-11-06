/*
 * 檔案整體說明
 *
 * 目的與定位：對實際 ContactService 注入已污染／過期的帳號快取，證明本次帳密核對必須直接讀原授權 SDK。 所有帳號、密碼、資料庫 ID 均為合成，結果只活於本案例；SDK 替身不連網，cache 
 * 替身模擬先前命中。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactAuthenticationIsolationTests。
 * - 缺失密碼不能因SDK有同為null/空的欄位而變成登入成功；只作業務拒絕，不查SDK或污染cache。
 * - 舊LINE/姓名鍵缺完整已授權scope且回傳mutable Entity，不能只把密碼入口修掉就保留其他串用通道。 注入其他subject的污染cache；兩個讀取均須忽略cache、
 * 回本SDK的新資料且明確caller取消前不I/O。
 * - 成功、密碼錯誤與密碼變更均不能重用之前已核對的 Entity；每次進 SDK、完全不讀寫快取。
 * - 真正 SDK 故障不得從先前快取回成功；保留同一原始 Exception，讓最終 host 告警取得真實故障。
 * 測試案例入口包含 MissingPassword_NeverAuthenticatesAnUnsetSecret、
 * OtherContactLookups_DoNotReuseUnpartitionedIdentityCache、
 * AccountLookup_AlwaysChecksCurrentPasswordInOriginalSdkScope、
 * AccountLookup_SdkFailureCannotFallBackToCachedAuthenticatedContact、
 * AccountLookup_InternalCancellationRemainsOriginalFault、
 * AccountLookup_CancellationNeverPublishesAuthenticationResult、
 * ParallelScopes_SameAccountCannotReuseAnotherSubjectsAuthentication、PoisonedCache；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Caching;
using ToolUtilityNameSpace.ContactOperations;
using Xunit;

namespace ToolUtility.Tests.ContactOperations
{
    /// <summary>
    /// 對實際 ContactService 注入已污染／過期的帳號快取，證明本次帳密核對必須直接讀原授權 SDK。
    /// 所有帳號、密碼、資料庫 ID 均為合成，結果只活於本案例；SDK 替身不連網，cache 替身模擬先前命中。
    /// </summary>
    public sealed class ContactAuthenticationIsolationTests
    {
        /// <summary>缺失密碼不能因SDK有同為null/空的欄位而變成登入成功；只作業務拒絕，不查SDK或污染cache。</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task MissingPassword_NeverAuthenticatesAnUnsetSecret(string? password)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection(new[]
            {
                new Entity("contact", new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")) { ["new_app_pass"] = password }
            }));
            var cache = PoisonedCache();
            var service = new ContactService(new object(), sdk.Object, cache.Object);
            Assert.Null(await service.RetrieveByAccountNumberAsync("synthetic", password!));
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Never);
            cache.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 舊LINE/姓名鍵缺完整已授權scope且回傳mutable Entity，不能只把密碼入口修掉就保留其他串用通道。
        /// 注入其他subject的污染cache；兩個讀取均須忽略cache、回本SDK的新資料且明確caller取消前不I/O。
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OtherContactLookups_DoNotReuseUnpartitionedIdentityCache(bool byName)
        {
            var currentId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() => Rows(currentId, "synthetic-current"));
            var cache = PoisonedCache();
            var service = new ContactService(new object(), sdk.Object, cache.Object);
            var result = await (byName ? service.RetrieveByFullNameAsync("synthetic")
                : service.RetrieveByLineIdAsync("synthetic"));
            Assert.NotNull(result);
            Assert.Equal(currentId, result.Id);
            result["fullname"] = "caller-mutation";
            var fresh = await (byName ? service.RetrieveByFullNameAsync("synthetic")
                : service.RetrieveByLineIdAsync("synthetic"));
            Assert.NotNull(fresh);
            Assert.False(fresh.Contains("fullname"));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => byName
                ? service.RetrieveByFullNameAsync("synthetic", cancelled.Token)
                : service.RetrieveByLineIdAsync("synthetic", cancelled.Token));
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
            cache.VerifyNoOtherCalls();
        }

        /// <summary>成功、密碼錯誤與密碼變更均不能重用之前已核對的 Entity；每次進 SDK、完全不讀寫快取。</summary>
        [Theory]
        [InlineData("synthetic-current", true)]
        [InlineData("synthetic-wrong", false)]
        [InlineData("synthetic-old", false)]
        public async Task AccountLookup_AlwaysChecksCurrentPasswordInOriginalSdkScope(string password, bool success)
        {
            var currentId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var sdk = Sdk(currentId, "synthetic-current");
            var cache = PoisonedCache();
            var service = new ContactService(new object(), sdk.Object, cache.Object);

            var result = await service.RetrieveByAccountNumberAsync("synthetic-account", password);

            if (success) Assert.Equal(currentId, Assert.IsType<Entity>(result).Id);
            else Assert.Null(result);
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            cache.VerifyNoOtherCalls();
        }

        /// <summary>真正 SDK 故障不得從先前快取回成功；保留同一原始 Exception，讓最終 host 告警取得真實故障。</summary>
        [Fact]
        public async Task AccountLookup_SdkFailureCannotFallBackToCachedAuthenticatedContact()
        {
            var fault = new TimeoutException("synthetic-private-fault");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(fault);
            var cache = PoisonedCache();
            var service = new ContactService(new object(), sdk.Object, cache.Object);

            Assert.Same(fault, await Assert.ThrowsAsync<TimeoutException>(() =>
                service.RetrieveByAccountNumberAsync("synthetic-account", "synthetic-current")));
            cache.VerifyNoOtherCalls();
        }

        /// <summary>SDK 內部取消／逾時而 caller 未取消時保留同一故障，不把機密核對失敗變成正常取消或 cache 成功。</summary>
        [Fact]
        public async Task AccountLookup_InternalCancellationRemainsOriginalFault()
        {
            var fault = new OperationCanceledException("synthetic-internal-timeout");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(fault);
            var cache = PoisonedCache();
            var operation = new ContactService(new object(), sdk.Object, cache.Object)
                .RetrieveByAccountNumberAsync("synthetic-account", "synthetic-current");
            Assert.Same(fault, await Assert.ThrowsAsync<OperationCanceledException>(() => operation));
            Assert.True(operation.IsFaulted);
            Assert.False(operation.IsCanceled);
            cache.VerifyNoOtherCalls();
        }

        /// <summary>明確 caller 取消前不讀 SDK／cache；I/O 期間取消後也不能發布已核對的成功 Entity。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AccountLookup_CancellationNeverPublishesAuthenticationResult(bool duringIo)
        {
            using var cancellation = new CancellationTokenSource();
            var sdk = Sdk(new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "synthetic-current");
            if (duringIo)
                sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
                {
                    cancellation.Cancel();
                    return Rows(new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "synthetic-current");
                });
            else cancellation.Cancel();
            var cache = PoisonedCache();
            var service = new ContactService(new object(), sdk.Object, cache.Object);

            var operation = service.RetrieveByAccountNumberAsync("synthetic-account", "synthetic-current", cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.True(operation.IsCanceled);
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), duringIo ? Times.Once() : Times.Never());
            cache.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 同帳號文字存在兩個獨立已授權 SDK scope，1000 次 A/B 正確／錯誤密碼交錯。
        /// 只有本 scope 正確密碼成功，caller 修改結果不污染下一次；所有測試 Task await 排空，active 歸零。
        /// </summary>
        [Fact]
        public async Task ParallelScopes_SameAccountCannotReuseAnotherSubjectsAuthentication()
        {
            var aId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var bId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var active = 0;
            var cache = PoisonedCache();
            var a = new ContactService(new object(), Source(aId, "synthetic-a").Object, cache.Object);
            var b = new ContactService(new object(), Source(bId, "synthetic-b").Object, cache.Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(async () =>
            {
                var useA = index % 2 == 0;
                var correct = index % 3 != 0;
                var result = await (useA ? a : b).RetrieveByAccountNumberAsync("synthetic-account",
                    correct ? useA ? "synthetic-a" : "synthetic-b" : "synthetic-wrong");
                if (correct)
                {
                    Assert.NotNull(result);
                    Assert.Equal(useA ? aId : bId, result.Id);
                    result["fullname"] = "caller-mutation";
                }
                else Assert.Null(result);
            })));
            Assert.Equal(0, active);
            cache.VerifyNoOtherCalls();

            Mock<IOrganizationService> Source(Guid id, string secret)
            {
                var source = new Mock<IOrganizationService>(MockBehavior.Strict);
                source.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
                {
                    Interlocked.Increment(ref active);
                    try { return Rows(id, secret); }
                    finally { Interlocked.Decrement(ref active); }
                });
                return source;
            }
        }

        /// <summary>模擬之前已通過驗證的另一筆快取 Entity；產品若讀 cache 會直接暴露錯誤身份並使斷言失敗。</summary>
        private static Mock<ICrmCacheService> PoisonedCache()
        {
            var cache = new Mock<ICrmCacheService>(MockBehavior.Strict);
            cache.Setup(item => item.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<Func<Task<Entity>>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).ReturnsAsync(
                    new Entity("contact", new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"))
                    { ["fullname"] = "synthetic-other-subject" });
            return cache;
        }

        /// <summary>固定查詢帳號／啟用欄位及第一筆限制；假 SDK 只提供本 scope 當前密碼，不代替產品執行核對。</summary>
        private static Mock<IOrganizationService> Sdk(Guid id, string secret)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryByAttribute>(input);
                Assert.Equal("contact", query.EntityName);
                Assert.Equal(1, query.TopCount);
                Assert.Equal(new[] { "new_app_acount", "statecode" }, query.Attributes);
                Assert.Equal(new object[] { "synthetic-account", 0 }, query.Values);
                return Rows(id, secret);
            });
            return sdk;
        }

        /// <summary>每次回傳獨立的合成 Entity，不把 mutable 回應、密碼或使用者身份保存在 static／共用快取。</summary>
        private static EntityCollection Rows(Guid id, string secret) => new EntityCollection
        {
            Entities = { new Entity("contact", id) { ["new_app_pass"] = secret } }
        };
    }
}
