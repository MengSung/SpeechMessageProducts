// ============================================================================
// 檔案：ChurchReport/Services/Donation/DonationFeeAuditAccessResolver.cs
// 目的：為奉獻稽核費用讀取建立唯一、純粹的伺服器端登入身分授權邊界。
//
// 本類別只檢查本 request 已由登入流程解析的 contact Entity snapshot，不接受瀏覽器目標
// GUID，也不進行 CRM 查詢、Session/快取寫入、profile 選擇或背景工作。因此呼叫端可在
// 任何資料存取前，以固定成本 fail closed，避免任意 contact ID 成為 IDOR 授權來源。
// Entity 仍由上游 request/session owner 擁有；本類別不保存其參考，呼叫結束後不保留任何
// 使用者、職稱或授權決定，確保不同使用者請求不會經由 static 狀態交叉洩漏。
// ============================================================================

using System;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Services.Donation;

/// <summary>
/// 奉獻稽核費用讀取的伺服器端授權解析器。
/// </summary>
/// <remarks>
/// <para>
/// 信任邊界：只有登入流程建立並保留在目前 request/session scope 的 contact snapshot 可以
/// 作為輸入；HTTP query、route 或 body 中的 target contact ID 不屬於此 API，故不可能以
/// 呼叫端指定的資料影響權限判斷。
/// </para>
/// <para>
/// 生命週期與隔離：解析器無欄位、無快取、無 I/O，且不保存傳入 <see cref="Entity"/>。
/// 授權結果只在呼叫堆疊內存活，任何取消、錯誤或後續 CRM/Gateway fault 都不會讓此類別
/// 留下可被另一 request 重用的身分或資料。
/// </para>
/// </remarks>
public static class DonationFeeAuditAccessResolver
{
    private const string ChurchJobTitleAttributeName = "new_church_jobtitle";

    /// <summary>
    /// 判斷伺服器已解析的登入 contact 是否具有奉獻稽核讀取權限。
    /// </summary>
    /// <param name="serverLoginContact">
    /// 僅能由既有登入流程取得的目前登入 contact snapshot。此參數不是瀏覽器選取目標，
    /// 也不會被本方法保存、查詢、更新或加入快取。
    /// </param>
    /// <returns>
    /// 僅當 snapshot 有有效 CRM ID、職稱屬性為非空白字串，且通過既有會計角色規則時回傳
    /// <see langword="true"/>；所有缺失、格式不符或不具權限的情況皆回傳
    /// <see langword="false"/>。
    /// </returns>
    public static bool CanAccessFeeAudit(Entity? serverLoginContact)
    {
        if (serverLoginContact is null || serverLoginContact.Id == Guid.Empty)
        {
            return false;
        }

        if (!serverLoginContact.Attributes.TryGetValue(
                ChurchJobTitleAttributeName,
                out var rawJobTitle) ||
            rawJobTitle is not string jobTitle ||
            string.IsNullOrWhiteSpace(jobTitle))
        {
            return false;
        }

        return DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle);
    }
}
