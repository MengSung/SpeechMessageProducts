#if NET10_0
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>逐入口保護完整發布、正式身份、批次故障保真與取消分類；所有失敗均由受測實作觀察合成SDK邊界。</summary>
    public sealed class ListSafetyTests
    {
        /// <summary>十二個List讀入口加兩個Marketing投影分開故障，確保每個public入口都執行完整頁驗證。</summary>
        public static IEnumerable<object[]> InvalidPages() => ReadEntries.SelectMany(entry =>
            new[] { "null", "empty", "duplicate", "table", "budget" }.Select(mode => new object[] { entry, mode }));

        /// <summary>任何候選身份故障必須拒絕整頁；不得回首筆、跳過壞列或用字典覆寫。</summary>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public void EveryRead_RejectsInvalidCandidate(string entry, string mode)
        {
            var sdk = Source(query => Invalid(Table(query), mode));
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns((QueryBase _) => Invalid("listmember", mode));
            var service = new ListService(new object(), sdk.Object);
            var marketing = new MarketingListService(repository.Object, sdk.Object);
            Assert.Throws<InvalidOperationException>(() => Read(service, marketing, entry));
        }

        /// <summary>XML值含特殊符號仍只是一個condition值，不可增加link或改變結構。</summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void NameInputs_AreEncoded(bool leader)
        {
            var sdk = Source(query =>
            {
                var xml = XElement.Parse(Assert.IsType<FetchExpression>(query).Query);
                var condition = xml.Descendants("condition").Single(node =>
                    (string?)node.Attribute("attribute") == (leader ? "new_contact_race_leager_list" : "fullname"));
                Assert.Equal("a<&\"'", (string?)condition.Attribute(leader ? "uiname" : "value"));
                Assert.Equal(leader ? 0 : 2, xml.Descendants("link-entity").Count());
                return new EntityCollection();
            });
            var service = new ListService(new object(), sdk.Object);
            Assert.Empty((leader ? service.RetrieveListByRacerLeader("a<&\"'", ListCharacterizationTests.ListId.ToString("D"))
                : service.RetrieveListByContact("a<&\"'")).Entities);
        }

        /// <summary>四類名單批次故障不再吞錯回部分成功；原Timeout必須仍可由host辨識，後續批次不得啟動。</summary>
        [Theory]
        [InlineData("list-add")] [InlineData("list-sdk")] [InlineData("list-remove")]
        [InlineData("marketing-add")] [InlineData("marketing-sdk")] [InlineData("marketing-remove")]
        public async Task BatchFailure_DoesNotReturnPartialSuccess(string entry)
        {
            var calls = 0;
            var failure = new TimeoutException("synthetic-private");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                calls++;
                if (calls == 2) throw failure;
                return request is ExecuteMultipleRequest ? new ExecuteMultipleResponse
                { Results = { ["Responses"] = new ExecuteMultipleResponseItemCollection() } } : new OrganizationResponse();
            });
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => Write(entry, sdk.Object, Members(), 1));
            Assert.Contains(failure, Chain(exception)); Assert.Equal(2, calls);
            Assert.DoesNotContain("synthetic-private", exception.Message);
        }

        /// <summary>ExecuteMultiple ReturnResponses=false仍回Fault；只要有Fault即拒絕成功數並保留真實OrganizationServiceFault。</summary>
        [Fact]
        public async Task RemoveBatch_ChecksSdkFaults()
        {
            var fault = new OrganizationServiceFault { ErrorCode = -1, Message = "synthetic-private" };
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns(new ExecuteMultipleResponse
            {
                Results = { ["Responses"] = new ExecuteMultipleResponseItemCollection
                { new ExecuteMultipleResponseItem { RequestIndex = 0, Fault = fault } } }
            });
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => Write("list-remove", sdk.Object, Members(), 2));
            Assert.Contains(Chain(failure), item => item is System.ServiceModel.FaultException<OrganizationServiceFault> actual
                && ReferenceEquals(fault, actual.Detail));
            Assert.DoesNotContain("synthetic-private", failure.Message);
        }

        /// <summary>輸入含零ID、重複ID、過量及超批次上限在I/O前拒絕；整份snapshot驗完才准寫入。</summary>
        [Theory]
        [InlineData("empty")] [InlineData("duplicate")] [InlineData("budget")] [InlineData("batch")]
        public async Task BatchValidation_RejectsBeforeAnyIo(string mode)
        {
            foreach (var entry in WriteEntries)
            {
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                var input = mode == "empty" ? new List<Guid> { Guid.Empty }
                    : mode == "duplicate" ? new List<Guid> { ListCharacterizationTests.FirstId, ListCharacterizationTests.FirstId }
                    : mode == "budget" ? Enumerable.Range(1, 5001).Select(index => new Guid(index, 0, 0, new byte[8])).ToList()
                    : Members();
                await Assert.ThrowsAnyAsync<ArgumentException>(() => Write(entry, sdk.Object, input,
                    mode == "batch" ? 1001 : mode == "budget" ? 1000 : 2));
                sdk.VerifyNoOtherCalls();
            }
        }

        /// <summary>caller未取消而SDK丟OCE必須是故障Task，不能冒充正常取消而漏掉共用通知。</summary>
        [Theory]
        [InlineData("list-add")] [InlineData("list-sdk")] [InlineData("list-remove")]
        [InlineData("marketing-add")] [InlineData("marketing-sdk")] [InlineData("marketing-remove")]
        public async Task InternalCancellation_RemainsFaulted(string entry)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var failure = new OperationCanceledException("synthetic-private");
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Throws(failure);
            var task = Write(entry, sdk.Object, Members(), 2);
            var actual = await Assert.ThrowsAnyAsync<Exception>(() => task);
            Assert.True(task.IsFaulted); Assert.False(task.IsCanceled);
            Assert.Contains(failure, Chain(actual));
        }

        /// <summary>caller預先取消不准I/O；取消由明確token決定，不能包成一般系統錯誤。</summary>
        [Theory]
        [InlineData("list-add")] [InlineData("list-sdk")] [InlineData("list-remove")]
        [InlineData("marketing-add")] [InlineData("marketing-sdk")] [InlineData("marketing-remove")]
        public async Task CallerCancellation_IsCanceledWithoutIo(string entry)
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var task = Write(entry, sdk.Object, Members(), 2, cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsCanceled); sdk.VerifyNoOtherCalls();
        }

        /// <summary>ArrayList投影本身的成員ID也必須非空且唯一；不同listmember列不能偷偷發布同個成員或漏欄位。</summary>
        [Theory]
        [InlineData("missing")] [InlineData("duplicate")] [InlineData("empty")] [InlineData("type")]
        public void MemberProjection_RejectsInvalidIdentity(string mode)
        {
            var sdk = Source(_ => new EntityCollection(new[] { Row(ListCharacterizationTests.FirstId), Row(ListCharacterizationTests.SecondId) }));
            var service = new ListService(new object(), sdk.Object);
            Assert.Throws<InvalidOperationException>(() => service.GetAllMemberDataFromList(ListCharacterizationTests.ListId));
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(new EntityCollection(new[] { Row(ListCharacterizationTests.FirstId), Row(ListCharacterizationTests.SecondId) }));
            Assert.Throws<InvalidOperationException>(() => new MarketingListService(repository.Object, sdk.Object)
                .GetAllMemberData(ListCharacterizationTests.ListId));

            Entity Row(Guid id)
            {
                var row = new Entity("listmember", id);
                if (mode != "missing") row["entityid"] = mode == "type" ? "invalid"
                    : mode == "empty" ? Guid.Empty : ListCharacterizationTests.FirstId;
                return row;
            }
        }

        /// <summary>代表所有讀入口；不使用字串名稱作資料身份。</summary>
        internal static readonly string[] ReadEntries = { "member", "member-service", "member-proxy", "dynamic", "dynamic-service",
            "dynamic-proxy", "lists", "small", "contact", "leader", "by-contact", "by-name", "marketing", "marketing-members" };
        /// <summary>六個非同步入口的固定集合，供取消／故障／drain逐項測試。</summary>
        internal static readonly string[] WriteEntries = { "list-add", "list-sdk", "list-remove", "marketing-add", "marketing-sdk", "marketing-remove" };

        /// <summary>只建立合成輸入，不從外部環境讀取任何scope。</summary>
        internal static List<Guid> Members() => new List<Guid> { ListCharacterizationTests.FirstId, ListCharacterizationTests.SecondId, ListCharacterizationTests.ListId };

        /// <summary>實際呼叫public入口，測試helper不實作任何產品驗證。</summary>
        internal static Task<int> Write(string entry, IOrganizationService sdk, List<Guid> input, int batchSize, CancellationToken token = default)
        {
            var list = new ListService(new object(), sdk);
            var marketing = new MarketingListService(new Mock<IEntityRepository>(MockBehavior.Strict).Object, sdk);
            return entry switch
            {
                "list-add" => list.AddMembersAsync(ListCharacterizationTests.ListId, input, batchSize, token),
                "list-sdk" => list.AddMembersUsingSdkAsync(ListCharacterizationTests.ListId, input, sdk, batchSize, token),
                "list-remove" => list.RemoveMembersAsync(ListCharacterizationTests.ListId, input, batchSize, token),
                "marketing-add" => marketing.AddMembersAsync(ListCharacterizationTests.ListId, input, batchSize, token),
                "marketing-sdk" => marketing.AddMembersUsingSdkAsync(ListCharacterizationTests.ListId, input, sdk, batchSize, token),
                _ => marketing.RemoveMembersAsync(ListCharacterizationTests.ListId, input, batchSize, token)
            };
        }

        /// <summary>逐項呼叫真實讀取入口，不取代query組裝。</summary>
        internal static object? Read(ListService list, MarketingListService marketing, string entry) => entry switch
        {
            "member" => list.RetrieveMemberListCollectionByListId(ListCharacterizationTests.ListId),
            "member-service" => list.RetrieveMemberListCollectionByListIdUsingService(GetSdk(list), ListCharacterizationTests.ListId),
            "member-proxy" => list.RetrieveMemberListCollectionByListIdUsingProxy(GetSdk(list), ListCharacterizationTests.ListId),
            "dynamic" => list.RetrieveDynamicMemberList(ListCharacterizationTests.ListId),
            "dynamic-service" => list.RetrieveDynamicMemberListUsingService(GetSdk(list), ListCharacterizationTests.ListId),
            "dynamic-proxy" => list.RetrieveDynamicMemberListUsingProxy(GetSdk(list), ListCharacterizationTests.ListId),
            "lists" => list.RetrieveLists(), "small" => list.RetrieveSmallGroupLists(),
            "contact" => list.RetrieveListByContact("Synthetic"),
            "leader" => list.RetrieveListByRacerLeader("Synthetic", ListCharacterizationTests.ListId.ToString("D")),
            "by-contact" => list.QueryListByContactId(ListCharacterizationTests.ListId, "contact_list"),
            "by-name" => list.RetrieveListEntityByName("Synthetic"),
            "marketing" => marketing.RetrieveMembersByListId(ListCharacterizationTests.ListId),
            _ => marketing.GetAllMemberData(ListCharacterizationTests.ListId)
        };

        /// <summary>僅取測試自有借用SDK供外部overload；不引入產品測試hook或權限。</summary>
        private static IOrganizationService GetSdk(ListService list) =>
            (IOrganizationService)typeof(ListService).GetField("_organizationService",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(list)!;

        /// <summary>SDK只回合成查詢頁；動態名單的讀取仍經產品核對正式ID與Fetch根。</summary>
        internal static Mock<IOrganizationService> Source(Func<QueryBase, EntityCollection> read)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(read);
            sdk.Setup(item => item.Retrieve("list", It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Returns((string _, Guid id, ColumnSet _) =>
                new Entity("list", id) { ["type"] = false, ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            return sdk;
        }

        /// <summary>只決定故障合成資料的根表，從不修改受測query。</summary>
        internal static string Table(QueryBase query) => query is QueryByAttribute by ? by.EntityName
            : query is QueryExpression expression ? expression.EntityName
            : (string)XElement.Parse(((FetchExpression)query).Query).Element("entity")!.Attribute("name")!;

        /// <summary>注入null、空ID、重複ID、錯表及超量，全部在產品處理前建立。</summary>
        private static EntityCollection Invalid(string table, string mode)
        {
            if (mode == "null") return null!;
            var row = new Entity(mode == "table" ? "unrelated" : table, mode == "empty" ? Guid.Empty : ListCharacterizationTests.FirstId);
            return new EntityCollection(mode == "budget"
                ? Enumerable.Range(1, 5001).Select(index => new Entity(table, new Guid(index, 0, 0, new byte[8]))).ToArray()
                : mode == "duplicate" ? new[] { row, row } : new[] { row });
        }

        /// <summary>沿InnerException核對原例外實體，確保沒有丟棄失敗型別或堆疊。</summary>
        private static IEnumerable<Exception> Chain(Exception failure)
        {
            for (Exception? item = failure; item != null; item = item.InnerException) yield return item;
        }
    }
}
#endif
