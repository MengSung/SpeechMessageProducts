/*
 * 檔案整體說明
 *
 * 目的與定位：舊 ChurchReport 管理者告警的相容入口。所有呼叫統一交給組合根的診斷 owner， 先完成 Exception.log 寫入／flush，再排入 LINE；不再自行建立長命 LINE client
 * 或同步等網路。 舊文字參數可能含憑證、姓名或完整例外，因此不讀取、不留存、不傳送，改以編譯期 caller 定位。 新增程式應直接傳 Exception 至 ExceptionReporting.Report，以保留型別與
 * stack 符號。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchReportLineAdminNotificationService。
 * - 既有產品管理者接收者；僅用作受信任部署預設值，不接受 request 覆寫。
 * - 保留舊二參數簽章；source／message 不外送，caller 僅由編譯器提供方法名稱。
 * - 以真實例外保留型別與可定位堆疊；共用 owner 仍先落檔 flush 再排入 LINE。
 * - 保留指定分類的舊三參數呼叫；不以動態分類或原始文字決定收件人或診斷路徑。
 * 主要操作入口包含 NotifyDefaultError、ReportException。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/NewPersonController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs 追查目前工作樹中的合作／使用位置。
 */

using System;
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

    /// <summary>以真實例外保留型別與可定位堆疊；共用 owner 仍先落檔 flush 再排入 LINE。</summary>
    public static void ReportException(string source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ExceptionReporting.Report(exception, source + ".Exception");
    }

    /// <summary>保留指定分類的舊三參數呼叫；不以動態分類或原始文字決定收件人或診斷路徑。</summary>
    public static void NotifyDefaultError(string source, string category, string errorMessage)
    {
        ExceptionReporting.Report(null, source + ".LegacyAdminError");
    }
}
