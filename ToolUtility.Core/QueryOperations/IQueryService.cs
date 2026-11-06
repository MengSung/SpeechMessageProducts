/*
 * 檔案整體說明
 *
 * 目的與定位：Repository 查詢的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 
 * 已完成安全純化。
 *
 * 主要功能與流程：
 * 本檔宣告 IQueryService。
 * - 沿用欄位等值查第一筆或 null；欄位值不作身份授權，實際資料庫 ID 才是正式記錄識別。
 * - 沿用欄位等值查集合；caller 擁有分頁及發布責任，不以重複顯示值隱藏合法記錄。
 * - 執行受信任且在授權 scope 內建構的 FetchXML；實作須拒絕 DTD／外部解析，查詢文字不得寫入診斷。
 * - 依正式聯絡人查舊排序的第一筆血液報告或 null；敏感資料僅供已授權 caller 使用。
 * 資料／選項欄位包含 IQueryService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/QueryOperations/QueryService.cs 追查目前工作樹中的合作／使用位置。
 */
#nullable enable
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// Repository 查詢的純 BCL／SDK 契約；保留既有 namespace、參數順序與預設值供兩個產品目標共用。
    /// 不包含 host 連線、cache、logger 或排程實作，介面搬移不代表舊 host 已完成安全純化。
    /// </summary>
    /// <remarks>
    /// 呼叫端先以伺服器驗證使用者／租戶／教會與資料範圍；任何名稱、路由或 ID 參數均不能建立授權。
    /// 可變 Entity、集合及 ArrayList 僅屬單次操作，caller 不得跨 scope 共用；實作須在發布前驗正式 ID 及完整結果。
    /// SDK 由 host 借入並擁有 timeout、drain 與 Dispose；本契約不保存 request、Session、credential 或執行資源。
    /// 真正故障由實作保留原 Exception 並交最終 host 共用告警；預期查無與明確 caller 取消不偽裝成系統錯誤。
    /// </remarks>
    public interface IQueryService
    {
        /// <summary>
        /// 沿用欄位等值查第一筆或 null；欄位值不作身份授權，實際資料庫 ID 才是正式記錄識別。
        /// </summary>
        /// <param name="entityName">受信任程式指定的 CRM 表 logical name，不接受其作授權。</param>
        /// <param name="fieldName">受信任程式指定的 CRM 欄位 logical name。</param>
        /// <param name="fieldValue">只屬此次查詢的欄位值，不可輸出至診斷。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? RetrieveEntityByField(string entityName, string fieldName, string fieldValue);

        /// <summary>
        /// 沿用欄位等值查集合；caller 擁有分頁及發布責任，不以重複顯示值隱藏合法記錄。
        /// </summary>
        /// <param name="entityName">受信任程式指定的 CRM 表 logical name，不接受其作授權。</param>
        /// <param name="fieldName">受信任程式指定的 CRM 欄位 logical name。</param>
        /// <param name="fieldValue">只屬此次查詢的欄位值，不可輸出至診斷。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue);

        /// <summary>
        /// 執行受信任且在授權 scope 內建構的 FetchXML；實作須拒絕 DTD／外部解析，查詢文字不得寫入診斷。
        /// </summary>
        /// <param name="fetchXml">受信任程式於已授權 scope 建構的查詢，不可含未跳脫輸入。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryByFetchXml(string fetchXml);

        /// <summary>
        /// 依正式聯絡人查舊排序的第一筆血液報告或 null；敏感資料僅供已授權 caller 使用。
        /// </summary>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <returns>查無資料時為 null；非空結果僅屬此 caller，不得跨授權 scope 保留。</returns>
        Entity? QueryBloodReportByContactId(Guid contactId);

        /// <summary>
        /// 保留名單、聯絡人及期間參數；不同舊實作的日期算法不在純契約搬移時統一，不能以 host 本地時間當教會時區。
        /// </summary>
        /// <param name="listEntityId">伺服器已驗證範圍內的正式名單 ID。</param>
        /// <param name="contactId">已授權流程提供的聯絡人／舊客戶編號，查詢條件本身不是權限。</param>
        /// <param name="monthPeriod">舊查詢期間參數；實際單位與日期算法依對應實作保留。</param>
        /// <returns>操作自有的 SDK 結果；caller 應處理分頁 metadata，發布前驗證正式身份且不可按顯示值去重。</returns>
        EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod);

    }
}
