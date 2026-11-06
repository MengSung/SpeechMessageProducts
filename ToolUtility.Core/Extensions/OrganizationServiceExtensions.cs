/*
 * 檔案整體說明
 *
 * 目的與定位：舊 SDK 相容入口，委派唯一 EntityQueryService 查詢驗證，不建立第二套 XML／資料列驗證器。 靜態類別沒有可變狀態、快取或 SDK 參照；授權、傳輸逾時與連線 owner 仍由 host
 * 負責。
 *
 * 主要功能與流程：
 * 本檔宣告 OrganizationServiceExtensions。
 * - 執行一次完整原請求，保留 Query、選項與分頁；完整穩定 ID 及根實體驗證後才發布。 無效輸入或回應明確失敗，只有 SDK 真正回傳的合法空頁才代表查無資料。 操作局部查詢器確定性 Dispose 只清借入參照，
 * 不關閉 SDK、不保存結果、不隱藏重試； 真實 SDK Exception 原樣傳回 host 的共用通知邊界。
 * 主要操作入口包含 ExecuteRetrieveMultiple。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.Extensions
{
    /// <summary>
    /// 舊 SDK 相容入口，委派唯一 EntityQueryService 查詢驗證，不建立第二套 XML／資料列驗證器。
    /// 靜態類別沒有可變狀態、快取或 SDK 參照；授權、傳輸逾時與連線 owner 仍由 host 負責。
    /// </summary>
    public static class OrganizationServiceExtensions
    {
        /// <summary>
        /// 執行一次完整原請求，保留 Query、選項與分頁；完整穩定 ID 及根實體驗證後才發布。
        /// 無效輸入或回應明確失敗，只有 SDK 真正回傳的合法空頁才代表查無資料。
        /// 操作局部查詢器確定性 Dispose 只清借入參照，不關閉 SDK、不保存結果、不隱藏重試；
        /// 真實 SDK Exception 原樣傳回 host 的共用通知邊界。
        /// </summary>
        /// <param name="service">host 擁有、已完成使用者／組織／範圍授權的借入 SDK。</param>
        /// <param name="request">caller 獨占且含已授權 Query 的原請求，不在呼叫中並行修改。</param>
        /// <returns>單次操作擁有的完整頁，保持所有不同 ID 的同名記錄。</returns>
        /// <exception cref="ArgumentNullException">SDK 或 request 未提供。</exception>
        /// <exception cref="ArgumentException">Query 無效，在 SDK I/O 前拒絕。</exception>
        /// <exception cref="InvalidOperationException">SDK 回應缺頁、ID 無效／重複或根實體不符。</exception>
        public static EntityCollection ExecuteRetrieveMultiple(this IOrganizationService service, RetrieveMultipleRequest request)
        {
#if NETFRAMEWORK
            if (service == null) throw new ArgumentNullException(nameof(service));
#else
            ArgumentNullException.ThrowIfNull(service);
#endif
            using var queries = new EntityQueryService(organizationService: service);
            return queries.ExecuteRetrieveMultiple(request);
        }
    }
}
