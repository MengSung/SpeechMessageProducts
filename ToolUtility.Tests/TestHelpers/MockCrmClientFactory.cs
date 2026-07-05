// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/TestHelpers/MockCrmClientFactory.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class MockCrmClientFactory
// 主要成員：CreateMock、CreateMockWithEntity、CreateMockWithCollection
// 引用命名空間：Moq、ToolUtilityNameSpace.Interfaces、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
