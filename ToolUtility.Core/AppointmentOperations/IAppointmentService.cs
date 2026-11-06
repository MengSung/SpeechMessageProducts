/*
 * 檔案整體說明
 *
 * 目的與定位：已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 
 * host 共用例外通知邊界。 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
 *
 * 主要功能與流程：
 * 本檔宣告 IAppointmentService。
 * - 保留舊版未依日期過濾、全欄位 QueryByAttribute 的相容行為；如需日期過濾使用明確區間 API。整頁穩定 ID 驗證後才發布。
 * - 依原 scheduledstart 含端點區間與投影查約會；拒絕反向區間，固定西曆日期，原分頁資訊完整保留。
 * - 依已授權聯絡人、今年與原三個簽核狀態查詢；顯示文字安全編碼，正式 GUID 驗證不能由名稱取代。
 * - 在原日期區間追加既有排程值；文字只作 XML 屬性，不可注入或省略查詢條件。
 * 資料／選項欄位包含 IAppointmentService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Core/AppointmentOperations/AppointmentService.cs、
 * ToolUtility/Core/ToolUtilityFacade.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface IAppointmentService
    {
        /// <summary>保留舊版未依日期過濾、全欄位 QueryByAttribute 的相容行為；如需日期過濾使用明確區間 API。整頁穩定 ID 驗證後才發布。</summary>
        /// <param name="selectedDate">舊版未使用的相容日期參數，不作授權或新增過濾。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDate(DateTime selectedDate);

        /// <summary>依原 scheduledstart 含端點區間與投影查約會；拒絕反向區間，固定西曆日期，原分頁資訊完整保留。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate);

        /// <summary>依已授權聯絡人、今年與原三個簽核狀態查詢；顯示文字安全編碼，正式 GUID 驗證不能由名稱取代。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByContactWithinYear(string contactName, string contactId);

        /// <summary>在原日期區間追加既有排程值；文字只作 XML 屬性，不可注入或省略查詢條件。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <param name="scheduleType">原 SDK 排程值，安全編碼且不保存。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType);
    }
}
