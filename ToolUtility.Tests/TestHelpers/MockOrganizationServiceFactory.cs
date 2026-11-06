/*
 * 檔案整體說明
 *
 * 目的與定位：IOrganizationService 測試替身工廠。
 *
 * 主要功能與流程：
 * 本檔宣告 MockOrganizationServiceFactory。
 * - 建立具備安全預設行為的 IOrganizationService 替身。
 * - 建立替身，並讓 Retrieve 於「邏輯名稱與識別碼皆相符」時回傳指定資料列。
 * - 建立替身，並讓 RetrieveMultiple 回傳指定集合。
 * 測試案例入口包含 CreateMock、CreateMockWithEntity、CreateMockWithCollection。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
