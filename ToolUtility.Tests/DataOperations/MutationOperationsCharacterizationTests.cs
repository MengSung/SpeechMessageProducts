using System;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.OwnerOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 搬移前鎖定 CRUD 與負責人操作的正常 SDK 邊界；所有識別與內容皆為固定合成值。
    /// 替身只記錄實際產品送出的參數，不連 D365、不代替業務授權或宣稱資料庫冪等。
    /// </summary>
    public sealed class MutationOperationsCharacterizationTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OwnerId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>三個寫入入口必須只呼叫一次 SDK 且不改動 caller 實體；實際請求與搬前黃金檔完全相符。</summary>
        /// <param name="operation">合成的 create、update 或 delete 操作。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        public void Crud_ValidInput_PreservesSdkRequest(string operation)
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
            var service = new EntityCrudService(new object(), sdk.Object);
            if (operation == "create") Assert.Equal(RecordId, service.CreateEntity(entity));
            else if (operation == "update") service.UpdateEntity(entity);
            else service.DeleteEntity("contact", RecordId);
            Assert.NotNull(actual);
            GoldenQueryAssertions.Equal("mutation-" + operation + ".xml", actual!.ToString());
            Assert.Equal(RecordId, entity.Id);
            Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
            sdk.VerifyAll();
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只讀 ownerid 不呼叫 SDK；缺值沿用 Empty／空字串，合法顯示名稱原樣回傳而不是去重鍵。</summary>
        /// <param name="present">是否有 ownerid。</param>
        /// <param name="name">合成 owner 顯示名稱，可缺省。</param>
        [Theory]
        [InlineData(false, null)]
        [InlineData(true, null)]
        [InlineData(true, "合成負責人")]
        public void Owner_ReadOnly_PreservesMissingAndPresentValues(bool present, string? name)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", RecordId);
            if (present) entity["ownerid"] = new EntityReference("systemuser", OwnerId) { Name = name };
            var service = new OwnerManagementService(new object(), sdk.Object);
            Assert.Equal(present ? OwnerId : Guid.Empty, service.GetOwnerId(entity));
            Assert.Equal(name ?? string.Empty, service.GetOwnerName(entity));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>指派仍以 systemuser 為受派者、原資料庫 ID 為 Target，不能誤換為顯示名稱或新識別。</summary>
        [Fact]
        public void Owner_Assign_PreservesTypedRequest()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            AssignRequest? actual = null;
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>()))
                .Callback<OrganizationRequest>(request => actual = (AssignRequest)request)
                .Returns(new AssignResponse());
            var entity = new Entity("contact", RecordId);
            new OwnerManagementService(new object(), sdk.Object).AssignOwner("contact", entity, OwnerId);
            Assert.NotNull(actual);
            var xml = new XElement("assign",
                new XElement("target", new XAttribute("name", actual!.Target.LogicalName), new XAttribute("id", actual.Target.Id)),
                new XElement("assignee", new XAttribute("name", actual.Assignee.LogicalName), new XAttribute("id", actual.Assignee.Id)));
            GoldenQueryAssertions.Equal("mutation-assign.xml", xml.ToString());
            Assert.False(entity.Contains("ownerid"));
            sdk.Verify(s => s.Execute(It.IsAny<AssignRequest>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>把實際 SDK 收到的實體投影成固定可稽核 XML；只處理此 fixture 的 fullname，不放入連線或真實資料。</summary>
        private static XElement Describe(string operation, Entity entity)
        {
            var xml = new XElement(operation, new XAttribute("name", entity.LogicalName), new XAttribute("id", entity.Id));
            if (entity.Contains("fullname")) xml.Add(new XElement("fullname", entity.GetAttributeValue<string>("fullname")));
            return xml;
        }
    }
}
