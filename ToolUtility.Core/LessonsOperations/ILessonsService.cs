/*
 * 檔案整體說明
 *
 * 目的與定位：已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回
 * host 共用例外通知邊界。 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
 *
 * 主要功能與流程：
 * 本檔宣告 ILessonsService。
 * - 依課程起訖日期與原報名 join 查課程；保留 distinct、原投影及完整正式 ID，不以顯示名稱去重。
 * - 依原課程起訖日期與分類排序查課程；拒絕反向區間，不依程序文化改變曆法，不自行轉換時區。
 * - 保留原狀態排除、5筆上限及聯絡人 outer join；兩 lookup 均驗非空 GUID，全部列驗證後才回完整頁。
 * 資料／選項欄位包含 ILessonsService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/LessonsOperations/LessonsService.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.LessonsOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface ILessonsService
    {
        /// <summary>依課程起訖日期與原報名 join 查課程；保留 distinct、原投影及完整正式 ID，不以顯示名稱去重。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId);

        /// <summary>依原課程起訖日期與分類排序查課程；拒絕反向區間，不依程序文化改變曆法，不自行轉換時區。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate);

        /// <summary>保留原狀態排除、5筆上限及聯絡人 outer join；兩 lookup 均驗非空 GUID，全部列驗證後才回完整頁。</summary>
        /// <param name="lessonName">僅作 uiname 的課程顯示文字。</param>
        /// <param name="lessonId">server 已授權的非空課程資料庫 GUID。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId);
    }
}
