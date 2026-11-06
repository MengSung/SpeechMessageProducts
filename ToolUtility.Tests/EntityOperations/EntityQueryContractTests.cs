/*
 * 檔案整體說明
 *
 * 目的與定位：保護不經快取的實體讀取、整頁穩定 ID 與安全 XML 邊界。 每案只借入合成 SDK，故障原樣傳遞給 host；沒有真正 D365 或 LINE 操作。
 *
 * 主要功能與流程：
 * 本檔宣告 EntityQueryContractTests。
 * - 相同 ID 連查兩次必須兩次進 SDK，第二次讀到更新值而非第一個 caller 修改後的物件。 所有欄位沿用舊來源 AllColumns，不把此方法變成 host cache 的別名。
 * - 不經快取入口的 SDK 故障必須維持原 Exception 實例，不回空、不重試或輸出敏感本文。
 * - XML特殊字元作為實體 logical name 並非合法資料庫識別，必須在 I/O 前拒絕； 此方法用型別化 Retrieve 而不是把名稱拼成 FetchXML，不能靠 escape 放行任意結構。
 * - 空 GUID 不能變成廣泛查詢或 draft key，拒絕時 SDK 不得被呼叫。
 * 測試案例入口包含 RetrieveEntityNoCache_RepeatedId_ReadsFreshSdkResult、
 * RetrieveEntityNoCache_SdkFault_PropagatesOriginalException、
 * RetrieveEntityNoCache_InvalidEntityName_RejectsBeforeIo、RetrieveEntityNoCache_EmptyId_RejectsBeforeIo、
 * RetrieveEntityNoCache_IdentityMismatch_RejectsPublication、
 * RetrieveTaskBySubject_XmlSpecialCharacters_CannotInjectConditions、MultiQuery_DuplicateId_RejectsEntireCandidate、
 * MultiQuery_StableIds_AreRequiredAndSameNamesArePreserved；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.EntityOperations
{
    /// <summary>
    /// 保護不經快取的實體讀取、整頁穩定 ID 與安全 XML 邊界。
    /// 每案只借入合成 SDK，故障原樣傳遞給 host；沒有真正 D365 或 LINE 操作。
    /// </summary>
    public sealed class EntityQueryContractTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-4111-8111-111111111111");

        /// <summary>
        /// 相同 ID 連查兩次必須兩次進 SDK，第二次讀到更新值而非第一個 caller 修改後的物件。
        /// 所有欄位沿用舊來源 AllColumns，不把此方法變成 host cache 的別名。
        /// </summary>
        [Fact]
        public void RetrieveEntityNoCache_RepeatedId_ReadsFreshSdkResult()
        {
            var generation = 0;
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId,
                It.Is<ColumnSet>(columns => columns.AllColumns)))
                .Returns(() => new Entity("contact", RecordId) { ["fullname"] = ++generation });
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var first = service.RetrieveEntityNoCache("contact", RecordId);
                first["fullname"] = "Synthetic caller mutation";
                var second = service.RetrieveEntityNoCache("contact", RecordId);
                Assert.NotSame(first, second);
                Assert.Equal(2, second["fullname"]);
            }
            client.Verify(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Exactly(2));
        }

        /// <summary>
        /// 不經快取入口的 SDK 故障必須維持原 Exception 實例，不回空、不重試或輸出敏感本文。
        /// </summary>
        [Fact]
        public void RetrieveEntityNoCache_SdkFault_PropagatesOriginalException()
        {
            var failure = new InvalidOperationException("Synthetic private SDK fault");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Throws(failure);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(
                    () => service.RetrieveEntityNoCache("contact", RecordId)));
        }

        /// <summary>
        /// XML特殊字元作為實體 logical name 並非合法資料庫識別，必須在 I/O 前拒絕；
        /// 此方法用型別化 Retrieve 而不是把名稱拼成 FetchXML，不能靠 escape 放行任意結構。
        /// </summary>
        /// <param name="entityName">非法 logical name。</param>
        [Theory]
        [InlineData("<&\"'")]
        [InlineData("")]
        [InlineData(" ")]
        public void RetrieveEntityNoCache_InvalidEntityName_RejectsBeforeIo(string entityName)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<ArgumentException>(() => service.RetrieveEntityNoCache(entityName, RecordId));
            client.VerifyNoOtherCalls();
        }

        /// <summary>空 GUID 不能變成廣泛查詢或 draft key，拒絕時 SDK 不得被呼叫。</summary>
        [Fact]
        public void RetrieveEntityNoCache_EmptyId_RejectsBeforeIo()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<ArgumentException>(() => service.RetrieveEntityNoCache("contact", Guid.Empty));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// SDK 回錯 ID 或實體是完整性故障，不可把其他資料回傳給原查詢者。
        /// </summary>
        /// <param name="wrongEntity">注入錯誤實體或不同非空 ID。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetrieveEntityNoCache_IdentityMismatch_RejectsPublication(bool wrongEntity)
        {
            var candidate = new Entity(wrongEntity ? "account" : "contact",
                wrongEntity ? RecordId : new Guid("22222222-2222-4222-8222-222222222222"));
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Returns(candidate);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => service.RetrieveEntityNoCache("contact", RecordId));
        }

        /// <summary>
        /// 工作主旨文字含全部 XML 特殊字元仍只能成為一個屬性值；新增節點／條件或改變 statecode
        /// 都屬可重現注入。本案檢查解碼原值與精確條件數，不只驗證查詢字串包含輸入。
        /// </summary>
        [Fact]
        public void RetrieveTaskBySubject_XmlSpecialCharacters_CannotInjectConditions()
        {
            const string subject = "Synthetic <&\"' value";
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request =>
                {
                    var xml = Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query;
                    var conditions = XDocument.Parse(xml).Descendants("condition").ToArray();
                    Assert.Equal(2, conditions.Length);
                    Assert.Equal(subject, (string?)conditions[0].Attribute("value"));
                    Assert.Equal("0", (string?)conditions[1].Attribute("value"));
                }).Returns(Response(new EntityCollection()));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Empty(service.RetrieveTaskBySubject(subject).Entities);
        }

        /// <summary>
        /// 通用查詢與 Execute 兩條路徑都要驗整頁 ID；注入相同非空 ID 後必須整頁拒絕，
        /// 且原候選不被靜默刪減，讓上層保有可診斷的原始失敗。
        /// </summary>
        /// <param name="execute">使用 Execute 或 RetrieveMultiple。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultiQuery_DuplicateId_RejectsEntireCandidate(bool execute)
        {
            var candidate = new EntityCollection(new[] { new Entity("contact", RecordId), new Entity("contact", RecordId) });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => Run(service, execute));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 空列 ID 是 SDK 完整性故障；合法不同 ID 的同名列在相同入口均需保留兩筆。
        /// </summary>
        /// <param name="missingId">是否注入空 GUID。</param>
        /// <param name="execute">SDK 執行路徑。</param>
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void MultiQuery_StableIds_AreRequiredAndSameNamesArePreserved(bool missingId, bool execute)
        {
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId) { ["fullname"] = "Synthetic Same Name" },
                new Entity("contact", missingId ? Guid.Empty : new Guid("22222222-2222-4222-8222-222222222222"))
                    { ["fullname"] = "Synthetic Same Name" }
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                if (missingId) Assert.Throws<InvalidOperationException>(() => Run(service, execute));
                else Assert.Equal(2, Run(service, execute).Entities.Count);
            }
        }

        /// <summary>
        /// FetchXML 兩個通用入口不能略過根實體驗證；注入不同實體、重複或空 ID 時，
        /// 即使 SDK 已回完整候選仍須整頁拒絕，而且不能靜默修改原資料來掩蓋衝突。
        /// </summary>
        /// <param name="execute">經 Execute 或 RetrieveMultiple 送出同一 FetchExpression。</param>
        /// <param name="fault">合成的跨實體、重複或空識別故障。</param>
        [Theory]
        [InlineData(false, "entity")]
        [InlineData(true, "entity")]
        [InlineData(false, "duplicate")]
        [InlineData(true, "duplicate")]
        [InlineData(false, "empty")]
        [InlineData(true, "empty")]
        public void FetchQuery_InvalidRowIdentity_RejectsEntireCandidate(bool execute, string fault)
        {
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId),
                new Entity(fault == "entity" ? "account" : "contact",
                    fault == "empty" ? Guid.Empty : fault == "duplicate" ? RecordId
                        : new Guid("22222222-2222-4222-8222-222222222222"))
            });
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(candidate);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Throws<InvalidOperationException>(() => RunFetch(service, execute,
                    new FetchExpression("<fetch><entity name='contact'/></fetch>")));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 只讀辨識 FetchXML 根實體不能改寫原查詢；包含 link-entity 仍以 contact 為主資料列。
        /// 合法同名不同 ID、原 SDK 分頁旗標及 cookie 必須完整保留，不引入快取或額外 I/O。
        /// </summary>
        /// <param name="execute">原 SDK 執行入口。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FetchQuery_ValidRows_PreservesQueryAndPaging(bool execute)
        {
            var query = new FetchExpression("<?xml version='1.0'?><fetch><entity name='contact'>" +
                "<attribute name='contactid'/><link-entity name='account' from='accountid' to='parentcustomerid'/>" +
                "</entity></fetch>");
            var candidate = new EntityCollection(new[]
            {
                new Entity("contact", RecordId) { ["fullname"] = "Synthetic Same Name" },
                new Entity("contact", new Guid("22222222-2222-4222-8222-222222222222"))
                    { ["fullname"] = "Synthetic Same Name" }
            }) { MoreRecords = true, PagingCookie = "Synthetic cookie" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(query)).Returns(candidate);
            client.Setup(service => service.Execute(It.Is<RetrieveMultipleRequest>(request =>
                ReferenceEquals(request.Query, query)))).Returns(Response(candidate));
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                var result = RunFetch(service, execute, query);
                Assert.Same(candidate, result);
                Assert.Equal(2, result.Entities.Count);
                Assert.True(result.MoreRecords);
                Assert.Equal("Synthetic cookie", result.PagingCookie);
            }
        }

        /// <summary>
        /// 無根實體、雙根實體、非法名稱、壞 XML 與 DTD／外部實體都在 SDK I/O 前拒絕。
        /// DTD 使用不存在的合成 URI，產品必須禁用解析而不是嘗試存取檔案或網路；
        /// 預期輸入拒絕只回固定訊息，不把 XML 本文放進例外訊息。
        /// </summary>
        /// <param name="xml">不合法或帶外部實體的合成文件。</param>
        [Theory]
        [InlineData("")]
        [InlineData("<fetch/>")]
        [InlineData("<other><entity name='contact'/></other>")]
        [InlineData("<fetch><entity/></fetch>")]
        [InlineData("<fetch><entity name='bad&amp;name'/></fetch>")]
        [InlineData("<fetch><entity name='contact'/><entity name='account'/></fetch>")]
        [InlineData("<fetch xmlns='urn:synthetic'><entity name='contact'/></fetch>")]
        [InlineData("<fetch><entity name='contact'></fetch>")]
        [InlineData("<!DOCTYPE fetch [<!ENTITY synthetic SYSTEM 'file:///p1-must-not-read'>]><fetch><entity name='contact'>&synthetic;</entity></fetch>")]
        public void FetchQuery_InvalidXml_RejectsBeforeIo(string xml)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
                foreach (var execute in new[] { false, true })
                    Assert.Throws<ArgumentException>(() => RunFetch(service, execute, new FetchExpression(xml)));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Dispose 只停止新操作並釋放服務保存的借用參照，不關閉呼叫端 SDK。
        /// 以可計數的 IDisposable SDK 故障注入，關閉後不允許再做任何資料讀取。
        /// </summary>
        [Fact]
        public void Dispose_StopsNewOperationsWithoutDisposingBorrowedClient()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = client.As<IDisposable>();
            var service = new EntityQueryService(new object(), client.Object);
            service.Dispose();
            service.Dispose();
            Assert.Throws<ObjectDisposedException>(() => service.RetrieveEntity("contact", RecordId));
            disposable.Verify(value => value.Dispose(), Times.Never);
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 兩個已授權範圍交錯 1000 次必須只收到自己的 SDK 結果；八路固定上限避免測試無界 fan-out。
        /// 全部操作完成後 active 歸零，服務 Dispose 不釋放借入 client，也不留下背景工作。
        /// </summary>
        [Fact]
        public void NoCache_ParallelScopes_DoNotShareResultsAndDrain()
        {
            var otherId = new Guid("22222222-2222-4222-8222-222222222222");
            var first = new Mock<IOrganizationService>(MockBehavior.Strict);
            var second = new Mock<IOrganizationService>(MockBehavior.Strict);
            first.As<IDisposable>();
            second.As<IDisposable>();
            first.Setup(client => client.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", RecordId) { ["fullname"] = "Synthetic A" });
            second.Setup(client => client.Retrieve("contact", otherId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", otherId) { ["fullname"] = "Synthetic B" });
            var active = 0;
            var completed = 0;
            using (var a = new EntityQueryService(new object(), first.Object))
            using (var b = new EntityQueryService(new object(), second.Object))
                Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var isFirst = index % 2 == 0;
                        var row = (isFirst ? a : b).RetrieveEntityNoCache("contact", isFirst ? RecordId : otherId);
                        Assert.Equal(isFirst ? "Synthetic A" : "Synthetic B", row["fullname"]);
                        row["fullname"] = "Synthetic caller mutation";
                        Interlocked.Increment(ref completed);
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            Assert.Equal(0, active);
            Assert.Equal(1000, completed);
            first.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
            second.As<IDisposable>().Verify(client => client.Dispose(), Times.Never);
        }

        /// <summary>
        /// 依名稱查帳戶保留原 projection／TopCount／啟用條件；真正空頁才回空 GUID。
        /// 同名資料由既有單筆契約取首筆，而不是改寫或刪除候選中的其他合法 ID。
        /// </summary>
        /// <param name="empty">注入真正空頁或合法資料。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AccountNameQuery_PreservesProjectionAndEmptySemantics(bool empty)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryByAttribute>(query);
                    Assert.Equal("account", actual.EntityName);
                    Assert.Equal(1, actual.TopCount);
                    Assert.Equal(new[] { "accountid" }, actual.ColumnSet.Columns);
                    Assert.False(actual.ColumnSet.AllColumns);
                    Assert.Equal(new[] { "name", "statecode" }, actual.Attributes);
                    Assert.Equal(new object[] { "Synthetic Account", 0 }, actual.Values);
                }).Returns(empty ? new EntityCollection() : new EntityCollection(new[] { new Entity("account", RecordId) }));
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Equal(empty ? Guid.Empty : RecordId, service.RetrieveAccountByName("Synthetic Account"));
        }

        /// <summary>
        /// QueryExpression 的既有空 PageInfo 會補第一頁 5000，上層分頁旗標與 cookie 原樣保留。
        /// 以真 SDK 請求物件進替身，比對完整頁選項而非只檢查程式碼文字。
        /// </summary>
        [Fact]
        public void QueryExpression_NullPageInfo_PreservesDefaultAndResponsePaging()
        {
            var expected = new EntityCollection { MoreRecords = true, PagingCookie = "Synthetic cookie" };
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var actual = Assert.IsType<QueryExpression>(query);
                    Assert.Equal(5000, actual.PageInfo.Count);
                    Assert.Equal(1, actual.PageInfo.PageNumber);
                }).Returns(expected);
            using (var service = new EntityQueryService(new object(), client.Object))
                Assert.Same(expected, service.RetrieveMultiple(new QueryExpression("contact") { PageInfo = null }));
        }

        /// <summary>
        /// 未初始化、null Query、null Request 與缺 Query 的 Request 都必須明確失敗，不能回空資料。
        /// 這些預期拒絕不發 LINE，借入 SDK 替身不應收到任何 I/O。
        /// </summary>
        [Fact]
        public void InvalidOperationInputs_RejectWithoutSdkIo()
        {
            using (var uninitialized = new EntityQueryService())
                Assert.Throws<InvalidOperationException>(() => uninitialized.RetrieveEntity("contact", RecordId));
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            using (var service = new EntityQueryService(new object(), client.Object))
            {
                Assert.Throws<ArgumentNullException>(() => service.RetrieveMultiple(null!));
                Assert.Throws<ArgumentNullException>(() => service.ExecuteRetrieveMultiple(null!));
                Assert.Throws<ArgumentException>(() => service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest()));
                client.Setup(value => value.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection());
                Assert.Null(service.RetrieveEntityByField("contact", "fullname", "Synthetic absent contact"));
            }
            client.Verify(value => value.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            client.VerifyNoOtherCalls();
        }

        /// <summary>建立操作局部 SDK 回應，不保留於 static 或共用快取。</summary>
        private static RetrieveMultipleResponse Response(EntityCollection candidate)
        {
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            return response;
        }

        /// <summary>由測試固定路徑呼叫真實服務，不在替身重做產品驗證邏輯。</summary>
        private static EntityCollection Run(EntityQueryService service, bool execute) => execute
            ? service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = new QueryByAttribute("contact") })
            : service.RetrieveMultiple(new QueryByAttribute("contact"));

        /// <summary>向兩條真實產品路徑傳入同一查詢；替身不負責解析或驗證 XML。</summary>
        private static EntityCollection RunFetch(EntityQueryService service, bool execute, FetchExpression query) => execute
            ? service.ExecuteRetrieveMultiple(new RetrieveMultipleRequest { Query = query })
            : service.RetrieveMultiple(query);
    }
}
