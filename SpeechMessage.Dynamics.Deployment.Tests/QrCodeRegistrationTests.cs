/*
 * 檔案整體說明
 * 目的與定位：P5-09 三個 QR 活動的正式註冊與閱讀情境格式守門，不建立實機流程。
 * 主要流程：用正式嚴格 reader 讀六案 scenarios/expected，確保 alias、參照、觀察及
 * 完整舊入口對應有效，再核對 Workflows 三個新活動與實際唯讀匯出的舊型別名稱。
 * 使用邊界與維護：只讀目前工作樹公開 JSON，不讀 Secrets、不建立 SDK、不啟用任何
 * 新舊步驟。每次 loader 自己關閉檔案與 JSON owner，候選僅屬本測試，沒有 Session、
 * 可變 static、timer 或背景工作；格式通過不能宣稱實機影子差異或已部署成功。
 * 故障防護：多活動、錯舊入口、reading誤標、differences冒稱零或活動被誤註冊為step
 * 都必須使本測試失敗，不能藉寬鬆讀取忽略本包真實部署契約。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>核對三個薄活動與reading情境，拒絕新增可啟用的Plugin步驟或偽實機證據。</summary>
public sealed class QrCodeRegistrationTests
{
    /// <summary>六案嚴格格式與三活動一對一；全部reading、differences=null且無本包Plugin步驟。</summary>
    [Fact]
    public void QrCodes_Registration_ThreeActivitiesWithReadingBasis()
    {
        var root = Root();
        var scenarios = ShadowScenarioFiles.Load(root, "qrcodes");
        Assert.Equal(6, scenarios.Count);
        Assert.All(scenarios, item => { Assert.True(item.IsActivity); Assert.False(item.LiveShadow); Assert.Equal("reading", item.Basis); });
        var manifest = RegistrationManifestLoader.LoadDocument(root);
        Assert.Empty(manifest.Assemblies.SelectMany(x => x.Steps).Where(x => x.Module == "qrcodes"));
        var assembly = Assert.Single(manifest.Assemblies, x => x.AssemblyName == "SpeechMessage.Dynamics.Workflows");
        var activities = assembly.Activities.Where(x => x.Module == "qrcodes").ToArray();
        Assert.Equal(3, activities.Length);
        var names = new[] { "MeetingQrCodeActivity", "MeetingScanCountActivity", "PersonalQrCodeActivity" };
        var legacy = new[] { "SundayQrCodeWorkflow.QrCodeWorkflow", "CalculateQrCodeScanNumberWorkflow.CalculateQrCodeScanNumberWorkflow", "PersonalQrCodeWorkflow.PersonalQrCodeWorkflowClass" };
        for (var i = 0; i < names.Length; i++)
        {
            var activity = Assert.Single(activities, x => x.Type == "SpeechMessage.Dynamics.Workflows.QrCodes." + names[i]);
            Assert.Equal(names[i], activity.DisplayName);
            Assert.Equal(legacy[i], Assert.Single(activity.Legacy).Type);
            Assert.Equal("reading", activity.ShadowReport.Basis);
            Assert.Null(activity.ShadowReport.Differences);
        }
    }

    /// <summary>僅從目前測試輸出向上定位方案，不向另一個worktree尋找後備檔。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Current repository root unavailable.");
    }
}
