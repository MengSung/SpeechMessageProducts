/*
 * 檔案整體說明
 *
 * 目的與定位：好牧人 2.0 F2 組合根的同步啟動守門，只允許核定 Development
 * 測試組織啟動；Production 與所有未知環境均在建立 CRM 或通知資源前拒絕。
 *
 * 主要功能與流程：ResolveEnvironmentName 接收組合根已取得的環境字串，依 D27
 * DOTNET、ASPNETCORE、Production 的 null 回退順序選值；Validate 借用同一份
 * IHostEnvironment 快照與有效組態，驗證 HTTPS、預設埠、完整組織服務路徑，
 * 排除 userinfo、query、fragment、URI 正規化繞過，再精確核對部署允許清單。
 *
 * 使用邊界與維護：環境及清單是伺服器部署資料，不是 HTTP 輸入，也不是授權證明；
 * goodshepherd20 必須是第一個 DNS label，短清單值只匹配該完整 label，完整清單值
 * 只匹配整個主機，誤列舊組織或其他產品也不能放行。不探測 DNS、D365 或版本，
 * 不讀程序環境，不保存組態、憑證、request、Session 或驗證結果，因此無跨呼叫快取。
 * 呼叫端保有組態與環境的所有權；所有區域值僅活至同步返回，無取消、等待、timer
 * 或 Dispose 順序。拒絕只拋固定原因，不含設定值；組合根負責最外層錯誤記錄。
 * 維護時執行 StartupTests 的 URL、環境優先序與允許清單案例，離線驗證不代表實機。
 */
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SpeechMessageProducts.GoodShepherd2.Web.Startup;

/// <summary>
/// 在 F2 階段強制好牧人 2.0 的開發環境及測試組織邊界；所有驗證均為同步純讀取，
/// 不連線、不建立服務 owner，也不保留能在不同主機或測試間共用的可變狀態。
/// </summary>
public static class GoodShepherd2StartupGuard
{
    /// <summary>本階段唯一核定的組織 DNS label，不能藉由部署清單開放舊組織或其他產品。</summary>
    private const string DevelopmentOrganization = "goodshepherd20";

    /// <summary>只接受完整且未經 dot-segment 或跳脫字元改寫的組織服務路徑。</summary>
    private const string OrganizationServicePath = "/XRMServices/2011/Organization.svc";

    /// <summary>
    /// 依 D27 選擇組合根的唯一環境快照，不讀程序環境；只有 null 代表來源未設定。
    /// 空白或未知 DOTNET 值仍優先保留，交由 Validate 拒絕，不能降級成 Development。
    /// </summary>
    /// <param name="dotnetEnvironment">組合根已讀取的 DOTNET_ENVIRONMENT 值，null 才回退。</param>
    /// <param name="aspnetcoreEnvironment">組合根已讀取的 ASPNETCORE_ENVIRONMENT 後備值。</param>
    /// <returns>首個非 null 名稱；兩者皆未設定時為 Production，隨後仍須通過守門。</returns>
    public static string ResolveEnvironmentName(string? dotnetEnvironment, string? aspnetcoreEnvironment) =>
        dotnetEnvironment ?? aspnetcoreEnvironment ?? Environments.Production;

    /// <summary>
    /// 先拒絕所有非 Development 的環境，再驗證當次有效 HTTPS 組織端點及部署允許清單。
    /// 必須由組合根在建立 CRM、通知或背景 owner 前呼叫；不重新解析環境來源，也不接管組態。
    /// </summary>
    /// <param name="configuration">主機擁有的有效組態；清單與 ServerUrl 都不能取自 HTTP。</param>
    /// <param name="environment">組合根按 D27 統一後的環境快照；不受組態內其他環境鍵覆寫。</param>
    /// <exception cref="ArgumentNullException">任一借入依賴為 null，表示組合根使用錯誤。</exception>
    /// <exception cref="InvalidOperationException">
    /// 環境、端點或允許清單不符時拒絕啟動；訊息只含固定理由及鍵名，不含輸入值。
    /// </exception>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "GoodShepherd2 目前只允許 Development，其他環境尚未開放啟動。");
        }

        var serverUrl = configuration["CrmConnection:ServerUrl"];
        if (serverUrl is null
            || !Uri.TryCreate(serverUrl, UriKind.Absolute, out var endpoint)
            || !endpoint.IsWellFormedOriginalString()
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || endpoint.HostNameType != UriHostNameType.Dns
            || !endpoint.IsDefaultPort
            || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0
            || !string.Equals(endpoint.AbsolutePath, OrganizationServicePath, StringComparison.Ordinal)
            || !HasExactOriginalPath(serverUrl)
            || !IsAllowedDevelopmentHost(endpoint.Host, configuration))
        {
            throw new InvalidOperationException(
                "CrmConnection:ServerUrl 必須是 GoodShepherd2:AllowedDevelopmentOrganizations 允許的完整 HTTPS 測試組織端點。");
        }
    }

    /// <summary>
    /// 同時核對原始路徑，避免 Uri 已消去 dot-segment 或正規化反斜線後才被誤認為合法；
    /// 空 userinfo 的 @ 分隔符也拒絕，不能只依 Uri.UserInfo 是否非空判斷。
    /// </summary>
    /// <param name="serverUrl">前置條件為已成功解析且協定為 HTTPS 的原始絕對 URI。</param>
    /// <returns>原始路徑恰為組織服務路徑且 authority 不帶 userinfo 分隔符時為 true。</returns>
    private static bool HasExactOriginalPath(string serverUrl)
    {
        var authorityAndPath = serverUrl.AsSpan(Uri.UriSchemeHttps.Length + 3);
        var pathIndex = authorityAndPath.IndexOf('/');
        return pathIndex >= 0
            && !authorityAndPath[..pathIndex].Contains('@')
            && authorityAndPath[pathIndex..].Equals(OrganizationServicePath.AsSpan(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 先限定唯一核定組織的完整 DNS label，再從當次部署清單逐項做 ordinal 精確比較；
    /// 不接受 suffix、substring、萬用字元、尾端句點或先 Trim 再放行的近似主機。
    /// 不複製或快取清單，故每次呼叫都重新驗證自己的組態且無跨主機保留。
    /// </summary>
    /// <param name="host">Uri 已驗證為 DNS 的主機，不含協定、埠或路徑。</param>
    /// <param name="configuration">主機借入的有效部署組態；本方法不取得其所有權。</param>
    /// <returns>組織 label 正確且短名稱或完整主機精確列於允許清單時為 true。</returns>
    private static bool IsAllowedDevelopmentHost(string host, IConfiguration configuration)
    {
        var firstDot = host.IndexOf('.', StringComparison.Ordinal);
        var firstLabel = firstDot < 0 ? host.AsSpan() : host.AsSpan(0, firstDot);
        if (host.EndsWith('.')
            || !firstLabel.Equals(DevelopmentOrganization.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var organization in configuration.GetSection(
            "GoodShepherd2:AllowedDevelopmentOrganizations").GetChildren())
        {
            if (string.Equals(organization.Value, DevelopmentOrganization, StringComparison.OrdinalIgnoreCase)
                || string.Equals(organization.Value, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
