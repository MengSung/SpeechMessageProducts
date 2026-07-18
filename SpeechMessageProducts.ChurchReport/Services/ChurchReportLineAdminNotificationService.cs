// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ChurchReportLineAdminNotificationService
// 主要成員：NotifyDefaultError、NotifyError、FormatAdminMessage、Normalize、CreateDefaultWorkflow、GetLineChannelAccessToken
// 引用命名空間：System、System.Collections.Generic、System.IO、LineMessagingProcessor、LineMessagingProcessor.Workflows、Microsoft.Extensions.Configuration
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Services;

/// <summary>
/// ChurchReport 產品層的 LINE 管理者告警服務。
/// 共用 LINE 專案只負責發送 LINE 訊息；管理者 LINE ID、告警格式與產品語意都留在 ChurchReport。
/// </summary>
public sealed class ChurchReportLineAdminNotificationService
{
    public const string DefaultAdminLineUserId = "U7638e4ed509708a3573ba6d69970583d";

    private const string DefaultProductSource = "ChurchReport";
    private const string DefaultCategory = "錯誤";

    private static readonly Lazy<IConfiguration> s_configuration = new(() =>
        new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build());

    private static readonly Lazy<ChurchReportLineAdminNotificationService> s_default = new(() =>
        new ChurchReportLineAdminNotificationService(
            CreateDefaultWorkflow(),
            DefaultAdminLineUserId));

    private readonly ILineNotificationWorkflow _lineNotificationWorkflow;
    private readonly string _adminLineUserId;

    public ChurchReportLineAdminNotificationService(
        ILineNotificationWorkflow lineNotificationWorkflow)
        : this(lineNotificationWorkflow, DefaultAdminLineUserId)
    {
    }

    public ChurchReportLineAdminNotificationService(
        ILineNotificationWorkflow lineNotificationWorkflow,
        string adminLineUserId)
    {
        _lineNotificationWorkflow = lineNotificationWorkflow
            ?? throw new ArgumentNullException(nameof(lineNotificationWorkflow));
        _adminLineUserId = string.IsNullOrWhiteSpace(adminLineUserId)
            ? DefaultAdminLineUserId
            : adminLineUserId.Trim();
    }

    /// <summary>
    /// 使用預設設定送出 ChurchReport 管理者錯誤通知。
    /// 這個靜態入口保留給尚未導入 DI 的舊流程，避免每個舊呼叫點自行 new LINE processor。
    /// </summary>
    public static void NotifyDefaultError(string source, string errorMessage)
    {
        s_default.Value.NotifyError(source, errorMessage);
    }

    public static Task NotifyDefaultErrorAsync(string source, string errorMessage)
    {
        return s_default.Value.NotifyErrorAsync(source, errorMessage);
    }

    public static void NotifyDefaultError(string source, string category, string errorMessage)
    {
        s_default.Value.NotifyError(source, category, errorMessage);
    }

    public static Task NotifyDefaultErrorAsync(string source, string category, string errorMessage)
    {
        return s_default.Value.NotifyErrorAsync(source, category, errorMessage);
    }

    /// <summary>
    /// 使用預設錯誤分類送出管理者告警。
    /// 此通知屬於輔助告警，LINE 發送失敗不可蓋掉原本的業務例外。
    /// </summary>
    public void NotifyError(string source, string errorMessage)
    {
        NotifyError(source, DefaultCategory, errorMessage);
    }

    public Task NotifyErrorAsync(string source, string errorMessage)
    {
        return NotifyErrorAsync(source, DefaultCategory, errorMessage);
    }

    /// <summary>
    /// 使用指定分類送出管理者告警。
    /// category 讓「錯誤」、「註冊錯誤」這類 ChurchReport 產品語意留在產品層。
    /// </summary>
    public void NotifyError(string source, string category, string errorMessage)
    {
        NotifyErrorAsync(source, category, errorMessage).GetAwaiter().GetResult();
    }

    public async Task NotifyErrorAsync(string source, string category, string errorMessage)
    {
        try
        {
            var normalizedSource = Normalize(source, DefaultProductSource);
            var normalizedCategory = Normalize(category, DefaultCategory);
            var message = FormatAdminMessage(normalizedSource, normalizedCategory, errorMessage);

            await _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(_adminLineUserId),
                Content = LineNotificationContent.TextMessage(message),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "ChurchReport.LineAdminErrorNotification",
                    ["productSource"] = normalizedSource,
                    ["category"] = normalizedCategory
                }
            });
        }
        catch
        {
            // 管理者告警是 best-effort 輔助訊息；LINE API 或 token 設定失敗時，不能改變原本例外流程。
        }
    }

    private static string FormatAdminMessage(string source, string category, string? errorMessage)
    {
        var normalizedMessage = errorMessage ?? string.Empty;
        if (category == DefaultCategory)
        {
            return $"{source}: {category} => {normalizedMessage}";
        }

        return $"{source} : {category} => {normalizedMessage}";
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static ILineNotificationWorkflow CreateDefaultWorkflow()
    {
        var channelAccessToken = GetLineChannelAccessToken();
        return new LineNotificationWorkflow(new LineMessagingProcessorClass(channelAccessToken));
    }

    private static string GetLineChannelAccessToken()
    {
        try
        {
            var configuration = s_configuration.Value;
            var organization = configuration["CrmConnection:Organization"];
            if (!string.IsNullOrWhiteSpace(organization))
            {
                var configKey = char.ToUpperInvariant(organization[0])
                    + organization.Substring(1).ToLowerInvariant();
                var token = configuration[$"LineMessaging:{configKey}:ChannelAccessToken"];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }

            var defaultOrg = configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
            return configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"] ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
