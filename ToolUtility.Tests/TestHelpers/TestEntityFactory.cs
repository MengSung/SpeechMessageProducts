// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/TestHelpers/TestEntityFactory.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class TestEntityFactory
// 主要成員：CreateContact、CreateContactFull、CreateList、CreateAnnotation、CreateEmpty
// 引用命名空間：Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 測試用的 Entity 工廠
    /// 快速建立測試用的 Dynamics 365 Entity 物件
    /// </summary>
    public static class TestEntityFactory
    {
        /// <summary>
        /// 建立一個測試用的 Contact Entity
        /// </summary>
        /// <param name="lineId">Line ID</param>
        /// <param name="fullName">聯絡人全名</param>
        /// <returns>Contact Entity</returns>
        public static Entity CreateContact(string lineId, string fullName)
        {
            var entity = new Entity("contact")
            {
                Id = Guid.NewGuid(),
                ["new_lineid"] = lineId,
                ["fullname"] = fullName,
                ["statecode"] = 0  // Active
            };

            return entity;
        }

        /// <summary>
        /// 建立一個測試用的 Contact Entity（含完整欄位）
        /// </summary>
        public static Entity CreateContactFull(
            string lineId,
            string fullName,
            string customerId = null,
            string accountNumber = null,
            string mobilePhone = null,
            string nationId = null)
        {
            var entity = CreateContact(lineId, fullName);

            if (!string.IsNullOrEmpty(customerId))
                entity["build_customer_id"] = customerId;

            if (!string.IsNullOrEmpty(accountNumber))
                entity["new_accountnumber"] = accountNumber;

            if (!string.IsNullOrEmpty(mobilePhone))
                entity["mobilephone"] = mobilePhone;

            if (!string.IsNullOrEmpty(nationId))
                entity["new_nationid"] = nationId;

            return entity;
        }

        /// <summary>
        /// 建立一個測試用的 List Entity（名單）
        /// </summary>
        public static Entity CreateList(string listName, Guid? listId = null)
        {
            return new Entity("list")
            {
                Id = listId ?? Guid.NewGuid(),
                ["listname"] = listName,
                ["statecode"] = 0
            };
        }

        /// <summary>
        /// 建立一個測試用的 Annotation Entity（附件）
        /// </summary>
        public static Entity CreateAnnotation(string subject, string fileName, byte[] documentBody)
        {
            return new Entity("annotation")
            {
                Id = Guid.NewGuid(),
                ["subject"] = subject,
                ["filename"] = fileName,
                ["documentbody"] = Convert.ToBase64String(documentBody),
                ["mimetype"] = "application/octet-stream"
            };
        }

        /// <summary>
        /// 建立一個空的 Entity（指定類型）
        /// </summary>
        public static Entity CreateEmpty(string entityName)
        {
            return new Entity(entityName)
            {
                Id = Guid.NewGuid()
            };
        }
    }
}
