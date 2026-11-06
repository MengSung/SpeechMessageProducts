/*
 * 檔案整體說明
 *
 * 目的與定位：注入屬性轉字串故障、SDK 異常頁與並行 A/B 操作，保護固定安全診斷、原始例外及單次發布。 全部資料為合成值；替身僅替代外部 SDK I/O，不實作產品驗證、不連線或發送通知。
 *
 * 主要功能與流程：
 * 本檔宣告 AttributeExtensionSafetyTests、FaultingValue。
 * - 沙箱可不建立 logger；舊簽章仍拒絕 null，不能以隱藏記錄物件維持相容。
 * - 三入口的 null Entity 仍包裝真實引數例外，但外層訊息不能包含呼叫者的欄位文字。
 * - 寫入欄位空值保持既有包裝及內層型別；拒絕不改動原實體或其他欄位。
 * - 欄位物件的 ToString 真正失敗時，保留同一例外供 host 統一通知，外層不回顯資料／原 Message； caller 修復自己的欄位後可重試，服務不保存失敗值。
 * 測試案例入口包含 AttributeConstructor_OffersStatelessPathAndPreservesLegacyNullRejection、
 * AttributeNullEntity_PreservesWrapperWithoutEchoingInput、AttributeInvalidProperty_RejectsWithoutMutation、
 * AttributeFormattingFault_PreservesOriginalExceptionAndAllowsRetry、AttributeLifetime_DoesNotRetainLoggerOrEntity、
 * ExtensionInvalidArguments_RejectBeforeSdkIo、ExtensionInvalidFetch_RejectsBeforeSdkIo、
 * SdkResponseIntegrity_RejectsIncompleteResponse；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.Extensions;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 注入屬性轉字串故障、SDK 異常頁與並行 A/B 操作，保護固定安全診斷、原始例外及單次發布。
    /// 全部資料為合成值；替身僅替代外部 SDK I/O，不實作產品驗證、不連線或發送通知。
    /// </summary>
    public sealed class AttributeExtensionSafetyTests
    {
        /// <summary>沙箱可不建立 logger；舊簽章仍拒絕 null，不能以隱藏記錄物件維持相容。</summary>
        [Fact]
        public void AttributeConstructor_OffersStatelessPathAndPreservesLegacyNullRejection()
        {
            var service = Activator.CreateInstance<EntityAttributeUtilityService>();
            Assert.Equal("", service.GetAttributeValue(new Entity("contact"), "missing"));
            Assert.Throws<ArgumentNullException>(() => new EntityAttributeUtilityService(null!));
        }

        /// <summary>三入口的 null Entity 仍包裝真實引數例外，但外層訊息不能包含呼叫者的欄位文字。</summary>
        /// <param name="operation">固定方法路徑。</param>
        [Theory]
        [InlineData("read")]
        [InlineData("remove")]
        [InlineData("clear")]
        public void AttributeNullEntity_PreservesWrapperWithoutEchoingInput(string operation)
        {
            var service = new EntityAttributeUtilityService(new object());
            Entity entity = null!;
            var error = Assert.Throws<InvalidOperationException>(() =>
                Invoke(service, operation, ref entity, "synthetic_secret_property"));
            Assert.IsType<ArgumentNullException>(error.InnerException);
            Assert.DoesNotContain("synthetic_secret", error.Message);
        }

        /// <summary>寫入欄位空值保持既有包裝及內層型別；拒絕不改動原實體或其他欄位。</summary>
        /// <param name="operation">固定的移除或清空路徑。</param>
        /// <param name="property">故障注入的無效欄位名稱。</param>
        [Theory]
        [InlineData("remove", null)]
        [InlineData("remove", "")]
        [InlineData("remove", " ")]
        [InlineData("clear", null)]
        [InlineData("clear", "")]
        [InlineData("clear", " ")]
        public void AttributeInvalidProperty_RejectsWithoutMutation(string operation, string? property)
        {
            var service = new EntityAttributeUtilityService(new object());
            var entity = new Entity("synthetic_secret_entity") { ["synthetic_keep"] = "保留" };
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(service, operation, ref entity, property!));
            Assert.IsType<ArgumentNullException>(error.InnerException);
            Assert.DoesNotContain("synthetic_secret", error.Message);
            Assert.Single(entity.Attributes);
            Assert.Equal("保留", entity["synthetic_keep"]);
        }

        /// <summary>
        /// 欄位物件的 ToString 真正失敗時，保留同一例外供 host 統一通知，外層不回顯資料／原 Message；
        /// caller 修復自己的欄位後可重試，服務不保存失敗值。
        /// </summary>
        /// <param name="alias">是否由 SDK 別名欄位包裝故障值。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AttributeFormattingFault_PreservesOriginalExceptionAndAllowsRetry(bool alias)
        {
            var failure = new FormatException("synthetic_secret_failure");
            var fault = new FaultingValue(failure);
            var entity = new Entity("synthetic_secret_entity")
            {
                ["synthetic_secret_property"] = alias ? new AliasedValue("contact", "fullname", fault) : fault
            };
            var service = new EntityAttributeUtilityService(new object());
            var error = Assert.Throws<InvalidOperationException>(() => service.GetAttributeValue(entity, "synthetic_secret_property"));
            Assert.Same(failure, error.InnerException);
            Assert.DoesNotContain("synthetic_secret", error.Message);
            entity["synthetic_secret_property"] = "已恢復";
            Assert.Equal("已恢復", service.GetAttributeValue(entity, "synthetic_secret_property"));
        }

        /// <summary>服務存活時，未使用的 logger 與已完成操作的 Entity 均必須可回收，不保留跨使用者狀態。</summary>
        [Fact]
        public void AttributeLifetime_DoesNotRetainLoggerOrEntity()
        {
            var service = CreateWithWeakInputs(out var logger, out var entity);
            for (var attempt = 0; attempt < 3 && (logger.IsAlive || entity.IsAlive); attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(logger.IsAlive);
            Assert.False(entity.IsAlive);
            GC.KeepAlive(service);
        }

        /// <summary>擴充入口的缺失參數與缺 Query 必須在 SDK I/O 前拒絕，不能偽裝成查無資料。</summary>
        [Fact]
        public void ExtensionInvalidArguments_RejectBeforeSdkIo()
        {
            var request = new RetrieveMultipleRequest { Query = new QueryExpression("contact") };
            Assert.Throws<ArgumentNullException>(() => OrganizationServiceExtensions.ExecuteRetrieveMultiple(null!, request));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentNullException>(() => sdk.Object.ExecuteRetrieveMultiple(null!));
            Assert.Throws<ArgumentException>(() => sdk.Object.ExecuteRetrieveMultiple(new RetrieveMultipleRequest()));
            Assert.Throws<ArgumentException>(() => sdk.Object.ExecuteRetrieveMultiple(
                new RetrieveMultipleRequest { Query = new QueryExpression("") }));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 三種 query 共用既有根實體辨識：不改 query，DTD 與缺表在 I/O 前失敗。
        /// 防止 extension 自行複製 Fetch parser，形成可繞過的另一條入口。
        /// </summary>
        /// <param name="xml">合成不合法查詢，不含檔案、網址或真實資料。</param>
        [Theory]
        [InlineData("<fetch />")]
        [InlineData("<fetch><entity name='contact'/><entity name='account'/></fetch>")]
        [InlineData("<!DOCTYPE fetch [<!ENTITY sample 'synthetic'>]><fetch><entity name='contact'/></fetch>")]
        public void ExtensionInvalidFetch_RejectsBeforeSdkIo(string xml)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentException>(() => sdk.Object.ExecuteRetrieveMultiple(
                new RetrieveMultipleRequest { Query = new FetchExpression(xml) }));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// SDK 缺少／錯型回應不是合法空頁；兩個公開入口必須明確回固定完整性故障，
        /// 而不是 NullReference、InvalidCast 或安靜回傳空集合。
        /// </summary>
        /// <param name="shape">合成的異常回應形狀。</param>
        /// <param name="extension">是否走相容擴充入口。</param>
        [Theory]
        [InlineData("null", true)]
        [InlineData("wrong", true)]
        [InlineData("missing-page", true)]
        [InlineData("null", false)]
        [InlineData("wrong", false)]
        [InlineData("missing-page", false)]
        public void SdkResponseIntegrity_RejectsIncompleteResponse(string shape, bool extension)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            OrganizationResponse response = shape == "wrong" ? new OrganizationResponse()
                : shape == "null" ? null! : new RetrieveMultipleResponse();
            sdk.Setup(value => value.Execute(It.IsAny<RetrieveMultipleRequest>())).Returns(response);
            var request = new RetrieveMultipleRequest { Query = new QueryByAttribute("contact") };
            using var service = new EntityQueryService(organizationService: sdk.Object);
            var error = Assert.Throws<InvalidOperationException>(() => extension
                ? sdk.Object.ExecuteRetrieveMultiple(request) : service.ExecuteRetrieveMultiple(request));
            Assert.DoesNotContain("synthetic_secret", error.Message);
        }

        /// <summary>整頁穩定 ID 衝突、空 ID 與跨表記錄必須拒絕，不得刪除衝突列後部分發布。</summary>
        /// <param name="shape">故障列的形狀。</param>
        /// <param name="fetch">是否經 FetchXML 根實體辨識。</param>
        [Theory]
        [InlineData("duplicate", false)]
        [InlineData("empty", false)]
        [InlineData("wrong-entity", false)]
        [InlineData("duplicate", true)]
        [InlineData("empty", true)]
        [InlineData("wrong-entity", true)]
        public void ExtensionPublication_RejectsWholeInvalidPage(string shape, bool fetch)
        {
            var id = new Guid("11111111-1111-1111-1111-111111111111");
            var page = new EntityCollection();
            page.Entities.Add(new Entity("contact", id));
            page.Entities.Add(new Entity(shape == "wrong-entity" ? "account" : "contact",
                shape == "empty" ? Guid.Empty : shape == "duplicate" ? id : Guid.NewGuid()));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(value => value.Execute(It.IsAny<RetrieveMultipleRequest>())).Returns(Response(page));
            var request = new RetrieveMultipleRequest
            {
                Query = fetch ? new FetchExpression("<fetch><entity name='contact'/></fetch>") : new QueryExpression("contact")
            };
            Assert.Throws<InvalidOperationException>(() => sdk.Object.ExecuteRetrieveMultiple(request));
            Assert.Equal(2, page.Entities.Count);
            sdk.Verify(value => value.Execute(request), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 原故障不重包、不吞掉；重試由 caller 明確發起，且 extension 絕不處置借入服務。</summary>
        [Fact]
        public void ExtensionSdkFault_PreservesExceptionRetryAndBorrowedOwnership()
        {
            var failure = new TimeoutException("synthetic_secret_transport");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            sdk.SetupSequence(value => value.Execute(It.IsAny<RetrieveMultipleRequest>()))
                .Throws(failure).Returns(Response(new EntityCollection()));
            var request = new RetrieveMultipleRequest { Query = new QueryExpression("contact") };
            Assert.Same(failure, Assert.Throws<TimeoutException>(() => sdk.Object.ExecuteRetrieveMultiple(request)));
            Assert.Empty(sdk.Object.ExecuteRetrieveMultiple(request).Entities);
            sdk.Verify(value => value.Execute(request), Times.Exactly(2));
            disposable.Verify(value => value.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 一千次有界 A/B 操作共用無狀態屬性服務，各自的 Entity、Query、回應及 SDK 始終獨立。
        /// Task 全數 await 後活動 SDK 計數回零、1000 次完整成功且借入 Dispose 為零。
        /// </summary>
        /// <returns>僅由測試擁有且排空的有限工作集合。</returns>
        [Fact]
        public async Task ConcurrentScopes_AttributeAndQueryDoNotCrossTalkAndDrain()
        {
            var active = 0;
            var completed = 0;
            var shared = new EntityAttributeUtilityService(new object());
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            for (var scope = 0; scope < clients.Length; scope++)
            {
                var expected = scope == 0 ? "scope-a" : "scope-b";
                clients[scope].As<IDisposable>();
                clients[scope].Setup(value => value.Execute(It.IsAny<RetrieveMultipleRequest>())).Returns((OrganizationRequest request) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var query = Assert.IsType<QueryByAttribute>(Assert.IsType<RetrieveMultipleRequest>(request).Query);
                        Assert.Equal(expected, Assert.Single(query.Values));
                        var entity = new Entity("contact", Guid.NewGuid()) { ["synthetic_scope"] = expected };
                        return Response(new EntityCollection(new[] { entity }));
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var scope = index % 2;
                var expected = scope == 0 ? "scope-a" : "scope-b";
                var query = new QueryByAttribute("contact");
                query.AddAttributeValue("synthetic_scope", expected);
                var page = clients[scope].Object.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = query });
                var entity = Assert.Single(page.Entities);
                Assert.Equal(expected, shared.GetAttributeValue(entity, "synthetic_scope"));
                shared.SetEntityAttributeToNull(ref entity, "synthetic_temporary");
                shared.RemoveAttribute(ref entity, "synthetic_temporary");
                Assert.False(entity.Contains("synthetic_temporary"));
                Assert.Equal(expected, entity["synthetic_scope"]);
                Interlocked.Increment(ref completed);
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref completed));
            Assert.Equal(0, Volatile.Read(ref active));
            foreach (var sdk in clients) sdk.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        }

        /// <summary>實際呼叫指定產品入口；不在測試 helper 重做欄位檢查、診斷或結果轉換。</summary>
        private static void Invoke(EntityAttributeUtilityService service, string operation, ref Entity entity, string property)
        {
            if (operation == "read") service.GetAttributeValue(entity, property);
            else if (operation == "remove") service.RemoveAttribute(ref entity, property);
            else service.SetEntityAttributeToNull(ref entity, property);
        }

        /// <summary>只建構 SDK 回應封包，驗證仍由真產品入口執行。</summary>
        private static RetrieveMultipleResponse Response(EntityCollection page)
        {
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = page;
            return response;
        }

        /// <summary>獨立堆疊避免 JIT 暫存強引用干擾回收斷言；服務只能持有本身無狀態實例。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EntityAttributeUtilityService CreateWithWeakInputs(out WeakReference logger, out WeakReference entity)
        {
            var log = new object();
            var candidate = new Entity("contact") { ["synthetic_value"] = "完成後可回收" };
            var service = new EntityAttributeUtilityService(log);
            service.GetAttributeValue(candidate, "synthetic_value");
            logger = new WeakReference(log);
            entity = new WeakReference(candidate);
            return service;
        }

        /// <summary>只擁有本測試建立的例外，不配置資源；ToString 故障用於證明真例外不被字串取代。</summary>
        private sealed class FaultingValue
        {
            private readonly Exception _failure;

            /// <summary>保存本次 fault-injection 的原例外，生命週期僅限單一測試。</summary>
            internal FaultingValue(Exception failure) { _failure = failure; }

            /// <summary>依測試要求拋出同一例外；產品必須保留其身分且不回顯原訊息。</summary>
            public override string ToString() => throw _failure;
        }
    }
}
