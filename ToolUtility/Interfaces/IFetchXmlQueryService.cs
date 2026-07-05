// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Interfaces/IFetchXmlQueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IFetchXmlQueryService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// FetchXML 查詢服務介面
    /// </summary>
    public interface IFetchXmlQueryService
    {
        /// <summary>
        /// 根據聯絡人查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveStorLessonsByFetchXml(string contactName, string contactId);

        /// <summary>
        /// 根據課程查詢學員上課記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(string lessonName, string lessonId);

        /// <summary>
        /// 根據聯絡人查詢認獻記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveDedicationBookingByFetchXml(string contactName, string contactId);

        /// <summary>
        /// 根據主日日期查詢聚會統計記錄 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime sundayDate);

        /// <summary>
        /// 根據認獻預約和繳費期間查詢收費單 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveFeeByFetchXml(string dedicationBookingName, string dedicationBookingId, string paidPeriod);

        /// <summary>
        /// 查詢所有需要點名的小組名單 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveListByFetchXml();

        /// <summary>
        /// 查詢所有小組名單集合 (使用 FetchXML)
        /// </summary>
        EntityCollection RetrieveSmallGroupListCollectionByFetchXml();
    }
}
