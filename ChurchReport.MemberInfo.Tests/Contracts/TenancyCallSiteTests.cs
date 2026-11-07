/*
 * 檔案整體說明
 *
 * 目的與定位：從正式主機與共用處理器來源驗證集中解析；只掃本工作樹，保留 Development token 安全驗證的獨立用途。
 *
 * 主要功能與流程：
 * 本檔宣告 TenancyCallSiteTests。
 * - 原九處及擴大盤點的十五處必須全部委派共用入口；P2-01 sender 搬入 Hosting 後，
 *   檢查真正新 owner 並確認產品 facade 不重複解析；其他來源不得藏有 token key 解析副本。
 * - 只由本測試輸出向上尋找同時擁有方案與既定主機的工作樹，不跨父工作樹掃描。
 * 測試案例入口包含 ChurchReport_AllCallSites_UseResolver、Root。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>從正式主機與共用處理器來源驗證集中解析；只掃本工作樹，保留 Development token 安全驗證的獨立用途。</summary>
public sealed class TenancyCallSiteTests
{
    /// <summary>原九處及擴大盤點的十五處必須全部委派共用入口；其他產品來源不得藏有新的 token key 解析副本。</summary>
    [Fact(DisplayName = "全部十五處LINE解析消費者委派Tenancy")]
    public void ChurchReport_AllCallSites_UseResolver()
    {
        var root = Root();
        string[] expected =
        [
            "LineMessagingProcessor/LineMessagingProcessorClass.cs",
            "SpeechMessageProducts.ChurchReport/Startup.cs",
            "SpeechMessage.Hosting/LineExceptionSender.cs",
            "SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs",
            "SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs",
            "SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs",
            "SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs",
            "SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs",
            "SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs",
            "SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs",
            "SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs",
            "SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs",
            "SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs",
            "SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs",
            "SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs"
        ];
        foreach (var path in expected)
        {
            var text = File.ReadAllText(Path.Combine(root, path));
            Assert.True(text.Contains("LegacyLineChannelAccess.", StringComparison.Ordinal)
                || text.Contains("ChurchReportLineChannelAccess.", StringComparison.Ordinal), path);
        }
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var project in new[] { "SpeechMessageProducts.ChurchReport", "LineMessagingProcessor", "SpeechMessage.Hosting" })
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", options)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            // 唯一安全 validator 可讀鍵做來源檢查；舊產品 facade 不再擁有此豁免。
            if (project == "SpeechMessage.Hosting" && Path.GetFileName(path) == "StartupGuards.cs") continue;
            var text = File.ReadAllText(path);
            Assert.False(Regex.IsMatch(text, "LineMessaging:[^\\r\\n]*:ChannelAccessToken[\"\\]]",
                RegexOptions.CultureInvariant), Path.GetRelativePath(root, path));
        }
        var senderFacade = File.ReadAllText(Path.Combine(root,
            "SpeechMessageProducts.ChurchReport/Services/LineExceptionSender.cs"));
        Assert.Contains("new SpeechMessage.Hosting.LineExceptionSender(", senderFacade, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOptionalAccessToken(", senderFacade, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpClient(", senderFacade, StringComparison.Ordinal);
        Assert.Contains("AddSpeechMessageTenancy(", File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.ChurchReport", "Startup.cs")), StringComparison.Ordinal);
    }

    /// <summary>只由本測試輸出向上尋找同時擁有方案與既定主機的工作樹，不跨父工作樹掃描。</summary>
    /// <returns>本次驗收的唯一方案根。</returns>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Solution root was not found.");
    }
}
