/*
 * 檔案整體說明
 *
 * 目的與定位：單次授權範圍的收費查詢契約；呼叫者先驗證身分／教會，再提供資料庫 ID。 所有結果是操作擁有的 SDK 查詢頁，保留分頁資訊；不按名稱去重，ID 衝突整頁拒絕。 實作不得持有跨使用者狀態或接管借入連線，
 * 真正故障傳至 host 共用通知邊界。
 *
 * 主要功能與流程：
 * 本檔宣告 IFeeService。
 * - 依認獻 ID 與期數讀啟用收費頁；文字安全組裝，不允許輸入改變條件樹。
 * - 依聯絡人ID讀啟用且已核准認獻；不得以空集合代替SDK查詢或失敗。
 * - 讀分類非空的奉獻收費頁；整頁驗穩定ID後才回傳，不改合法同名記錄。
 * - 讀含端點日期內的奉獻頁，套用原付款狀態白名單；日期採西曆但不作時區偏移。
 * 資料／選項欄位包含 IFeeService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/FeeOperations/FeeService.cs、ToolUtility/Core/ToolUtilityFacade.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.FeeOperations
{
    /// <summary>
    /// 單次授權範圍的收費查詢契約；呼叫者先驗證身分／教會，再提供資料庫 ID。
    /// 所有結果是操作擁有的 SDK 查詢頁，保留分頁資訊；不按名稱去重，ID 衝突整頁拒絕。
    /// 實作不得持有跨使用者狀態或接管借入連線，真正故障傳至 host 共用通知邊界。
    /// </summary>
    public interface IFeeService
    {
        /// <summary>
        /// 依認獻 ID 與期數讀啟用收費頁；文字安全組裝，不允許輸入改變條件樹。
        /// </summary>
        /// <param name="dedicationBookingName">僅供lookup顯示，不作識別或授權。</param>
        /// <param name="dedicationBookingId">非空認獻資料庫GUID。</param>
        /// <param name="paidPeriod">原查詢的非空期數文字。</param>
        /// <returns>保留原投影、排序與500筆上限的SDK頁。</returns>
        EntityCollection RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod);

        /// <summary>依聯絡人ID讀啟用且已核准認獻；不得以空集合代替SDK查詢或失敗。</summary>
        /// <param name="contactName">僅供lookup顯示，不作權限依據。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <returns>保留原建立時間倒序及1000筆上限的SDK頁。</returns>
        EntityCollection RetrieveDedicationBooking(string contactName, string contactId);

        /// <summary>讀分類非空的奉獻收費頁；整頁驗穩定ID後才回傳，不改合法同名記錄。</summary>
        /// <param name="contactName">可含特殊字元的lookup顯示名稱。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <returns>呼叫者擁有的原SDK頁，不在實作保存。</returns>
        EntityCollection RetrieveDedicationFee(string contactName, string contactId);

        /// <summary>
        /// 讀含端點日期內的奉獻頁，套用原付款狀態白名單；日期採西曆但不作時區偏移。
        /// </summary>
        /// <param name="contactName">lookup顯示名稱，不作權限。</param>
        /// <param name="contactId">非空聯絡人資料庫GUID。</param>
        /// <param name="startDate">呼叫者已決定的業務起日。</param>
        /// <param name="endDate">不早於起日的業務迄日。</param>
        /// <returns>完整驗證、保留原分頁資訊的一頁結果。</returns>
        EntityCollection RetrieveDedicationFeeByDateRange(string contactName, string contactId, DateTime startDate, DateTime endDate);

        /// <summary>依課程紀錄ID及pay_date非空查已繳頁，不推導或改寫付款狀態。</summary>
        /// <param name="storLessonName">只為來源相容保留，實作不使用或保存。</param>
        /// <param name="storLessonId">伺服器已授權的非空課程紀錄GUID。</param>
        /// <returns>含繳費日期及實繳額的SDK頁，合法同名不同ID均保留。</returns>
        EntityCollection GetPaidFee(string storLessonName, string storLessonId);

        /// <summary>依課程紀錄ID及pay_date為null查未繳頁，保留舊來源欄位及排序。</summary>
        /// <param name="storLessonName">只為相容保留，不當識別或授權。</param>
        /// <param name="storLessonId">伺服器已授權的非空課程紀錄GUID。</param>
        /// <returns>含應繳額的SDK頁，重複或欠缺穩定ID時整頁拒絕。</returns>
        EntityCollection GetNotYetPaidFee(string storLessonName, string storLessonId);

        /// <summary>以卡或ATM欄位OR比對訂單文字，安全編碼所有XML特殊字元且保留原欄位／排序。</summary>
        /// <param name="orderNumber">非空訂單文字；所屬權限仍由host先驗證。</param>
        /// <returns>完整ID驗證的SDK頁，不以文字合併不同資料庫記錄。</returns>
        EntityCollection RetrieveFeeByFetchXmlOrderNumber(string orderNumber);
    }
}
