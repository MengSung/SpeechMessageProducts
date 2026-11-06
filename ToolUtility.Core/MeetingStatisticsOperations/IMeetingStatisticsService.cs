/*
 * 檔案整體說明
 *
 * 目的與定位：已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 
 * host 共用例外通知邊界。 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
 *
 * 主要功能與流程：
 * 本檔宣告 IMeetingStatisticsService。
 * - 依已決定語義的主日日期查原欄位與排序；固定西曆，不自行換算時區，正式列 ID 不能為空或重複。
 * - 保留原週報／聯絡人條件及相容 uitype 提示值；lookup GUID 先驗證，不用顯示文字決定權限。
 * - 依聯絡人與主日日期條件查原出席頁；安全編碼且完整驗證正式 ID，同名不同 ID 資料全保留。
 * - 保留替代入口的參數順序與週報 uitype；安全組裝條件、整頁驗證，不另造快取或變更原查詢語義。
 * 資料／選項欄位包含 IMeetingStatisticsService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、
 * ToolUtility.Core/MeetingStatisticsOperations/MeetingStatisticsService.cs、ToolUtility/Core/ToolUtilityFacade.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface IMeetingStatisticsService
    {
        /// <summary>依已決定語義的主日日期查原欄位與排序；固定西曆，不自行換算時區，正式列 ID 不能為空或重複。</summary>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveBySunday(DateTime sundayDate);

        /// <summary>保留原週報／聯絡人條件及相容 uitype 提示值；lookup GUID 先驗證，不用顯示文字決定權限。</summary>
        /// <param name="weeklyReportName">只供 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">已由 host 授權的非空週報 GUID。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId);

        /// <summary>依聯絡人與主日日期條件查原出席頁；安全編碼且完整驗證正式 ID，同名不同 ID 資料全保留。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate);

        /// <summary>保留替代入口的參數順序與週報 uitype；安全組裝條件、整頁驗證，不另造快取或變更原查詢語義。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="weeklyReportName">只供 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">已由 host 授權的非空週報 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId);

        /// <summary>保留聯絡人與關懷到期日 not-null 條件；不能省略 lookup 而擴張範圍，故障不能變成空集合。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId);

        /// <summary>依聯絡人、小組與主日三項原條件查詢；兩個正式 lookup GUID 均先驗證，不以姓名隔離或去重。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="smallGroupName">只供 uiname 的小組顯示文字。</param>
        /// <param name="smallGroupId">已由 host 授權的非空小組 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate);
    }
}
