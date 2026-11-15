/*
 * 檔案整體說明
 *
 * 目的與定位：以正式清單及共用六案驗證 P5-05 週報部署的三入口，避免測試使用
 * 合成清單而漏掉實際模組、前像、活動或舊型別對照錯誤。
 * 主要功能與流程：嚴格 loader 讀正式 JSON，統計為同步 PostOperation、通知為
 * 非同步 PostOperation，兩者有精確 PreImage 與篩選欄位、Shadow、空 secure、
 * goodshepherdback 停用；Workflows 的本模組生成活動保留真正舊 namespace。
 * 使用邊界與維護：僅讀取本工作樹公開結構與合成期望，不建立 SDK、不讀 Secrets，
 * 不啟用步驟或流程、不推播。reader 同步關閉串流，候選及集合只存活於本次測試，
 * 沒有跨使用者資料或需排空的背景資源；這不代表實機影子比對已完成。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護正式週報清單與共用情境，實機延後不得冒充差異為零。</summary>
public sealed class WeeklyReportRegistrationTests
{
    /// <summary>用真正 loader 驗證離線文件及三入口註冊，不以檔案文字搜尋代替結構契約。</summary>
    [Fact]
    public void WeeklyReport_Registration_DisabledShadowWithLegacyReferences()
    {
        var root = Root();
        var scenarios = ShadowScenarioFiles.Load(root, "weeklyreports");
        Assert.Equal(6, scenarios.Count);
        Assert.All(scenarios, item =>
        {
            Assert.Equal("reading", item.Basis);
            Assert.False(item.LiveShadow);
        });
        var document = RegistrationManifestLoader.LoadDocument(root);
        var steps = document.Assemblies.SelectMany(item => item.Steps)
            .Where(item => item.Module == "weeklyreports").ToArray();
        Assert.Equal(2, steps.Length);
        foreach (var step in steps)
        {
            Assert.Equal("Update", step.Message);
            Assert.Equal("new_group_present_weekly_report", step.Entity);
            Assert.Equal("PostOperation", step.Stage);
            Assert.Equal("{\"mode\":\"Shadow\"}", step.UnsecureConfig);
            Assert.Empty(step.SecureConfig);
            Assert.False(step.EnabledByOrganization["goodshepherdback"]);
            Assert.Equal("reading", step.ShadowReport.Basis);
            Assert.Null(step.ShadowReport.Differences);
            Assert.Single(step.Legacy);
            var image = Assert.Single(step.Images);
            Assert.Equal("PreImage", image.Name);
            Assert.Equal("PreImage", image.Type);
        }
        var stats = Assert.Single(steps, item => item.Type.EndsWith(".WeeklyReportStatisticsPlugin", StringComparison.Ordinal));
        Assert.Equal("Synchronous", stats.Mode);
        Assert.Equal("new_saved_flag", Assert.Single(stats.FilteringAttributes));
        var notice = Assert.Single(steps, item => item.Type.EndsWith(".WeeklyReportNotificationPlugin", StringComparison.Ordinal));
        Assert.Equal("Asynchronous", notice.Mode);
        Assert.Equal("new_paster_reply", Assert.Single(notice.FilteringAttributes));
        Assert.Equal("WeeklyReportLinePlugInDatabase", Assert.Single(notice.Legacy).Assembly);
        var workflows = Assert.Single(document.Assemblies, item => item.AssemblyName == "SpeechMessage.Dynamics.Workflows");
        Assert.Empty(workflows.Steps);
        var activity = Assert.Single(workflows.Activities, item => item.Module == "weeklyreports");
        Assert.Equal("weeklyreports", activity.Module);
        Assert.EndsWith(".WeeklyReportGenerationActivity", activity.Type, StringComparison.Ordinal);
        Assert.Equal("WeeklyReportWorkflowNameSpace.WeeklyReportWorkflow", Assert.Single(activity.Legacy).Type);
        Assert.Equal("reading", activity.ShadowReport.Basis);
        Assert.Null(activity.ShadowReport.Differences);
    }

    /// <summary>只沿當前測試輸出向上找方案，不跨到父工作樹或依賴程序工作目錄。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Test repository root unavailable.");
    }
}
