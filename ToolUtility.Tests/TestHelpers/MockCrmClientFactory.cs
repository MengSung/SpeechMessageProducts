using Moq;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// Mock CRM Client 工廠
    /// 用於產生測試用的 ICrmClient Mock 物件
    /// </summary>
    public static class MockCrmClientFactory
    {
        /// <summary>
        /// 建立一個基本的 Mock ICrmClient 實例
        /// </summary>
        public static Mock<ICrmClient> CreateMock()
        {
            var mock = new Mock<ICrmClient>();
            
            // 預設行為：Retrieve 回傳 null
            mock.Setup(x => x.Retrieve(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<ColumnSet>()))
                .Returns((Entity)null);
            
            // 預設行為：RetrieveMultiple 回傳空集合
            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(new EntityCollection());
            
            // 預設行為：Create 回傳新的 Guid
            mock.Setup(x => x.Create(It.IsAny<Entity>()))
                .Returns(Guid.NewGuid());
            
            // 預設行為：Update / Delete 不做任何事
            mock.Setup(x => x.Update(It.IsAny<Entity>()));
            mock.Setup(x => x.Delete(It.IsAny<string>(), It.IsAny<Guid>()));
            
            return mock;
        }

        /// <summary>
        /// 建立一個 Mock ICrmClient，設定 Retrieve 回傳指定的 Entity
        /// </summary>
        public static Mock<ICrmClient> CreateMockWithEntity(Entity entity)
        {
            var mock = CreateMock();
            
            mock.Setup(x => x.Retrieve(
                entity.LogicalName,
                entity.Id,
                It.IsAny<ColumnSet>()))
                .Returns(entity);
            
            return mock;
        }

        /// <summary>
        /// 建立一個 Mock ICrmClient，設定 RetrieveMultiple 回傳指定的 EntityCollection
        /// </summary>
        public static Mock<ICrmClient> CreateMockWithCollection(EntityCollection collection)
        {
            var mock = CreateMock();
            
            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(collection);
            
            return mock;
        }
    }
}
