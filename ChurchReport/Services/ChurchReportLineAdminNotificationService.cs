using System;
using System.Collections.Generic;
using System.IO;
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

    public static void NotifyDefaultError(string source, string category, string errorMessage)
    {
        s_default.Value.NotifyError(source, category, errorMessage);
    }

    /// <summary>
    /// 使用預設錯誤分類送出管理者告警。
    /// 此通知屬於輔助告警，LINE 發送失敗不可蓋掉原本的業務例外。
    /// </summary>
    public void NotifyError(string source, string errorMessage)
    {
        NotifyError(source, DefaultCategory, errorMessage);
    }

    /// <summary>
    /// 使用指定分類送出管理者告警。
    /// category 讓「錯誤」、「註冊錯誤」這類 ChurchReport 產品語意留在產品層。
    /// </summary>
    public void NotifyError(string source, string category, string errorMessage)
    {
        try
        {
            var normalizedSource = Normalize(source, DefaultProductSource);
            var normalizedCategory = Normalize(category, DefaultCategory);
            var message = FormatAdminMessage(normalizedSource, normalizedCategory, errorMessage);

            _lineNotificationWorkflow.SendAsync(new LineNotificationRequest
            {
                Recipient = LineNotificationRecipient.User(_adminLineUserId),
                Content = LineNotificationContent.TextMessage(message),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "ChurchReport.LineAdminErrorNotification",
                    ["productSource"] = normalizedSource,
                    ["category"] = normalizedCategory
                }
            }).GetAwaiter().GetResult();
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
