// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/TestHelpers/MockOrganizationServiceFactory.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：提供 IOrganizationService 的測試替身工廠，讓服務層測試不需接觸真實 Dataverse。
// 主要型別：static class MockOrganizationServiceFactory
// 主要成員：CreateMock、CreateMockWithEntity、CreateMockWithCollection
// 引用命名空間：Moq、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query
// 閱讀路徑：閱讀此檔案時應先看各方法預設的 mock 行為，因為它們是所有服務層測試的共同前提。
// 維護重點：本工廠取代舊的 MockCrmClientFactory。ToolUtility 的服務建構式已由
//           ICrmClient 改為直接接收 Microsoft.Xrm.Sdk.IOrganizationService，
//           測試替身必須與產品契約一致，不可再以 ICrmClient 冒充。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig。
// ============================================================================
using Moq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// <see cref="IOrganizationService"/> 測試替身工廠。
    /// </summary>
    /// <remarks>
    /// 保護的契約：ToolUtility 的服務層一律以 <see cref="IOrganizationService"/> 作為對外
    /// 資料存取邊界（建構式簽章為 <c>(object logger, IOrganizationService organizationService)</c>）。
    /// 本工廠產生的替身即代表該邊界，使測試不依賴真實 Dataverse 連線、憑證或網路。
    ///
    /// 資源生命週期：這裡產生的 mock 為純記憶體物件，不持有連線、通道、檔案或計時器，
    /// 由 GC 隨測試方法結束回收，測試無須執行任何釋放步驟。
    ///
    /// 隔離保證：每次呼叫都回傳全新的 <see cref="Mock{T}"/> 實例，測試之間不共用可變狀態，
    /// 因此不會出現跨測試的資料污染。
    /// </remarks>
    public static class MockOrganizationServiceFactory
    {
        /// <summary>
        /// 建立具備安全預設行為的 <see cref="IOrganizationService"/> 替身。
        /// </summary>
        /// <remarks>
        /// 預設行為刻意選擇「不拋例外、回傳空值」：<c>Retrieve</c> 回傳 <c>null</c>、
        /// <c>RetrieveMultiple</c> 回傳空集合、<c>Create</c> 回傳新的 <see cref="Guid"/>。
        /// 這讓每個測試只需覆寫自己真正關心的那一個行為，其餘保持中性，
        /// 避免測試因為未設定的呼叫而失敗於無關的原因。
        /// </remarks>
        /// <returns>可繼續以 Moq 覆寫行為的替身。</returns>
        public static Mock<IOrganizationService> CreateMock()
        {
            var mock = new Mock<IOrganizationService>();

            mock.Setup(x => x.Retrieve(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<ColumnSet>()))
                .Returns((Entity)null);

            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(new EntityCollection());

            mock.Setup(x => x.Create(It.IsAny<Entity>()))
                .Returns(Guid.NewGuid());

            mock.Setup(x => x.Update(It.IsAny<Entity>()));
            mock.Setup(x => x.Delete(It.IsAny<string>(), It.IsAny<Guid>()));

            return mock;
        }

        /// <summary>
        /// 建立替身，並讓 <c>Retrieve</c> 於「邏輯名稱與識別碼皆相符」時回傳指定資料列。
        /// </summary>
        /// <param name="entity">預期被取回的資料列；其 <c>LogicalName</c> 與 <c>Id</c> 即為比對條件。</param>
        /// <remarks>
        /// 刻意以精確比對（而非 <c>It.IsAny</c>）設定，使測試能證明受測程式碼確實
        /// 以正確的實體名稱與識別碼查詢，而不是碰巧取到資料。
        /// </remarks>
        public static Mock<IOrganizationService> CreateMockWithEntity(Entity entity)
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
        /// 建立替身，並讓 <c>RetrieveMultiple</c> 回傳指定集合。
        /// </summary>
        /// <param name="collection">預期被取回的集合。</param>
        /// <remarks>
        /// 查詢條件使用 <c>It.IsAny</c>，因為使用此多載的測試關心的是「集合內容如何被轉換」，
        /// 而非查詢本身如何組成；查詢組成由 FetchXml／QueryExpression 相關測試各自負責。
        /// </remarks>
        public static Mock<IOrganizationService> CreateMockWithCollection(EntityCollection collection)
        {
            var mock = CreateMock();

            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(collection);

            return mock;
        }
    }
}
