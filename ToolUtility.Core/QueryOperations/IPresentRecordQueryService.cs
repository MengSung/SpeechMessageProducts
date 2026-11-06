/*
 * 檔案整體說明
 *
 * 目的與定位：出席與週報純SDK單頁契約；保留八個原簽章，host先完成使用者／教會／組織授權。 結果只屬caller、最多5000列，正式ID完整驗證後發布；SDK由host借入並負責drain及Dispose。
 *
 * 主要功能與流程：
 * 本檔宣告 IPresentRecordQueryService。
 * - 搜尋主日日期是最近N週的靈修單 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
 * - 根據主日日期排序查詢出席記錄 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
 * - 使用 FetchXML 查詢最近N週的出席記錄 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
 * - 根據週報和聯絡人ID查詢出席記錄 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
 * 資料／選項欄位包含 IPresentRecordQueryService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/DataOperations/PresentQueryCharacterizationTests.cs、
 * ToolUtility.Tests/DataOperations/PresentQuerySafetyTests.cs、
 * ToolUtility.Core/QueryOperations/PresentRecordQueryService.cs、ToolUtility/Core/ToolUtilityFacade.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 出席與週報純SDK單頁契約；保留八個原簽章，host先完成使用者／教會／組織授權。
    /// 結果只屬caller、最多5000列，正式ID完整驗證後發布；SDK由host借入並負責drain及Dispose。
    /// </summary>
    public interface IPresentRecordQueryService
    {
        /// <summary>
        /// 搜尋主日日期是最近N週的靈修單
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int weekPeriod);

        /// <summary>
        /// 根據主日日期排序查詢出席記錄
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryPresentRecordSortBySunday(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 使用 FetchXML 查詢最近N週的出席記錄
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryPresentRecordSortBySundayFetchXml(int lastWeeks, string contactName, string contactId);

        /// <summary>
        /// 根據週報和聯絡人ID查詢出席記錄
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid contactId, Guid weeklyReportEntityId);

        /// <summary>
        /// 根據日期範圍查詢實體清單
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryEntityListByDate(string parentEntityName, string parentEntityIdName,
            string parentEntityId, string associationName, string childEntityName);

        /// <summary>
        /// 查詢週報(根據主日日期)
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryWeeklyReportBySunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 查詢週報(主日日期前兩個月)
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryWeeklyReportBeforeTwoMonthOfSunday(DateTime sunday, Guid listEntityId);

        /// <summary>
        /// 根據聯絡人ID查詢名單
        /// 不同正式ID的同名記錄完整保留；分頁metadata不省略，失敗向host保留原Exception。
        /// </summary>
        EntityCollection QueryListByContactId(Guid contactId, string associationName);
    }
}
