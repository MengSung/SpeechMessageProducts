
using ToolUtilityNameSpace.Diagnostics;

namespace ChurchReport.Services;

/// <summary>
/// 舊 ChurchReport 管理者告警的相容入口。所有呼叫統一交給組合根的診斷 owner，
/// 先完成 Exception.log 寫入／flush，再排入 LINE；不再自行建立長命 LINE client 或同步等網路。
/// 舊文字參數可能含憑證、姓名或完整例外，因此不讀取、不留存、不傳送，改以編譯期 caller 定位。
/// 新增程式應直接傳 Exception 至 ExceptionReporting.Report，以保留型別與 stack 符號。
/// </summary>
public static class ChurchReportLineAdminNotificationService
{
    /// <summary>既有產品管理者接收者；僅用作受信任部署預設值，不接受 request 覆寫。</summary>
    public const string DefaultAdminLineUserId = "U7638e4ed509708a3573ba6d69970583d";

    /// <summary>保留舊二參數簽章；source／message 不外送，caller 僅由編譯器提供方法名稱。</summary>
    public static void NotifyDefaultError(string source, string errorMessage)
    {
        ExceptionReporting.Report(null, source + ".LegacyAdminError");
    }

    /// <summary>保留指定分類的舊三參數呼叫；不以動態分類或原始文字決定收件人或診斷路徑。</summary>
    public static void NotifyDefaultError(string source, string category, string errorMessage)
    {
        ExceptionReporting.Report(null, source + ".LegacyAdminError");
    }
}
