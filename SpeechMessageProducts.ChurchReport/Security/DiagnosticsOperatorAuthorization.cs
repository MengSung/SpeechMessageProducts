using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Security
{
    /// <summary>
    /// 擁有 DEBUG 診斷端點的部署授權契約。允許清單只在 Host 啟動時由部署設定建立一次不可變快照，
    /// request 僅以登入 cookie 內由伺服器簽發的 <see cref="ClaimTypes.NameIdentifier"/> 比對；不讀取
    /// Session、query、header 或產品端 JSON，也不建立跨使用者 mutable cache、timer、subscription 或背景工作。
    /// </summary>
    public static class DiagnosticsOperatorAuthorization
    {
        /// <summary>供 MVC controller 與 Startup 共用的具名授權政策，避免字串分岔造成保護失效。</summary>
        public const string PolicyName = "diagnostics-operator";

        private const string OperatorContactIdsSection = "Diagnostics:OperatorContactIds";

        /// <summary>
        /// 從部署設定建立操作員聯絡人識別的不可變快照。只接受非空 GUID，並轉成固定 D 格式；遺漏、空白或
        /// 非法項目不會擴大權限，若最後沒有有效項目，後續政策必須 fail closed。回傳的 FrozenSet 由 Host
        /// policy closure 唯一持有直到服務停止，不會保留 IConfiguration、request、principal 或 Session。
        /// </summary>
        /// <param name="configuration">Host 啟動時的部署設定；產品使用者輸入不是此設定的信任來源。</param>
        /// <returns>只讀且不可變的正規化操作員識別集合。</returns>
        public static FrozenSet<string> CreateAllowedContactIds(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return configuration
                .GetSection(OperatorContactIdsSection)
                .GetChildren()
                .Select(child => NormalizeContactId(child.Value))
                .Where(static contactId => contactId is not null)
                .Select(static contactId => contactId!)
                .ToFrozenSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// 驗證目前 principal 是否唯一包含一個有效且在部署清單內的 NameIdentifier。未驗證身分、空清單、
        /// 缺少 claim、重複 claim、空 GUID 或格式錯誤皆拒絕，避免 claim 混淆與任意已登入使用者觸發
        /// ADFS／CRM 診斷流量。方法只使用區域變數，不保存 principal 或 claim，也沒有可洩漏的生命週期資源。
        /// </summary>
        /// <param name="principal">Cookie middleware 驗證後的伺服器端 principal。</param>
        /// <param name="allowedContactIds">Host 啟動時建立的不可變部署清單。</param>
        /// <returns>只有唯一、有效且已允許的聯絡人識別才回傳 true。</returns>
        public static bool IsAuthorized(
            ClaimsPrincipal principal,
            IReadOnlySet<string> allowedContactIds)
        {
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentNullException.ThrowIfNull(allowedContactIds);

            if (principal.Identity?.IsAuthenticated != true || allowedContactIds.Count == 0)
            {
                return false;
            }

            string? normalizedContactId = null;
            foreach (var claim in principal.FindAll(ClaimTypes.NameIdentifier))
            {
                if (normalizedContactId is not null)
                {
                    return false;
                }

                normalizedContactId = NormalizeContactId(claim.Value);
                if (normalizedContactId is null)
                {
                    return false;
                }
            }

            return normalizedContactId is not null && allowedContactIds.Contains(normalizedContactId);
        }

        /// <summary>
        /// 將聯絡人識別正規化為不可混淆的 GUID D 格式；不記錄原值，也不把無效值帶入例外或遙測。
        /// </summary>
        private static string? NormalizeContactId(string? value)
            => Guid.TryParse(value?.Trim(), out var contactId) && contactId != Guid.Empty
                ? contactId.ToString("D")
                : null;
    }
}
