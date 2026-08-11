using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// Worker 啟動探針與後續 identity operation 共用的 fail-closed 驗證器。
/// 驗證只處理 bounded scalar，不保留 SDK client、credential、Session 或 Organization response；
/// 任一空 GUID、Organization 不符或 CE major/minor 不符都拒絕，避免錯誤 Profile 被標記為 Ready。
/// </summary>
public static class OfficialCrmIdentityValidator
{
    /// <summary>
    /// 驗證目前連線的使用者、Business Unit、Organization 與 CE 版本是否精確符合 deployment-owned 預期值。
    /// 此方法沒有 I/O、快取或共享可變狀態，適合在啟動與每次重新驗證時重複呼叫，不會造成跨要求資料保留。
    /// </summary>
    /// <returns>僅在全部 identity 與版本證據完整相符時回傳 <see langword="true"/>。</returns>
    public static bool IsValid(
        Guid userId,
        Guid businessUnitId,
        Guid organizationId,
        Guid expectedOrganizationId,
        Version? connectedOrganizationVersion,
        string expectedCeVersion)
    {
        if (userId == Guid.Empty ||
            businessUnitId == Guid.Empty ||
            organizationId == Guid.Empty ||
            expectedOrganizationId == Guid.Empty ||
            organizationId != expectedOrganizationId ||
            connectedOrganizationVersion is null)
        {
            return false;
        }

        return expectedCeVersion switch
        {
            "8.2" => connectedOrganizationVersion.Major == 8 &&
                     connectedOrganizationVersion.Minor == 2,
            "9.1" => connectedOrganizationVersion.Major == 9 &&
                     connectedOrganizationVersion.Minor == 1,
            _ => false
        };
    }
}
