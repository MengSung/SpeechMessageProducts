/*
 * 檔案整體說明
 *
 * 目的與定位：在原 host 先鎖定 Repository 六入口的有效 SDK 邊界，再由相同測試保護 Core 搬移。 全部識別與內容為合成值；只替代外部 SDK，不假稱 D365、授權或資料庫唯一鍵已驗收。
 *
 * 主要功能與流程：
 * 本檔宣告 RepositoryCharacterizationTests。
 * - 寫入參數與既有黃金檔一致，原 Entity 不改動，每次只呼叫一次 SDK。
 * - 省略或傳 null 欄位仍讀 AllColumns；明確欄位實例原樣交 SDK，回應不被重建或保留。
 * - 三種合法查詢與完整分頁資訊原樣通過；相同顯示名稱的不同 ID 都保留。
 * - 指派仍使用 systemuser 與原目標資料庫 ID；請求與原 golden 相同且不寫回 Entity。
 * 測試案例入口包含 Crud_ValidInput_PreservesGoldenRequest、Retrieve_PreservesColumnSetAndResponse、
 * RetrieveMultiple_PreservesQueryRowsAndPaging、AssignOwner_PreservesGoldenRequest、
 * ExistingValidation_RejectsBeforeSdkWithOriginalParameterNames、Describe。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在原 host 先鎖定 Repository 六入口的有效 SDK 邊界，再由相同測試保護 Core 搬移。
    /// 全部識別與內容為合成值；只替代外部 SDK，不假稱 D365、授權或資料庫唯一鍵已驗收。
    /// </summary>
    public sealed class RepositoryCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OwnerId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>寫入參數與既有黃金檔一致，原 Entity 不改動，每次只呼叫一次 SDK。</summary>
        /// <param name="operation">固定的合成寫入種類，不從使用者輸入產生。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        public void Crud_ValidInput_PreservesGoldenRequest(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", RecordId) { ["fullname"] = "合成同名" };
            XElement? actual = null;
            if (operation == "create")
                sdk.Setup(s => s.Create(entity)).Callback<Entity>(e => actual = Describe(operation, e)).Returns(RecordId);
            else if (operation == "update")
                sdk.Setup(s => s.Update(entity)).Callback<Entity>(e => actual = Describe(operation, e));
            else
                sdk.Setup(s => s.Delete("contact", RecordId))
                    .Callback<string, Guid>((name, id) => actual = Describe(operation, new Entity(name, id)));
            var repository = new EntityRepository(sdk.Object);
            if (operation == "create") Assert.Equal(RecordId, repository.Create(entity));
            else if (operation == "update") repository.Update(entity);
            else repository.Delete("contact", RecordId);
            Assert.NotNull(actual);
            GoldenQueryAssertions.Equal("mutation-" + operation + ".xml", actual!.ToString());
            Assert.Equal(RecordId, entity.Id);
            Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
            sdk.VerifyAll();
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>省略或傳 null 欄位仍讀 AllColumns；明確欄位實例原樣交 SDK，回應不被重建或保留。</summary>
        /// <param name="mode">省略、null、指定欄位或明確全欄位。</param>
        [Theory]
        [InlineData("omitted")]
        [InlineData("null")]
        [InlineData("explicit")]
        [InlineData("all")]
        public void Retrieve_PreservesColumnSetAndResponse(string mode)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var columns = mode == "explicit" ? new ColumnSet("fullname") : mode == "all" ? new ColumnSet(true) : null;
            var expected = new Entity("contact", RecordId) { ["fullname"] = "合成同名" };
            ColumnSet? actual = null;
            sdk.Setup(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Callback<string, Guid, ColumnSet>((_, _, sent) => actual = sent).Returns(expected);
            var repository = new EntityRepository(sdk.Object);
            var result = mode == "omitted" ? repository.Retrieve("contact", RecordId)
                : repository.Retrieve("contact", RecordId, columns!);
            Assert.Same(expected, result);
            Assert.NotNull(actual);
            if (columns != null) Assert.Same(columns, actual);
            else Assert.True(actual!.AllColumns);
            sdk.Verify(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>三種合法查詢與完整分頁資訊原樣通過；相同顯示名稱的不同 ID 都保留。</summary>
        /// <param name="kind">SDK 支援的固定查詢種類。</param>
        [Theory]
        [InlineData("expression")]
        [InlineData("attributes")]
        [InlineData("fetch")]
        public void RetrieveMultiple_PreservesQueryRowsAndPaging(string kind)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var paging = new PagingInfo { Count = 23, PageNumber = 4, PagingCookie = "synthetic-cookie" };
            QueryBase query = kind == "expression" ? new QueryExpression("contact")
                { ColumnSet = new ColumnSet("fullname"), PageInfo = paging }
                : kind == "attributes" ? new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) }
                : new FetchExpression("<fetch page='4' count='23'><entity name='contact'><attribute name='fullname'/></entity></fetch>");
            var expected = new EntityCollection
            {
                MoreRecords = true, PagingCookie = "synthetic-next", TotalRecordCount = 63
            };
            expected.Entities.Add(new Entity("contact", RecordId) { ["fullname"] = "合成同名" });
            expected.Entities.Add(new Entity("contact", OwnerId) { ["fullname"] = "合成同名" });
            sdk.Setup(s => s.RetrieveMultiple(query)).Returns(expected);
            var result = new EntityRepository(sdk.Object).RetrieveMultiple(query);
            Assert.Same(expected, result);
            Assert.Equal(2, result.Entities.Count);
            Assert.Equal(RecordId, result.Entities[0].Id);
            Assert.Equal(OwnerId, result.Entities[1].Id);
            Assert.True(result.MoreRecords);
            Assert.Equal("synthetic-next", result.PagingCookie);
            Assert.Equal(63, result.TotalRecordCount);
            if (query is QueryExpression expression) Assert.Same(paging, expression.PageInfo);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>指派仍使用 systemuser 與原目標資料庫 ID；請求與原 golden 相同且不寫回 Entity。</summary>
        [Fact]
        public void AssignOwner_PreservesGoldenRequest()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            AssignRequest? actual = null;
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>()))
                .Callback<OrganizationRequest>(request => actual = (AssignRequest)request).Returns(new AssignResponse());
            var entity = new Entity("contact", RecordId);
            new EntityRepository(sdk.Object).AssignOwner("contact", entity, OwnerId);
            Assert.NotNull(actual);
            GoldenQueryAssertions.Equal("mutation-assign.xml", new XElement("assign",
                new XElement("target", new XAttribute("name", actual!.Target.LogicalName), new XAttribute("id", actual.Target.Id)),
                new XElement("assignee", new XAttribute("name", actual.Assignee.LogicalName), new XAttribute("id", actual.Assignee.Id))).ToString());
            Assert.False(entity.Contains("ownerid"));
            sdk.Verify(s => s.Execute(It.IsAny<AssignRequest>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>既有參數拒絕的位置、型別及名稱不變；特別保護 Update 不放寬為純替代鍵更新。</summary>
        [Fact]
        public void ExistingValidation_RejectsBeforeSdkWithOriginalParameterNames()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var repository = new EntityRepository(sdk.Object);
            Assert.Equal("organizationService", Assert.Throws<ArgumentNullException>(() => new EntityRepository(null!)).ParamName);
            Assert.Equal("entity", Assert.Throws<ArgumentNullException>(() => repository.Create(null!)).ParamName);
            Assert.Equal("entity", Assert.Throws<ArgumentNullException>(() => repository.Update(null!)).ParamName);
            var alternateKeyOnly = new Entity("contact");
            alternateKeyOnly.KeyAttributes["new_externalid"] = "synthetic-event";
            Assert.Equal("entity", Assert.Throws<ArgumentException>(() => repository.Update(alternateKeyOnly)).ParamName);
            Assert.Equal("entityName", Assert.Throws<ArgumentException>(() => repository.Delete(" ", RecordId)).ParamName);
            Assert.Equal("entityId", Assert.Throws<ArgumentException>(() => repository.Delete("contact", Guid.Empty)).ParamName);
            Assert.Equal("entityName", Assert.Throws<ArgumentException>(() => repository.Retrieve(" ", RecordId)).ParamName);
            Assert.Equal("entityId", Assert.Throws<ArgumentException>(() => repository.Retrieve("contact", Guid.Empty)).ParamName);
            Assert.Equal("query", Assert.Throws<ArgumentNullException>(() => repository.RetrieveMultiple(null!)).ParamName);
            Assert.Equal("entityName", Assert.Throws<ArgumentException>(() => repository.AssignOwner(" ", null!, Guid.Empty)).ParamName);
            Assert.Equal("entity", Assert.Throws<ArgumentNullException>(() => repository.AssignOwner("contact", null!, Guid.Empty)).ParamName);
            Assert.Equal("newOwnerId", Assert.Throws<ArgumentException>(() => repository.AssignOwner("contact", new Entity("contact", RecordId), Guid.Empty)).ParamName);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只投影合成 fullname 與固定識別；不序列化 SDK、憑證或任意使用者資料。</summary>
        private static XElement Describe(string operation, Entity entity)
        {
            var xml = new XElement(operation, new XAttribute("name", entity.LogicalName), new XAttribute("id", entity.Id));
            if (entity.Contains("fullname")) xml.Add(new XElement("fullname", entity.GetAttributeValue<string>("fullname")));
            return xml;
        }
    }
}
