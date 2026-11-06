/*
 * 檔案整體說明
 *
 * 目的與定位：名單兩服務原host特性；完整Fetch、SDK路徑、成員投影及批次分割先跑綠，再進行安全修補與搬移。
 *
 * 主要功能與流程：
 * 本檔宣告 ListCharacterizationTests。
 * - 四種原Fetch各有獨立golden，保留降冪、未含statecode、退出排除及原link位置。
 * - 內部與兩外部靜態入口保持QueryByAttribute欄位和GUID值；external null維持舊空集合語義。
 * - 三動態入口先讀原list.query，再由相同借用SDK直接RetrieveMultiple；不換成Execute或第二個scope。
 * - 保留contact查listmember的單根條件及by-name第一筆契約；同名異ID不被合併，single只選原首筆。
 * 測試案例入口包含 OriginalHost_PreservesCompleteFetch、OriginalHost_PreservesStaticMemberQuery、
 * OriginalHost_PreservesDynamicRoutes、OriginalHost_PreservesContactAndNameQueries、
 * OriginalHost_PreservesMemberProjection、OriginalHost_PreservesSynchronousWriteRequests、
 * OriginalHost_PreservesListBatchMessages、OriginalHost_PreservesMarketingRoutesAndResults；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 ToolUtility.Tests/DataOperations/ListLifecycleTests.cs、ToolUtility.Tests/DataOperations/ListSafetyTests.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System.Collections;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>名單兩服務原host特性；完整Fetch、SDK路徑、成員投影及批次分割先跑綠，再進行安全修補與搬移。</summary>
    public sealed class ListCharacterizationTests
    {
        /// <summary>四種原Fetch各有獨立golden，保留降冪、未含statecode、退出排除及原link位置。</summary>
        [Theory]
        [InlineData("lists")] [InlineData("small")] [InlineData("contact")] [InlineData("leader")]
        public void OriginalHost_PreservesCompleteFetch(string entry)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                GoldenQueryAssertions.Equal("list-" + entry + ".xml", Assert.IsType<FetchExpression>(query).Query);
                return Page("list");
            });
            var service = new ListService(new object(), sdk.Object);
            var result = entry switch
            {
                "lists" => service.RetrieveLists(), "small" => service.RetrieveSmallGroupLists(),
                "contact" => service.RetrieveListByContact("Synthetic"),
                _ => service.RetrieveListByRacerLeader("Synthetic", ListId.ToString("D"))
            };
            Assert.Equal(new[] { FirstId, SecondId }, result.Entities.Select(row => row.Id));
            Assert.All(result.Entities, row => Assert.Equal("same-name", row["new_name"]));
            Assert.True(result.MoreRecords); Assert.Equal("synthetic", result.PagingCookie);
        }

        /// <summary>內部與兩外部靜態入口保持QueryByAttribute欄位和GUID值；external null維持舊空集合語義。</summary>
        [Theory]
        [InlineData("internal")] [InlineData("service")] [InlineData("proxy")]
        public void OriginalHost_PreservesStaticMemberQuery(string entry)
        {
            var internalSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var externalSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var target = entry == "internal" ? internalSdk : externalSdk;
            target.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryByAttribute>(input);
                Assert.Equal("listmember", query.EntityName);
                Assert.Equal(new[] { "listmemberid", "entityid", "listid" }, query.ColumnSet.Columns);
                Assert.Equal(new[] { "listid" }, query.Attributes); Assert.Equal(ListId, Assert.Single(query.Values));
                return Page("listmember");
            });
            var service = new ListService(new object(), internalSdk.Object);
            var page = entry switch
            {
                "internal" => service.RetrieveMemberListCollectionByListId(ListId),
                "service" => service.RetrieveMemberListCollectionByListIdUsingService(externalSdk.Object, ListId),
                _ => service.RetrieveMemberListCollectionByListIdUsingProxy(externalSdk.Object, ListId)
            };
            Assert.Equal(2, page.Entities.Count);
            if (entry != "internal") internalSdk.VerifyNoOtherCalls();
            Assert.Empty(service.RetrieveMemberListCollectionByListIdUsingService(null!, ListId).Entities);
            Assert.Empty(service.RetrieveMemberListCollectionByListIdUsingProxy(null!, ListId).Entities);
        }

        /// <summary>三動態入口先讀原list.query，再由相同借用SDK直接RetrieveMultiple；不換成Execute或第二個scope。</summary>
        [Theory]
        [InlineData("internal")] [InlineData("service")] [InlineData("proxy")]
        public void OriginalHost_PreservesDynamicRoutes(string entry)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Retrieve("list", ListId, It.IsAny<ColumnSet>())).Returns((string _, Guid id, ColumnSet columns) =>
            {
                Assert.Equal(new[] { "query" }, columns.Columns);
                return new Entity("list", id) { ["query"] = RepositoryQueryCharacterizationTests.Fetch };
            });
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                GoldenQueryAssertions.Equal("complex-dynamic.xml", Assert.IsType<FetchExpression>(query).Query);
                return Page("contact");
            });
            var service = new ListService(new object(), sdk.Object);
            var page = entry switch
            {
                "internal" => service.RetrieveDynamicMemberList(ListId),
                "service" => service.RetrieveDynamicMemberListUsingService(sdk.Object, ListId),
                _ => service.RetrieveDynamicMemberListUsingProxy(sdk.Object, ListId)
            };
            Assert.Equal(2, page.Entities.Count);
            Assert.Empty(service.RetrieveDynamicMemberListUsingService(null!, ListId).Entities);
            Assert.Empty(service.RetrieveDynamicMemberListUsingProxy(null!, ListId).Entities);
        }

        /// <summary>保留contact查listmember的單根條件及by-name第一筆契約；同名異ID不被合併，single只選原首筆。</summary>
        [Fact]
        public void OriginalHost_PreservesContactAndNameQueries()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                if (query is QueryExpression expression)
                {
                    Assert.Equal("listmember", expression.EntityName); Assert.True(expression.ColumnSet.AllColumns);
                    var condition = Assert.Single(expression.Criteria.Conditions);
                    Assert.Equal("entityid", condition.AttributeName); Assert.Equal(ConditionOperator.Equal, condition.Operator);
                    Assert.Equal(ListId, Assert.Single(condition.Values));
                    return Page("listmember");
                }
                var attributes = Assert.IsType<QueryByAttribute>(query);
                Assert.Equal("list", attributes.EntityName);
                Assert.Equal(new[] { "listmemberid", "entityid", "listid" }, attributes.ColumnSet.Columns);
                Assert.Equal(new[] { "listname", "statecode" }, attributes.Attributes);
                Assert.Equal(new object[] { "Synthetic", 0 }, attributes.Values);
                return Page("list");
            });
            var service = new ListService(new object(), sdk.Object);
            Assert.Equal(2, service.QueryListByContactId(ListId, "contact_list").Entities.Count);
            Assert.Equal(FirstId, service.RetrieveListEntityByName("Synthetic")!.Id);
        }

        /// <summary>ArrayList仍回正式成員GUID；靜態取EntityReference，動態取contactid，兩筆同名都保留。</summary>
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void OriginalHost_PreservesMemberProjection(bool dynamicList)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Retrieve("list", ListId, It.IsAny<ColumnSet>()))
                .Returns(new Entity("list", ListId) { ["type"] = dynamicList, ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase _) =>
                new EntityCollection(new[] { Row(FirstId), Row(SecondId) }));
            var result = new ListService(new object(), sdk.Object).GetAllMemberDataFromList(ListId);
            Assert.Equal(new[] { FirstId, SecondId }, result.Cast<Guid>());

            Entity Row(Guid id) => dynamicList ? new Entity("contact", id) { ["contactid"] = id }
                : new Entity("listmember", Guid.NewGuid()) { ["entityid"] = new EntityReference("contact", id) };
        }

        /// <summary>同步四寫入保持原SDK訊息及輸入順序，不用模擬Create/Delete繞過關聯原生唯一性。</summary>
        [Fact]
        public void OriginalHost_PreservesSynchronousWriteRequests()
        {
            var requests = new List<OrganizationRequest>();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            { requests.Add(request); return new OrganizationResponse(); });
            var service = new ListService(new object(), sdk.Object);
            service.AddMembers(ListId, new List<Guid> { FirstId, SecondId });
            service.AddMembersUsingSdk(ListId, new List<Guid> { FirstId, SecondId }, sdk.Object);
            service.RemoveMember(ListId, FirstId); service.RemoveMemberUsingSdk(ListId, FirstId, sdk.Object);
            Assert.Equal(4, requests.Count);
            foreach (var request in requests.Take(2).Cast<AddListMembersListRequest>())
            { Assert.Equal(ListId, request.ListId); Assert.Equal(new[] { FirstId, SecondId }, request.MemberIds); }
            foreach (var request in requests.Skip(2).Cast<RemoveMemberListRequest>())
            { Assert.Equal(ListId, request.ListId); Assert.Equal(FirstId, request.EntityId); }
        }

        /// <summary>保留三個List非同步入口的批次切分與訊息形狀；成功數必須涵蓋全部合成輸入。</summary>
        [Theory]
        [InlineData("add")] [InlineData("sdk")] [InlineData("remove")]
        public async Task OriginalHost_PreservesListBatchMessages(string entry)
        {
            var sizes = new List<int>();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                if (request is AddListMembersListRequest add)
                { Assert.Equal(ListId, add.ListId); sizes.Add(add.MemberIds.Length); return new AddListMembersListResponse(); }
                var multi = Assert.IsType<ExecuteMultipleRequest>(request);
                Assert.True(multi.Settings.ContinueOnError); Assert.False(multi.Settings.ReturnResponses);
                Assert.All(multi.Requests, value => Assert.Equal(ListId, Assert.IsType<RemoveMemberListRequest>(value).ListId));
                sizes.Add(multi.Requests.Count);
                return new ExecuteMultipleResponse { Results = { ["Responses"] = new ExecuteMultipleResponseItemCollection() } };
            });
            var service = new ListService(new object(), sdk.Object);
            var members = new List<Guid> { FirstId, SecondId, ListId };
            var result = entry == "add" ? await service.AddMembersAsync(ListId, members, 2)
                : entry == "sdk" ? await service.AddMembersUsingSdkAsync(ListId, members, sdk.Object, 2)
                : await service.RemoveMembersAsync(ListId, members, 2);
            Assert.Equal(3, result); Assert.Equal(new[] { 2, 1 }, sizes);
        }

        /// <summary>Marketing保留repository查詢而非改SDKroute，entityid的Guid投影與三類寫入都保持原訊息。</summary>
        [Fact]
        public async Task OriginalHost_PreservesMarketingRoutesAndResults()
        {
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal("listmember", expression.EntityName); Assert.True(expression.ColumnSet.AllColumns);
                var condition = Assert.Single(expression.Criteria.Conditions);
                Assert.Equal("listid", condition.AttributeName); Assert.Equal(ListId, Assert.Single(condition.Values));
                return new EntityCollection(new[] { new Entity("listmember", FirstId) { ["entityid"] = SecondId } });
            });
            var requests = new System.Collections.Concurrent.ConcurrentBag<OrganizationRequest>();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            { requests.Add(request); return new OrganizationResponse(); });
            var service = new MarketingListService(repository.Object, sdk.Object);
            Assert.Equal(FirstId, Assert.Single(service.RetrieveMembersByListId(ListId).Entities).Id);
            Assert.Equal(SecondId, Assert.Single(service.GetAllMemberData(ListId).Cast<Guid>()));
            service.AddMember(ListId, FirstId); service.RemoveMember(ListId, FirstId);
            Assert.Equal(2, await service.AddMembersAsync(ListId, new List<Guid> { FirstId, SecondId }, 1));
            Assert.Equal(2, await service.RemoveMembersAsync(ListId, new List<Guid> { FirstId, SecondId }, 1));
            Assert.Equal(2, await service.AddMembersUsingSdkAsync(ListId, new List<Guid> { FirstId, SecondId }, sdk.Object, 1));
            Assert.Equal(3, requests.OfType<AddMemberListRequest>().Count());
            Assert.Equal(3, requests.OfType<RemoveMemberListRequest>().Count());
            Assert.Equal(2, requests.OfType<AddListMembersListRequest>().Count());
        }

        /// <summary>每次建立全新同名異ID單頁，metadata只供單頁契約使用。</summary>
        internal static EntityCollection Page(string table) => new EntityCollection(new[]
        {
            new Entity(table, FirstId) { ["new_name"] = "same-name" }, new Entity(table, SecondId) { ["new_name"] = "same-name" }
        }) { MoreRecords = true, PagingCookie = "synthetic" };

        /// <summary>合成名單範圍，沒有外部資料或授權意義。</summary>
        internal static readonly Guid ListId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        /// <summary>第一筆正式合成資料庫ID。</summary>
        internal static readonly Guid FirstId = ContactCharacterizationTests.FirstId;
        /// <summary>第二筆正式合成資料庫ID，顯示內容可以完全相同。</summary>
        internal static readonly Guid SecondId = ContactCharacterizationTests.SecondId;
    }
}
